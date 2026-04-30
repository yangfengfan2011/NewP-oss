using System.Xml.Linq;
using ICP.Platform.HostApi.Extensions;
using ICP.Platform.Persistence.Abstractions;
using ICP.Platform.Persistence.DevSync.Coordination;
using ICP.Platform.Persistence.DevSync.Execution;
using ICP.Platform.Persistence.DevSync.Planning;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace ICP.Platform.Persistence.Tests;

public class DevelopmentSchemaSyncBootstrapTests
{
    [Fact]
    public void AddPlatformPersistence_WhenDevelopmentSchemaSyncEnabled_RegistersCoordinator()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Persistence:Provider"] = "SqlServer",
                ["Persistence:AutoMigrate"] = "true",
                ["DevelopmentSchemaSync:Enabled"] = "true",
                ["ConnectionStrings:SqlServer"] = "Server=(localdb)\\mssqllocaldb;Database=ICP_Platform;Trusted_Connection=True;TrustServerCertificate=True;"
            })
            .Build();

        services.AddPlatformPersistence(configuration);

        Assert.Contains(services, item => item.ServiceType.Name.Contains("DevelopmentSchemaSyncCoordinator", StringComparison.Ordinal));
    }

    [Fact]
    public void AddPlatformPersistence_WhenDevelopmentSchemaSyncEnabled_ResolvesCoordinatorAndOptions()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Persistence:Provider"] = "SqlServer",
                ["Persistence:AutoMigrate"] = "true",
                ["DevelopmentSchemaSync:Enabled"] = "true",
                ["ConnectionStrings:SqlServer"] = "Server=(localdb)\\mssqllocaldb;Database=ICP_Platform;Trusted_Connection=True;TrustServerCertificate=True;"
            })
            .Build();

        services.AddPlatformPersistence(configuration);

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
        using var scope = provider.CreateScope();

        var options = scope.ServiceProvider.GetRequiredService<DevelopmentSchemaSyncOptions>();
        var coordinator = scope.ServiceProvider.GetRequiredService<DevelopmentSchemaSyncCoordinator>();

        Assert.Equal("SqlServer", options.Provider);
        Assert.Contains("ICP_Platform", options.ConnectionString, StringComparison.Ordinal);
        Assert.True(options.ApplyChanges);
        Assert.NotNull(options.ConfirmApply);
        Assert.True(options.ConfirmApply(new SchemaPlan(
            new SchemaPlanSummary(0, 0, 0, 0, 0),
            [],
            [],
            [])));
        Assert.NotNull(coordinator);
    }

    [Fact]
    public void HostApiProject_RuntimeModelTruthAssets_AreCopiedForAllConfigurations()
    {
        var projectPath = Path.Combine(FindRepositoryRoot(), "apps", "host-api", "HostApi.csproj");
        var document = XDocument.Load(projectPath);
        var runtimeIncludes = new[]
        {
            @"..\..\modules\**\metadata\models\*.model.json",
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
    }

    [Fact]
    public void HostApiProject_GeneratedDevSyncAssets_AreCopiedOnlyForDebugOrExplicitOptIn()
    {
        var projectPath = Path.Combine(FindRepositoryRoot(), "apps", "host-api", "HostApi.csproj");
        var document = XDocument.Load(projectPath);
        var devSyncIncludes = new[]
        {
            @"..\..\server\modules\**\DTOs\Generated\*Dto.g.cs",
            @"..\..\server\modules\**\DTOs\Generated\*BaseDtoSignatureProvider.g.cs"
        };

        foreach (var include in devSyncIncludes)
        {
            var content = Assert.Single(document.Descendants("Content"),
                element => string.Equals(element.Attribute("Include")?.Value, include, StringComparison.Ordinal));
            var condition = content.Attribute("Condition")?.Value
                ?? content.Parent?.Attribute("Condition")?.Value;

            Assert.False(string.IsNullOrWhiteSpace(condition));
            Assert.Contains("EnableDevelopmentSchemaSyncAssets", condition, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task ApplyPlatformPersistenceAsync_WhenDevelopmentSchemaSyncBlocks_ThrowsInvalidOperationException()
    {
        var migrationRunner = new RecordingMigrationRunner();
        var applyCalled = false;
        await using var app = BuildApp(
            Environments.Development,
            "SqlServer",
            developmentSchemaSyncEnabled: true,
            DevelopmentSchemaSyncCoordinator.CreateForTests(
                ModelSourceInventory.Success(["Role"]),
                signaturesMatch: true,
                new SchemaPlan(
                    new SchemaPlanSummary(1, 1, 0, 1, 0),
                    ["Role"],
                    [new SchemaOperation("DropColumn", "Confirm", "Role", "Description", null, "drop Description", [])],
                    []),
                applyConfirmation: static _ => true,
                applyAsync: (_, _) =>
                {
                    applyCalled = true;
                    return Task.FromResult(new ApplyResult(true, false, 1, 1));
                }),
            migrationRunner);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            app.ApplyPlatformPersistenceAsync());

        Assert.Contains("Confirm", exception.Message, StringComparison.Ordinal);
        Assert.False(applyCalled);
        Assert.Equal(0, migrationRunner.CallCount);
    }

    [Fact]
    public async Task ApplyPlatformPersistenceAsync_WhenDevelopmentSchemaSyncSafePlanAllowsStartup_AppliesAndSkipsMigrationRunner()
    {
        var migrationRunner = new RecordingMigrationRunner();
        var applyCalled = false;
        await using var app = BuildApp(
            Environments.Development,
            "SqlServer",
            developmentSchemaSyncEnabled: true,
            DevelopmentSchemaSyncCoordinator.CreateForTests(
                ModelSourceInventory.Success(["Role"]),
                signaturesMatch: true,
                new SchemaPlan(
                    new SchemaPlanSummary(1, 1, 1, 0, 0),
                    ["Role"],
                    [new SchemaOperation("AddColumn", "Safe", "Role", "Description", null, "add Description", [])],
                    []),
                applyConfirmation: static _ => true,
                applyAsync: (_, _) =>
                {
                    applyCalled = true;
                    return Task.FromResult(new ApplyResult(true, false, 1, 1));
                }),
            migrationRunner);

        await app.ApplyPlatformPersistenceAsync();

        Assert.True(applyCalled);
        Assert.Equal(0, migrationRunner.CallCount);
    }

    [Fact]
    public async Task ApplyPlatformPersistenceAsync_WhenDevelopmentSchemaSyncSafeApplyFails_ThrowsAndSkipsMigrationRunner()
    {
        var migrationRunner = new RecordingMigrationRunner();
        await using var app = BuildApp(
            Environments.Development,
            "SqlServer",
            developmentSchemaSyncEnabled: true,
            DevelopmentSchemaSyncCoordinator.CreateForTests(
                ModelSourceInventory.Success(["Role"]),
                signaturesMatch: true,
                new SchemaPlan(
                    new SchemaPlanSummary(1, 1, 1, 0, 0),
                    ["Role"],
                    [new SchemaOperation("AddColumn", "Safe", "Role", "Description", null, "add Description", [])],
                    []),
                applyConfirmation: static _ => true,
                applyResult: new ApplyResult(false, false, 0, 0)),
            migrationRunner);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            app.ApplyPlatformPersistenceAsync());

        Assert.Contains("执行失败", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, migrationRunner.CallCount);
    }

    [Theory]
    [InlineData("Production", true, "SqlServer")]
    [InlineData("Development", false, "SqlServer")]
    [InlineData("Development", true, "MySql")]
    public async Task ApplyPlatformPersistenceAsync_WhenDevelopmentSchemaSyncBranchDoesNotMatch_RunsMigrationRunner(
        string environmentName,
        bool developmentSchemaSyncEnabled,
        string provider)
    {
        var migrationRunner = new RecordingMigrationRunner();
        await using var app = BuildApp(
            environmentName,
            provider,
            developmentSchemaSyncEnabled,
            CreateAllowedCoordinator(),
            migrationRunner);

        await app.ApplyPlatformPersistenceAsync();

        Assert.Equal(1, migrationRunner.CallCount);
    }

    private static WebApplication BuildApp(
        string environmentName,
        string provider,
        bool developmentSchemaSyncEnabled,
        DevelopmentSchemaSyncCoordinator coordinator,
        RecordingMigrationRunner migrationRunner)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = environmentName,
            ContentRootPath = Directory.GetCurrentDirectory()
        });

        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Persistence:Provider"] = provider,
            ["DevelopmentSchemaSync:Enabled"] = developmentSchemaSyncEnabled.ToString(),
            ["ConnectionStrings:SqlServer"] = "Server=(localdb)\\mssqllocaldb;Database=ICP_Platform;Trusted_Connection=True;TrustServerCertificate=True;"
        });
        builder.Services.AddSingleton(coordinator);
        builder.Services.AddSingleton<IPersistenceMigrationRunner>(migrationRunner);

        return builder.Build();
    }

    private static DevelopmentSchemaSyncCoordinator CreateAllowedCoordinator()
    {
        return DevelopmentSchemaSyncCoordinator.CreateForTests(
            ModelSourceInventory.Success(["Role"]),
            signaturesMatch: true,
            new SchemaPlan(
                new SchemaPlanSummary(1, 0, 0, 0, 0),
                ["Role"],
                [],
                []));
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

    private sealed class RecordingMigrationRunner : IPersistenceMigrationRunner
    {
        public int CallCount { get; private set; }

        public Task MigrateAsync(CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.CompletedTask;
        }
    }
}
