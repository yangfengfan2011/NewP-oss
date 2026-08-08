using ICP.Platform.Modeling;
using ICP.Platform.Modeling.Definitions;
using ICP.Platform.Persistence.Abstractions;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Persistence.EfCore.Extensions;
using ICP.Platform.Persistence.Ownership;
using ICP.Platform.Persistence.Planning;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ICP.Platform.Persistence.Tests;

public sealed class PersistenceMigrationRunnerTests
{
    [Fact]
    public async Task MigrateAsync_ForbiddenModuleOperation_FailsBeforeOpeningConnection()
    {
        var migrationId = ForbiddenSqlMigration.MigrationId;
        var plan = BuildPlan(migrationId);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Persistence:Adapter"] = "EfCore",
                ["Persistence:Provider"] = "SqlServer",
                ["Persistence:AutoMigrate"] = "true",
                ["ConnectionStrings:SqlServer"] =
                    "Server=invalid.invalid,1;Database=never-connect;User Id=invalid;Password=REDACTED_FOR_OPEN_SOURCE;Connect Timeout=1;TrustServerCertificate=True;"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddEfCorePersistence(
            configuration,
            plan,
            WorktreeDatabaseIdentity.FromGitPaths(@"D:\repo\.git", @"D:\repo\.git"));
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var runner = scope.ServiceProvider.GetRequiredService<IPersistenceMigrationRunner>();

        var exception = await Assert.ThrowsAsync<PersistencePlanExecutionException>(
            () => runner.MigrateAsync());

        Assert.Equal(PersistenceMigrationFailureCodes.OperationForbidden, exception.FailureCode);
        Assert.Equal("Partner.AssetCatalog", exception.ModuleId);
        Assert.Equal(migrationId, exception.MigrationId);
    }

    [Fact]
    public async Task MigrateAsync_FilteredModuleIndex_FailsBeforeOpeningConnection()
    {
        var migrationId = FilteredIndexMigration.MigrationId;
        var plan = BuildPlan(migrationId);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Persistence:Adapter"] = "EfCore",
                ["Persistence:Provider"] = "SqlServer",
                ["Persistence:AutoMigrate"] = "true",
                ["ConnectionStrings:SqlServer"] =
                    "Server=invalid.invalid,1;Database=never-connect;User Id=invalid;Password=REDACTED_FOR_OPEN_SOURCE;Connect Timeout=1;TrustServerCertificate=True;"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddEfCorePersistence(
            configuration,
            plan,
            WorktreeDatabaseIdentity.FromGitPaths(@"D:\repo\.git", @"D:\repo\.git"));
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var runner = scope.ServiceProvider.GetRequiredService<IPersistenceMigrationRunner>();

        var exception = await Assert.ThrowsAsync<PersistencePlanExecutionException>(
            () => runner.MigrateAsync());

        Assert.Equal(PersistenceMigrationFailureCodes.OperationForbidden, exception.FailureCode);
        Assert.Equal(migrationId, exception.MigrationId);
    }

    private static CompiledPersistencePlan BuildPlan(string migrationId)
    {
        var models = TestPersistencePlan.Create().Models
            .Append(new ModelDefinition(
                "Asset",
                "Asset",
                ModelKind.Entity,
                [
                    new FieldDefinition(
                        "Id",
                        "Id",
                        FieldKind.Scalar,
                        FieldValueType.ObjectId,
                        "string",
                        Required: true,
                        IsPrimaryKey: true,
                        IsAuditField: false)
                ],
                [],
                OwnerModuleId: "Partner.AssetCatalog",
                SchemaName: "np_partner_assetcatalog"))
            .ToArray();
        var modelDigest = PersistenceModelDigest.Compute(models);
        var stream = new ModuleMigrationStream(
            "Partner.AssetCatalog",
            PersistenceProvider.SqlServer,
            typeof(ForbiddenSqlMigration).Assembly.Location,
            "test-manifest.json",
            new string('b', 64),
            modelDigest,
            migrationId,
            IsLegacyBaseline: false,
            [
                new CompiledMigrationStep(
                    "000001",
                    migrationId,
                    null,
                    new string('c', 64),
                    new string('d', 64),
                    modelDigest,
                    "safe-expand")
            ]);
        return new CompiledPersistencePlan(
            "test-candidate",
            new string('a', 64),
            new string('a', 64),
            PersistenceProvider.SqlServer,
            modelDigest,
            [],
            models,
            [stream]);
    }
}

[DbContext(typeof(EfPlatformDbContext))]
[Migration(MigrationId)]
public sealed class ForbiddenSqlMigration : Migration
{
    public const string MigrationId = "20260809999900_ForbiddenSql";

    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("SELECT 1;");

    protected override void Down(MigrationBuilder migrationBuilder)
    {
    }
}

[DbContext(typeof(EfPlatformDbContext))]
[Migration(MigrationId)]
public sealed class FilteredIndexMigration : Migration
{
    public const string MigrationId = "20260809999800_FilteredIndex";

    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.CreateIndex(
            name: "IX_Asset_Filtered",
            schema: "np_partner_assetcatalog",
            table: "Asset",
            column: "Id",
            filter: "[Id] <> N''");

    protected override void Down(MigrationBuilder migrationBuilder)
    {
    }
}
