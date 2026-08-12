using System.Xml.Linq;
using ICP.Platform.HostApi.Extensions;
using ICP.Platform.HostApi.Readiness;
using ICP.Platform.Persistence.Abstractions;
using ICP.Platform.Persistence.Planning;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ICP.Platform.Persistence.Tests;

public sealed class PersistenceBootstrapTests
{
    [Fact]
    public void AddPlatformPersistence_RegistersOneScopedMigrationAndReadOnlyConsistencyKernel()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Persistence:Provider"] = "SqlServer",
                ["ConnectionStrings:SqlServer"] = "Server=(localdb)\\mssqllocaldb;Database=ICP_Platform;Trusted_Connection=True;TrustServerCertificate=True;"
            })
            .Build();

        services.AddPlatformPersistence(configuration, TestPersistencePlan.Create());

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
        using var scope = provider.CreateScope();
        var migration = scope.ServiceProvider.GetRequiredService<IPersistenceMigrationRunner>();
        var consistency = scope.ServiceProvider.GetRequiredService<IPersistenceConsistencyChecker>();

        Assert.Same(migration, consistency);
        Assert.DoesNotContain(
            services,
            descriptor => descriptor.ServiceType.Name.Contains("SchemaSync", StringComparison.Ordinal));
    }

    [Fact]
    public void HostApiProject_ContainsRuntimeTruthAssetsButNoGeneratedSourceContent()
    {
        var projectPath = Path.Combine(FindRepositoryRoot(), "apps", "host-api", "HostApi.csproj");
        var document = XDocument.Load(projectPath);
        var runtimeIncludes = new[]
        {
            @"..\..\modules\**\metadata\**\*.json",
            @"..\..\server\platform-modeling\metadata\storage-types.json"
        };

        foreach (var include in runtimeIncludes)
        {
            var content = Assert.Single(document.Descendants("Content"),
                element => string.Equals(element.Attribute("Include")?.Value, include, StringComparison.Ordinal));
            var condition = content.Attribute("Condition")?.Value
                ?? content.Parent?.Attribute("Condition")?.Value;

            Assert.True(string.IsNullOrWhiteSpace(condition));
            Assert.Equal("PreserveNewest", content.Element("CopyToOutputDirectory")?.Value);
        }

        Assert.DoesNotContain(
            document.Descendants("Content"),
            element => element.Attribute("Include")?.Value.Contains(
                @"DTOs\Generated",
                StringComparison.Ordinal) is true);
    }

    [Fact]
    public void RepositoryMigrationEntry_UsesDisposableHostPathWithoutRetiredSwitchOrExternalWriter()
    {
        var repositoryRoot = FindRepositoryRoot();
        var runtimePaths = new[]
        {
            Path.Combine(repositoryRoot, "apps", "host-api", "appsettings.json"),
            Path.Combine(repositoryRoot, "apps", "host-api", "appsettings.Development.json"),
            Path.Combine(repositoryRoot, "scripts", "dev.ps1"),
            Path.Combine(repositoryRoot, "server", "platform-persistence", "PersistenceOptions.cs"),
            Path.Combine(repositoryRoot, "server", "platform-persistence-efcore", "PersistenceMigrationRunner.cs")
        };
        Assert.All(runtimePaths, path =>
            Assert.DoesNotContain("AutoMigrate", File.ReadAllText(path), StringComparison.Ordinal));

        var smokePath = Path.Combine(
            repositoryRoot,
            "tests",
            "integration",
            "persistence",
            "Invoke-PersistenceSmoke.ps1");
        var smoke = File.ReadAllText(smokePath);
        Assert.Contains("Run-SqlServerSmoke.ps1", smoke, StringComparison.Ordinal);
        Assert.DoesNotContain("dotnet-ef", smoke, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("database update", smoke, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Start-Process", smoke, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ASPNETCORE_URLS", smoke, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    [InlineData("Acceptance")]
    public async Task ApplyPlatformPersistenceAsync_AllEnvironmentsRunMigrationThenReadOnlyCheckAndBecomeReady(
        string environmentName)
    {
        var events = new List<string>();
        var migrationRunner = new RecordingMigrationRunner(events: events);
        var consistencyChecker = new RecordingConsistencyChecker(events: events);
        await using var app = BuildApp(environmentName, migrationRunner, consistencyChecker);

        await app.ApplyPlatformPersistenceAsync();

        Assert.Equal(["migration", "read-only-check"], events);
        Assert.Equal(1, migrationRunner.CallCount);
        Assert.Equal(1, consistencyChecker.CallCount);
        var readiness = await ReadinessAsync(app);
        Assert.Equal(ReadinessState.Ready, readiness.State);
        Assert.Null(readiness.FailureCode);
    }

    [Fact]
    public async Task ApplyPlatformPersistenceAsync_WhenMigrationFails_DoesNotRunReadOnlyCheck()
    {
        var migrationRunner = new RecordingMigrationRunner(new InvalidOperationException("migration failed"));
        var consistencyChecker = new RecordingConsistencyChecker();
        await using var app = BuildApp("Development", migrationRunner, consistencyChecker);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            app.ApplyPlatformPersistenceAsync());

        Assert.Equal("migration failed", exception.Message);
        Assert.Equal(1, migrationRunner.CallCount);
        Assert.Equal(0, consistencyChecker.CallCount);
        var readiness = await ReadinessAsync(app);
        Assert.Equal(ReadinessState.Failed, readiness.State);
        Assert.Equal(PersistenceReadinessFailureCodes.BootstrapFailed, readiness.FailureCode);
    }

    [Fact]
    public async Task ApplyPlatformPersistenceAsync_WhenReadOnlyCheckFails_RemainsNotReady()
    {
        var failure = new PersistencePlanExecutionException(
            PersistenceConsistencyFailureCodes.SchemaTargetMismatch,
            "platform.persistence");
        var migrationRunner = new RecordingMigrationRunner();
        var consistencyChecker = new RecordingConsistencyChecker(failure);
        await using var app = BuildApp("Production", migrationRunner, consistencyChecker);

        var exception = await Assert.ThrowsAsync<PersistencePlanExecutionException>(() =>
            app.ApplyPlatformPersistenceAsync());

        Assert.Equal(PersistenceConsistencyFailureCodes.SchemaTargetMismatch, exception.FailureCode);
        Assert.Equal(1, migrationRunner.CallCount);
        Assert.Equal(1, consistencyChecker.CallCount);
        var readiness = await ReadinessAsync(app);
        Assert.Equal(ReadinessState.Failed, readiness.State);
        Assert.Equal(PersistenceConsistencyFailureCodes.SchemaTargetMismatch, readiness.FailureCode);
    }

    [Fact]
    public async Task ApplyPlatformPersistenceAsync_WhenRunnerReportsDatabaseAhead_BlocksBeforeReadOnlyCheck()
    {
        var migrationRunner = new RecordingMigrationRunner(databaseAhead: true);
        var consistencyChecker = new RecordingConsistencyChecker();
        await using var app = BuildApp("Development", migrationRunner, consistencyChecker);

        var exception = await Assert.ThrowsAsync<PersistencePlanExecutionException>(() =>
            app.ApplyPlatformPersistenceAsync());

        Assert.Equal(PersistenceHistoryFailureCodes.FrontierMismatch, exception.FailureCode);
        Assert.Equal(0, consistencyChecker.CallCount);
        var readiness = await ReadinessAsync(app);
        Assert.Equal(ReadinessState.Failed, readiness.State);
        Assert.Equal(PersistenceHistoryFailureCodes.FrontierMismatch, readiness.FailureCode);
    }

    [Theory]
    [InlineData(PersistenceHistoryFailureCodes.VersionUnsupported)]
    [InlineData(PersistenceHistoryFailureCodes.Duplicate)]
    [InlineData(PersistenceHistoryFailureCodes.Unknown)]
    [InlineData(PersistenceHistoryFailureCodes.NonPrefix)]
    [InlineData(PersistenceHistoryFailureCodes.FrontierMismatch)]
    [InlineData(PersistenceHistoryFailureCodes.FactMissing)]
    [InlineData(PersistenceHistoryFailureCodes.OwnerMismatch)]
    [InlineData(PersistenceHistoryFailureCodes.BundleMismatch)]
    [InlineData(PersistenceHistoryFailureCodes.ContentMismatch)]
    [InlineData(PersistenceHistoryFailureCodes.ModelMismatch)]
    [InlineData(PersistenceHistoryFailureCodes.PlanMismatch)]
    [InlineData(PersistenceHistoryFailureCodes.ProviderTargetMismatch)]
    [InlineData(PersistenceHistoryFailureCodes.ProviderTransitionMismatch)]
    public async Task ApplyPlatformPersistenceAsync_HistoryFailure_PreservesStableNotReadyCode(
        string failureCode)
    {
        var migrationRunner = new RecordingMigrationRunner(
            new PersistencePlanExecutionException(failureCode, "platform.persistence"));
        var consistencyChecker = new RecordingConsistencyChecker();
        await using var app = BuildApp("Production", migrationRunner, consistencyChecker);

        var exception = await Assert.ThrowsAsync<PersistencePlanExecutionException>(() =>
            app.ApplyPlatformPersistenceAsync());

        Assert.Equal(failureCode, exception.FailureCode);
        Assert.Equal(0, consistencyChecker.CallCount);
        var readiness = await ReadinessAsync(app);
        Assert.Equal(ReadinessState.Failed, readiness.State);
        Assert.Equal(failureCode, readiness.FailureCode);
    }

    private static WebApplication BuildApp(
        string environmentName,
        RecordingMigrationRunner migrationRunner,
        RecordingConsistencyChecker consistencyChecker)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = environmentName,
            ContentRootPath = Directory.GetCurrentDirectory()
        });
        builder.Services.AddSingleton<IPersistenceMigrationRunner>(migrationRunner);
        builder.Services.AddSingleton<IPersistenceConsistencyChecker>(consistencyChecker);
        builder.Services.AddSingleton<PersistenceReadinessState>();
        return builder.Build();
    }

    private static async Task<ReadinessContribution> ReadinessAsync(WebApplication app)
    {
        var contributor = new PersistenceReadinessContributor(
            app.Services.GetRequiredService<PersistenceReadinessState>());
        return await contributor.CheckAsync(CancellationToken.None);
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "ICP-Platform.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("无法定位仓库根目录。");
    }

    private sealed class RecordingMigrationRunner(
        Exception? exception = null,
        bool databaseAhead = false,
        List<string>? events = null) : IPersistenceMigrationRunner
    {
        public int CallCount { get; private set; }

        public bool DatabaseAhead => databaseAhead;

        public Task MigrateAsync(CancellationToken cancellationToken = default)
        {
            CallCount++;
            events?.Add("migration");
            return exception is null ? Task.CompletedTask : Task.FromException(exception);
        }
    }

    private sealed class RecordingConsistencyChecker(
        Exception? exception = null,
        List<string>? events = null) : IPersistenceConsistencyChecker
    {
        public int CallCount { get; private set; }

        public Task CheckAsync(CancellationToken cancellationToken = default)
        {
            CallCount++;
            events?.Add("read-only-check");
            return exception is null ? Task.CompletedTask : Task.FromException(exception);
        }
    }
}
