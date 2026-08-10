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

    [Fact]
    public async Task MigrateAsync_UnfilteredIndexOnOwnedTable_PassesStaticValidation()
    {
        var plan = BuildPlan(UnfilteredIndexMigration.MigrationId);
        var runner = CreateRunner(plan, out var provider, out var scope);
        await using var providerScope = provider;
        await using var migrationScope = scope;

        var exception = await Assert.ThrowsAnyAsync<Exception>(() => runner.MigrateAsync());

        Assert.False(
            exception is PersistencePlanExecutionException
            {
                FailureCode: PersistenceMigrationFailureCodes.OperationForbidden
            },
            exception.ToString());
    }

    [Fact]
    public async Task MigrateAsync_UndeclaredUnfilteredUniqueIndex_FailsBeforeOpeningConnection()
    {
        var plan = BuildPlan(UnfilteredUniqueIndexMigration.MigrationId);
        var runner = CreateRunner(plan, out var provider, out var scope);
        await using var providerScope = provider;
        await using var migrationScope = scope;

        var exception = await Assert.ThrowsAsync<PersistencePlanExecutionException>(
            () => runner.MigrateAsync());

        Assert.Equal(PersistenceMigrationFailureCodes.OperationForbidden, exception.FailureCode);
    }

    [Fact]
    public async Task MigrateAsync_DeclaredUnfilteredUniqueIndex_PassesStaticValidation()
    {
        var plan = BuildPlan(
            UnfilteredUniqueIndexMigration.MigrationId,
            [new IndexDefinition("UX_Asset_Id", ["Id"], IsUnique: true)]);
        var runner = CreateRunner(plan, out var provider, out var scope);
        await using var providerScope = provider;
        await using var migrationScope = scope;

        var exception = await Assert.ThrowsAnyAsync<Exception>(() => runner.MigrateAsync());

        Assert.False(
            exception is PersistencePlanExecutionException
            {
                FailureCode: PersistenceMigrationFailureCodes.OperationForbidden
            },
            exception.ToString());
    }

    [Fact]
    public async Task MigrateAsync_DeclaredOwnedForeignKey_PassesStaticValidation()
    {
        var plan = BuildForeignKeyPlan(DeclaredForeignKeyMigration.MigrationId, declareForeignKey: true);
        var runner = CreateRunner(plan, out var provider, out var scope);
        await using var providerScope = provider;
        await using var migrationScope = scope;

        var exception = await Assert.ThrowsAnyAsync<Exception>(() => runner.MigrateAsync());

        Assert.False(
            exception is PersistencePlanExecutionException
            {
                FailureCode: PersistenceMigrationFailureCodes.OperationForbidden
            },
            exception.ToString());
    }

    [Fact]
    public async Task MigrateAsync_UndeclaredForeignKey_FailsBeforeOpeningConnection()
    {
        var plan = BuildForeignKeyPlan(DeclaredForeignKeyMigration.MigrationId, declareForeignKey: false);
        var runner = CreateRunner(plan, out var provider, out var scope);
        await using var providerScope = provider;
        await using var migrationScope = scope;

        var exception = await Assert.ThrowsAsync<PersistencePlanExecutionException>(
            () => runner.MigrateAsync());

        Assert.Equal(PersistenceMigrationFailureCodes.OperationForbidden, exception.FailureCode);
    }

    [Fact]
    public async Task MigrateAsync_OtherModuleSelfReportedLegacyDrop_FailsBeforeOpeningConnection()
    {
        var plan = BuildPlan(
            SelfReportedLegacyDropMigration.MigrationId,
            compatibility: "backup-restore-only");
        var runner = CreateRunner(plan, out var provider, out var scope);
        await using var providerScope = provider;
        await using var migrationScope = scope;

        var exception = await Assert.ThrowsAsync<PersistencePlanExecutionException>(
            () => runner.MigrateAsync());

        Assert.Equal(PersistenceMigrationFailureCodes.OperationForbidden, exception.FailureCode);
    }

    [Fact]
    public async Task MigrateAsync_TrustedOrganizationLegacyDrop_PassesStaticValidation()
    {
        var plan = BuildPlan(
            TrustedOrganizationLegacyDropMigration.MigrationId,
            compatibility: "backup-restore-only",
            owner: "Organization",
            schema: "dbo");
        var runner = CreateRunner(plan, out var provider, out var scope);
        await using var providerScope = provider;
        await using var migrationScope = scope;

        var exception = await Assert.ThrowsAnyAsync<Exception>(() => runner.MigrateAsync());

        Assert.False(
            exception is PersistencePlanExecutionException
            {
                FailureCode: PersistenceMigrationFailureCodes.OperationForbidden
            },
            exception.ToString());
    }

    private static IPersistenceMigrationRunner CreateRunner(
        CompiledPersistencePlan plan,
        out ServiceProvider provider,
        out AsyncServiceScope scope)
    {
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
        provider = services.BuildServiceProvider();
        scope = provider.CreateAsyncScope();
        return scope.ServiceProvider.GetRequiredService<IPersistenceMigrationRunner>();
    }

    private static CompiledPersistencePlan BuildPlan(
        string migrationId,
        IReadOnlyList<IndexDefinition>? indexes = null,
        string compatibility = "safe-expand",
        string owner = "Partner.AssetCatalog",
        string schema = "np_partner_assetcatalog")
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
                indexes ?? [],
                OwnerModuleId: owner,
                SchemaName: schema))
            .ToArray();
        var modelDigest = PersistenceModelDigest.Compute(models);
        var stream = new ModuleMigrationStream(
            owner,
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
                    compatibility)
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

    private static CompiledPersistencePlan BuildForeignKeyPlan(string migrationId, bool declareForeignKey)
    {
        const string owner = "Partner.AssetCatalog";
        const string schema = "np_partner_assetcatalog";
        var id = new FieldDefinition(
            "Id",
            "Id",
            FieldKind.Scalar,
            FieldValueType.ObjectId,
            "string",
            Required: true,
            IsPrimaryKey: true,
            IsAuditField: false);
        var parentId = id with { Name = "ParentId", StoreName = "ParentId", IsPrimaryKey = false };
        var models = new[]
        {
            new ModelDefinition("Parent", "Parent", ModelKind.Entity, [id], [], owner, schema),
            new ModelDefinition(
                "Child",
                "Child",
                ModelKind.Entity,
                [id, parentId],
                [],
                owner,
                schema,
                declareForeignKey
                    ?
                    [new ForeignKeyDefinition(
                        "FK_Child_Parent",
                        ["ParentId"],
                        $"{owner}/Parent",
                        ["Id"],
                        ForeignKeyDeleteBehavior.Restrict)]
                    : [])
        };
        var modelDigest = PersistenceModelDigest.Compute(models);
        var stream = new ModuleMigrationStream(
            owner,
            PersistenceProvider.SqlServer,
            typeof(DeclaredForeignKeyMigration).Assembly.Location,
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

[DbContext(typeof(EfPlatformDbContext))]
[Migration(MigrationId)]
public sealed class UnfilteredIndexMigration : Migration
{
    public const string MigrationId = "20260809999600_UnfilteredIndex";

    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.CreateIndex(
            name: "IX_Asset_Unfiltered",
            schema: "np_partner_assetcatalog",
            table: "Asset",
            column: "Id");

    protected override void Down(MigrationBuilder migrationBuilder)
    {
    }
}

[DbContext(typeof(EfPlatformDbContext))]
[Migration(MigrationId)]
public sealed class UnfilteredUniqueIndexMigration : Migration
{
    public const string MigrationId = "20260809999500_UnfilteredUniqueIndex";

    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.CreateIndex(
            name: "UX_Asset_Id",
            schema: "np_partner_assetcatalog",
            table: "Asset",
            column: "Id",
            unique: true);

    protected override void Down(MigrationBuilder migrationBuilder)
    {
    }
}

[DbContext(typeof(EfPlatformDbContext))]
[Migration(MigrationId)]
public sealed class SelfReportedLegacyDropMigration : Migration
{
    public const string MigrationId = "20260809999400_SelfReportedLegacyDrop";

    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropTable(name: "LegacyAsset", schema: "np_partner_assetcatalog")
            .Annotation("NewP:OwnerModuleId", "Partner.AssetCatalog");

    protected override void Down(MigrationBuilder migrationBuilder)
    {
    }
}

[DbContext(typeof(EfPlatformDbContext))]
[Migration(MigrationId)]
public sealed class TrustedOrganizationLegacyDropMigration : Migration
{
    public const string MigrationId = "20260810102141_Organization_000001_PermanentUserBinding";

    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropTable(name: "PersonUserLink");

    protected override void Down(MigrationBuilder migrationBuilder)
    {
    }
}

[DbContext(typeof(EfPlatformDbContext))]
[Migration(MigrationId)]
public sealed class DeclaredForeignKeyMigration : Migration
{
    public const string MigrationId = "20260809999700_DeclaredForeignKey";

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "Parent",
            schema: "np_partner_assetcatalog",
            columns: table => new
            {
                Id = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_Parent", x => x.Id));
        migrationBuilder.CreateTable(
            name: "Child",
            schema: "np_partner_assetcatalog",
            columns: table => new
            {
                Id = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                ParentId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Child", x => x.Id);
                table.ForeignKey(
                    name: "FK_Child_Parent",
                    column: x => x.ParentId,
                    principalSchema: "np_partner_assetcatalog",
                    principalTable: "Parent",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
    }
}
