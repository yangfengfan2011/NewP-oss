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
    public async Task MigrateAsync_PublicPlanRepeatsMigrationIdInOneStream_FailsBeforeOpeningConnection()
    {
        var plan = BuildPlan(ForbiddenSqlMigration.MigrationId);
        var stream = plan.Streams.Single();
        var repeated = stream.Migrations[0] with
        {
            Sequence = "000002",
            PreviousContentDigest = stream.Migrations[0].ContentDigest,
            ContentDigest = new string('e', 64),
            FromModelDigest = stream.Migrations[0].ToModelDigest
        };
        plan = plan with
        {
            Streams = [stream with { Migrations = [stream.Migrations[0], repeated] }]
        };
        var runner = CreateRunner(plan, out var provider, out var scope);
        await using var providerScope = provider;
        await using var migrationScope = scope;

        var exception = await Assert.ThrowsAsync<PersistencePlanExecutionException>(() => runner.MigrateAsync());

        Assert.Equal("PERSISTENCE_MIGRATION_ID_CONFLICT", exception.FailureCode);
        Assert.Equal(ForbiddenSqlMigration.MigrationId, exception.MigrationId);
    }

    [Fact]
    public async Task MigrateAsync_StreamReferencesMissingMigrationType_FailsBeforeOpeningConnection()
    {
        const string missingMigrationId = "20990101000000_Missing";
        var plan = BuildPlan(missingMigrationId);
        var runner = CreateRunner(plan, out var provider, out var scope);
        await using var providerScope = provider;
        await using var migrationScope = scope;

        var exception = await Assert.ThrowsAsync<PersistencePlanExecutionException>(() => runner.MigrateAsync());

        Assert.Equal(PersistenceMigrationFailureCodes.MigrationMissing, exception.FailureCode);
        Assert.Equal("Partner.AssetCatalog", exception.ModuleId);
        Assert.Equal(missingMigrationId, exception.MigrationId);
    }

    [Fact]
    public async Task MigrateAsync_StreamFrontierDoesNotMatchCompiledMigration_FailsBeforeOpeningConnection()
    {
        var plan = BuildPlan(ForbiddenSqlMigration.MigrationId);
        var stream = plan.Streams.Single();
        plan = plan with
        {
            Streams = [stream with { ExpectedFrontier = "20990101000000_Unknown" }]
        };
        var runner = CreateRunner(plan, out var provider, out var scope);
        await using var providerScope = provider;
        await using var migrationScope = scope;

        var exception = await Assert.ThrowsAsync<PersistencePlanExecutionException>(() => runner.MigrateAsync());

        Assert.Equal(PersistenceHistoryFailureCodes.FrontierMismatch, exception.FailureCode);
        Assert.Equal("20990101000000_Unknown", exception.MigrationId);
    }

    [Fact]
    public async Task MigrateAsync_UnknownHistoryVersion_FailsBeforeOpeningConnection()
    {
        var plan = BuildPlan(ForbiddenSqlMigration.MigrationId) with
        {
            HistoryVersion = "unified-history/v2"
        };
        var runner = CreateRunner(plan, out var provider, out var scope);
        await using var providerScope = provider;
        await using var migrationScope = scope;

        var exception = await Assert.ThrowsAsync<PersistencePlanExecutionException>(() => runner.MigrateAsync());

        Assert.Equal(PersistenceHistoryFailureCodes.VersionUnsupported, exception.FailureCode);
    }

    [Fact]
    public void ValidateBeforeConnection_LegalMigrationAppend_ExtendsCanonicalHistoryAndPreservesPrefix()
    {
        var initialHistory = ReadCanonicalHistory(BuildAppendableModulePlan(includeSecondMigration: false));
        var appendedHistory = ReadCanonicalHistory(BuildAppendableModulePlan(includeSecondMigration: true));

        Assert.Equal(
            initialHistory.ToArray(),
            appendedHistory.Take(initialHistory.Count).ToArray());
        Assert.Equal(
            [CreateAssetCatalogMigration.MigrationId, AddUniqueAssetCodeMigration.MigrationId],
            appendedHistory.Select(static entry => entry.MigrationId));
    }

    [Fact]
    public void ValidateBeforeConnection_RequiredFieldThreeStepTransition_GeneratesCanonicalHistory()
    {
        var history = ReadCanonicalHistory(BuildRequiredFieldBackfillPlan());

        Assert.Equal(
            [
                CreateNullableAssetCodeMigration.MigrationId,
                BackfillAssetCodeMigration.MigrationId,
                RequireAssetCodeMigration.MigrationId
            ],
            history.Select(static entry => entry.MigrationId));
    }

    [Fact]
    public async Task MigrateAsync_CrossOwnerRequiredFieldBackfill_FailsBeforeOpeningConnection()
    {
        var plan = BuildRequiredFieldBackfillPlan(CrossOwnerBackfillAssetCodeMigration.MigrationId);
        var runner = CreateRunner(plan, out var provider, out var scope);
        await using var providerScope = provider;
        await using var migrationScope = scope;

        var exception = await Assert.ThrowsAsync<PersistencePlanExecutionException>(() => runner.MigrateAsync());

        Assert.Equal(PersistenceMigrationFailureCodes.OperationForbidden, exception.FailureCode);
        Assert.Equal("Partner.AssetCatalog", exception.ModuleId);
        Assert.Equal(CrossOwnerBackfillAssetCodeMigration.MigrationId, exception.MigrationId);
    }

    [Theory]
    [InlineData(ForbiddenSqlMigration.MigrationId)]
    [InlineData(TransactionSuppressedSqlMigration.MigrationId)]
    public async Task MigrateAsync_ForbiddenOrTransactionSuppressedOperation_FailsBeforeOpeningConnection(
        string migrationId)
    {
        var plan = BuildPlan(migrationId);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Persistence:Adapter"] = "EfCore",
                ["Persistence:Provider"] = "SqlServer",
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
    public async Task MigrateAsync_MigrationDefinesDown_DoesNotRejectItsValidatedUp()
    {
        var plan = BuildPlan(CreateAssetWithDownMigration.MigrationId);
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
    [InlineData(MalformedCreateTableMigration.MigrationId)]
    [InlineData(MalformedRenameTableMigration.MigrationId)]
    [InlineData(MalformedAlterColumnMigration.MigrationId)]
    [InlineData(MalformedDropColumnMigration.MigrationId)]
    public async Task MigrateAsync_MalformedStructuralOperation_FailsBeforeOpeningConnection(
        string migrationId)
    {
        var runner = CreateRunner(BuildPlan(migrationId), out var provider, out var scope);
        await using var providerScope = provider;
        await using var migrationScope = scope;

        var exception = await Assert.ThrowsAsync<PersistencePlanExecutionException>(
            () => runner.MigrateAsync());

        Assert.Equal(PersistenceMigrationFailureCodes.OperationForbidden, exception.FailureCode);
        Assert.Equal(migrationId, exception.MigrationId);
    }

    [Theory]
    [InlineData(AddMismatchedRevisionColumnMigration.MigrationId)]
    [InlineData(AddRevisionWithBusinessDefaultMigration.MigrationId)]
    public async Task MigrateAsync_AddColumnThatDoesNotMatchProviderTarget_FailsBeforeOpeningConnection(
        string migrationId)
    {
        var plan = BuildPlanWithRevision(migrationId);
        var runner = CreateRunner(plan, out var provider, out var scope);
        await using var providerScope = provider;
        await using var migrationScope = scope;

        var exception = await Assert.ThrowsAsync<PersistencePlanExecutionException>(
            () => runner.MigrateAsync());

        Assert.Equal(PersistenceMigrationFailureCodes.OperationForbidden, exception.FailureCode);
        Assert.Equal(migrationId, exception.MigrationId);
    }

    [Fact]
    public async Task MigrateAsync_AddRequiredColumnWithClrDefault_NormalizesToNoDefaultBeforeConnection()
    {
        var plan = BuildPlanWithRevision(AddRevisionWithClrDefaultMigration.MigrationId);
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
    public async Task MigrateAsync_FilteredModuleIndex_FailsBeforeOpeningConnection()
    {
        var migrationId = FilteredIndexMigration.MigrationId;
        var plan = BuildPlan(migrationId);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Persistence:Adapter"] = "EfCore",
                ["Persistence:Provider"] = "SqlServer",
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

    private static IReadOnlyList<CompiledMigrationHistoryEntry> ReadCanonicalHistory(
        CompiledPersistencePlan plan)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Persistence:Adapter"] = "EfCore",
                ["Persistence:Provider"] = "SqlServer",
                ["ConnectionStrings:SqlServer"] =
                    "Server=(localdb)\\MSSQLLocalDB;Database=never-connect;Integrated Security=True;TrustServerCertificate=True;"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddEfCorePersistence(
            configuration,
            plan,
            WorktreeDatabaseIdentity.FromGitPaths(@"D:\repo\.git", @"D:\repo\.git"));
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
        return new ModuleMigrationExecutor(dbContext, plan).ValidateBeforeConnection();
    }

    private static CompiledPersistencePlan BuildAppendableModulePlan(bool includeSecondMigration)
    {
        const string owner = "Partner.AssetCatalog";
        const string schema = "np_partner_assetcatalog";
        var asset = new ModelDefinition(
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
                    IsAuditField: false),
                new FieldDefinition(
                    "Code",
                    "Code",
                    FieldKind.Scalar,
                    FieldValueType.Text,
                    "nvarchar(256)",
                    Required: true,
                    IsPrimaryKey: false,
                    IsAuditField: false)
            ],
            includeSecondMigration
                ? [new IndexDefinition("UX_Asset_Code", ["Code"], IsUnique: true)]
                : [],
            owner,
            schema);
        var firstModelDigest = PersistenceModelDigest.Compute([asset with { Indexes = [] }]);
        var modelDigest = PersistenceModelDigest.Compute([asset]);
        var migrations = new List<CompiledMigrationStep>
        {
            new(
                "000001",
                CreateAssetCatalogMigration.MigrationId,
                null,
                new string('1', 64),
                new string('0', 64),
                firstModelDigest,
                "safe-expand")
        };
        if (includeSecondMigration)
        {
            migrations.Add(new CompiledMigrationStep(
                "000002",
                AddUniqueAssetCodeMigration.MigrationId,
                new string('1', 64),
                new string('2', 64),
                firstModelDigest,
                modelDigest,
                "safe-expand"));
        }

        var assemblyPath = typeof(CreateAssetCatalogMigration).Assembly.Location;
        var assemblyBytes = File.ReadAllBytes(assemblyPath);
        var stream = new ModuleMigrationStream(
            owner,
            PersistenceProvider.SqlServer,
            assemblyPath,
            "test-appendable-manifest.json",
            Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(assemblyBytes)),
            modelDigest,
            migrations[^1].MigrationId,
            IsPlatformStream: false,
            migrations)
        {
            AssemblyImage = new VerifiedArtifactImage(assemblyBytes)
        };
        return new CompiledPersistencePlan(
            "test-appendable-candidate",
            modelDigest,
            modelDigest,
            PersistenceProvider.SqlServer,
            modelDigest,
            [],
            [asset],
            [stream]);
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
            IsPlatformStream: false,
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

    private static CompiledPersistencePlan BuildRequiredFieldBackfillPlan(
        string backfillMigrationId = BackfillAssetCodeMigration.MigrationId)
    {
        const string owner = "Partner.AssetCatalog";
        const string schema = "np_partner_assetcatalog";
        var asset = new ModelDefinition(
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
                    IsAuditField: false),
                new FieldDefinition(
                    "Code",
                    "Code",
                    FieldKind.Scalar,
                    FieldValueType.Text,
                    "nvarchar(256)",
                    Required: true,
                    IsPrimaryKey: false,
                    IsAuditField: false)
            ],
            [],
            owner,
            schema);
        var modelDigest = PersistenceModelDigest.Compute([asset]);
        var migrationIds = new[]
        {
            CreateNullableAssetCodeMigration.MigrationId,
            backfillMigrationId,
            RequireAssetCodeMigration.MigrationId
        };
        var steps = migrationIds
            .Select((migrationId, index) => new CompiledMigrationStep(
                $"{index + 1:D6}",
                migrationId,
                index == 0 ? null : new string((char)('a' + index - 1), 64),
                new string((char)('a' + index), 64),
                modelDigest,
                modelDigest,
                index switch
                {
                    0 => "safe-expand",
                    1 => "data-backfill",
                    _ => "contract"
                }))
            .ToArray();
        var assemblyPath = typeof(CreateNullableAssetCodeMigration).Assembly.Location;
        var assemblyBytes = File.ReadAllBytes(assemblyPath);
        var stream = new ModuleMigrationStream(
            owner,
            PersistenceProvider.SqlServer,
            assemblyPath,
            "test-required-field-backfill-manifest.json",
            Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(assemblyBytes)),
            modelDigest,
            migrationIds[^1],
            IsPlatformStream: false,
            steps)
        {
            AssemblyImage = new VerifiedArtifactImage(assemblyBytes)
        };
        return new CompiledPersistencePlan(
            "test-required-field-backfill-candidate",
            modelDigest,
            modelDigest,
            PersistenceProvider.SqlServer,
            modelDigest,
            [],
            [asset],
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
            IsPlatformStream: false,
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

    private static CompiledPersistencePlan BuildPlanWithRevision(string migrationId)
    {
        const string owner = "Partner.AssetCatalog";
        const string schema = "np_partner_assetcatalog";
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
                        IsAuditField: false),
                    new FieldDefinition(
                        "Revision",
                        "Revision",
                        FieldKind.Scalar,
                        FieldValueType.Integer,
                        "long",
                        Required: true,
                        IsPrimaryKey: false,
                        IsAuditField: false)
                ],
                [],
                owner,
                schema))
            .ToArray();
        var modelDigest = PersistenceModelDigest.Compute(models);
        var assemblyPath = typeof(AddMismatchedRevisionColumnMigration).Assembly.Location;
        var stream = new ModuleMigrationStream(
            owner,
            PersistenceProvider.SqlServer,
            assemblyPath,
            "test-manifest.json",
            Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(assemblyPath))),
            modelDigest,
            migrationId,
            IsPlatformStream: false,
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
