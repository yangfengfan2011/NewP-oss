using ICP.Platform.HostApi.Extensions;
using ICP.Platform.HostApi.Readiness;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Modeling.Metadata;
using ICP.Platform.Persistence.Abstractions;
using ICP.Platform.Persistence.Planning;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ICP.Platform.Persistence.Tests;

[Trait("Category", "ModuleMetadataConsumer")]
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
    public void HostRuntimeOutput_ContainsRuntimeTruthAssetsButNoGeneratedSourceContent()
    {
        // 声明根可嵌套；验证正式构建的消费资产，不把 csproj 的 Include 写法当契约。
        var repositoryRoot = FindRepositoryRoot();
        var testOutput = new DirectoryInfo(AppContext.BaseDirectory);
        var runtimeRoot = Path.Combine(repositoryRoot, "apps", "host-api", "bin", testOutput.Parent!.Name, testOutput.Name);
        var catalog = ComponentSourceCatalog.Discover(repositoryRoot);
        Assert.NotEmpty(catalog.Components);
        foreach (var component in catalog.Components)
        {
            AssertRuntimeCopy(component.CapsulePath, Path.Combine("modules", component.ModuleId, "plugin.json"));
            foreach (var source in catalog.EnumerateOwnFiles(component.ModuleId).Where(path =>
                Path.GetRelativePath(component.Root, path).Replace('\\', '/').StartsWith("metadata/", StringComparison.Ordinal)))
                AssertRuntimeCopy(source, Path.Combine("modules", component.ModuleId, Path.GetRelativePath(component.Root, source)));
        }
        // 历史 metadata owner 没有 Capsule，继续检查其既有输出，不授予组件发行身份。
        foreach (var root in ModuleAuthoringPaths.EnumerateModuleRoots(repositoryRoot).Where(root => !File.Exists(Path.Combine(root, "newp.plugin.json"))))
        {
            foreach (var source in Directory.EnumerateFiles(Path.Combine(root, "metadata"), "*.json", SearchOption.AllDirectories))
                AssertRuntimeCopy(source, Path.Combine("modules", ModuleAuthoringPaths.ReadModuleId(root), Path.GetRelativePath(root, source)));
        }
        const string storageTypes = "server/platform-modeling/metadata/storage-types.json";
        AssertRuntimeCopy(Path.Combine(repositoryRoot, storageTypes), storageTypes);
        Assert.DoesNotContain(
            Directory.EnumerateFiles(runtimeRoot, "*", SearchOption.AllDirectories),
            path => ("/" + Path.GetRelativePath(runtimeRoot, path).Replace('\\', '/')).Contains("/DTOs/Generated/", StringComparison.OrdinalIgnoreCase));

        void AssertRuntimeCopy(string source, string relativeOutput)
        {
            var output = Path.Combine(runtimeRoot, relativeOutput);
            Assert.True(File.Exists(output), "Runtime truth asset is missing: " + output);
            Assert.Equal(File.ReadAllBytes(source), File.ReadAllBytes(output));
        }
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
