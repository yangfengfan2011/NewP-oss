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
    public async Task MigrateAsync_HostApiDependentMigrationAssembly_IsRejected()
    {
        const string migrationId = "20260809999999_HostApiDependent";
        var plan = BuildPlan(
            migrationId,
            migrationAssemblyPath: typeof(PersistenceMigrationRunnerTests).Assembly.Location);
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

        var exception = await Assert.ThrowsAsync<PersistencePlanExecutionException>(() =>
            scope.ServiceProvider.GetRequiredService<IPersistenceMigrationRunner>().MigrateAsync());

        Assert.Equal(PersistenceMigrationFailureCodes.AssemblyInvalid, exception.FailureCode);
        Assert.Equal("Partner.AssetCatalog", exception.ModuleId);
    }

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
    public async Task MigrateAsync_UndeclaredUnfilteredIndex_FailsBeforeOpeningConnection()
    {
        var plan = BuildPlan(UnfilteredIndexMigration.MigrationId);
        var runner = CreateRunner(plan, out var provider, out var scope);
        await using var providerScope = provider;
        await using var migrationScope = scope;

        var exception = await Assert.ThrowsAsync<PersistencePlanExecutionException>(
            () => runner.MigrateAsync());

        Assert.Equal(PersistenceMigrationFailureCodes.OperationForbidden, exception.FailureCode);
    }

    [Fact]
    public async Task MigrateAsync_DeclaredUnfilteredIndex_PassesStaticValidation()
    {
        var plan = BuildPlan(
            UnfilteredIndexMigration.MigrationId,
            [new IndexDefinition("IX_Asset_Unfiltered", ["Id"], IsUnique: false)]);
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

    [Theory]
    [InlineData(DescendingIndexMigration.MigrationId)]
    [InlineData(ClusteredIndexMigration.MigrationId)]
    [InlineData(IncludedColumnsIndexMigration.MigrationId)]
    public async Task MigrateAsync_IndexWithUndeclaredSqlProducingFacet_FailsBeforeOpeningConnection(
        string migrationId)
    {
        var plan = BuildPlan(
            migrationId,
            [new IndexDefinition("IX_Asset_Unfiltered", ["Id"], IsUnique: false)]);
        var runner = CreateRunner(plan, out var provider, out var scope);
        await using var providerScope = provider;
        await using var migrationScope = scope;

        var exception = await Assert.ThrowsAsync<PersistencePlanExecutionException>(
            () => runner.MigrateAsync());

        Assert.Equal(PersistenceMigrationFailureCodes.OperationForbidden, exception.FailureCode);
        Assert.Equal(migrationId, exception.MigrationId);
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
    public async Task MigrateAsync_PublicPlanCannotForgeTrustedOrganizationLegacyDrop()
    {
        var plan = BuildPlan(
            TrustedOrganizationLegacyDropMigration.MigrationId,
            compatibility: "backup-restore-only",
            owner: "Organization",
            schema: "dbo");
        var runner = CreateRunner(plan, out var provider, out var scope);
        await using var providerScope = provider;
        await using var migrationScope = scope;

        var exception = await Assert.ThrowsAsync<PersistencePlanExecutionException>(
            () => runner.MigrateAsync());

        Assert.Equal(PersistenceMigrationFailureCodes.OperationForbidden, exception.FailureCode);
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
        string schema = "np_partner_assetcatalog",
        string? migrationAssemblyPath = null)
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
        var assemblyPath = migrationAssemblyPath ?? typeof(ForbiddenSqlMigration).Assembly.Location;
        var stream = new ModuleMigrationStream(
            owner,
            PersistenceProvider.SqlServer,
            assemblyPath,
            "test-manifest.json",
            Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(assemblyPath))),
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
            ])
        {
            AssemblyImage = new VerifiedArtifactImage(File.ReadAllBytes(assemblyPath))
        };
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
        var assemblyPath = typeof(DeclaredForeignKeyMigration).Assembly.Location;
        var stream = new ModuleMigrationStream(
            owner,
            PersistenceProvider.SqlServer,
            assemblyPath,
            "test-manifest.json",
            Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(assemblyPath))),
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
            ])
        {
            AssemblyImage = new VerifiedArtifactImage(File.ReadAllBytes(assemblyPath))
        };
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
