using System.Text.Json;
using ICP.Platform.Modeling.Definitions;
using ICP.Platform.Modules.Authentication.Migrations.SqlServer.Migrations;
using ICP.Platform.Modules.Authentication.Persistence.Descriptors;
using ICP.Platform.Modules.Example.Migrations.SqlServer.Migrations;
using ICP.Platform.Modules.Example.Persistence.Descriptors;
using ICP.Platform.Modules.Identity.Migrations.SqlServer.Migrations;
using ICP.Platform.Modules.Identity.Persistence.Descriptors;
using ICP.Platform.Modules.Organization.Migrations.SqlServer.Migrations;
using ICP.Platform.Modules.Organization.Persistence.Descriptors;
using ICP.Platform.Persistence.Composition;
using ICP.Platform.Persistence.EfCore.SqlServerMigrations;
using ICP.Platform.Persistence.Planning;

namespace ICP.Platform.Persistence.Tests;

internal static class TestPersistencePlan
{
    public static CompiledPersistencePlan Create(PersistenceProvider provider = PersistenceProvider.SqlServer)
    {
        if (provider is PersistenceProvider.SqlServer)
        {
            return CreateSqlServerPlan();
        }

        var models = PlatformPersistenceModelCatalog.AdaptPlatformCore(
            NonSqlPlatformBaselineModelSource.GetDefinitions());
        var digest = PersistenceModelDigest.Compute(models);
        var frontier = provider switch
        {
            PersistenceProvider.Oracle => "20260428145243_AlignModelMetadataEntityKeys",
            PersistenceProvider.MySql => "20260428145227_AlignModelMetadataEntityKeys",
            _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, null)
        };
        var platformStream = new ModuleMigrationStream(
            "platform.core",
            provider,
            "platform.core:provider-baseline",
            "platform.core:provider-baseline",
            digest,
            digest,
            frontier,
            IsPlatformStream: true,
            []);
        return Build(provider, models, digest, platformStream);
    }

    public static CompiledPersistencePlan CreateCurrentSqlServer()
    {
        var foundation = CreateSqlServerPlan();
        var identityModels = AdaptOwner(new IdentityModelMetadataProvider().GetDefinitions(), "Identity");
        var authenticationModels = AdaptOwner(
            new AuthenticationModelMetadataProvider().GetDefinitions(),
            "Authentication");
        var organizationModels = AdaptOwner(
            new OrganizationModelMetadataProvider().GetDefinitions(),
            "Organization");
        var exampleModels = AdaptOwner(
            new ExampleModelMetadataProvider().GetDefinitions(),
            "Example");
        var models = foundation.Models
            .Concat(identityModels)
            .Concat(authenticationModels)
            .Concat(organizationModels)
            .Concat(exampleModels)
            .OrderBy(static model => model.LogicalKey, StringComparer.Ordinal)
            .ToArray();
        var streams = foundation.Streams
            .Concat(
            [
                LoadModuleStream(
                    "Example",
                    "Example.SqlServerMigrations.json",
                    typeof(Example_000001_Foundation).Assembly.Location,
                    exampleModels),
                LoadModuleStream(
                    "Identity",
                    "Identity.SqlServerMigrations.json",
                    typeof(IdentityFoundation).Assembly.Location,
                    identityModels),
                LoadModuleStream(
                    "Authentication",
                    "Authentication.SqlServerMigrations.json",
                    typeof(AuthenticationFoundation).Assembly.Location,
                    authenticationModels),
                LoadModuleStream(
                    "Organization",
                    "Organization.SqlServerMigrations.json",
                    typeof(Organization_000001_Foundation).Assembly.Location,
                    organizationModels)
            ])
            .ToArray();
        var digest = PersistenceModelDigest.Compute(models);
        return new CompiledPersistencePlan(
            "current-sqlserver-test-candidate",
            digest,
            digest,
            PersistenceProvider.SqlServer,
            digest,
            Array.Empty<byte>(),
            models,
            streams);
    }

    private static CompiledPersistencePlan CreateSqlServerPlan()
    {
        var models = PlatformFoundationModelSource.GetDefinitions()
            .Select(static definition => definition with { SchemaName = "dbo" })
            .OrderBy(static definition => definition.LogicalKey, StringComparer.Ordinal)
            .ToArray();
        var digest = PersistenceModelDigest.Compute(models);
        var assemblyPath = typeof(SqlServerDesignTimeDbContextFactory).Assembly.Location;
        var assemblyBytes = File.ReadAllBytes(assemblyPath);
        var manifestPath = Path.Combine(AppContext.BaseDirectory, "PlatformFoundation.SqlServerMigrations.json");
        var manifest = JsonSerializer.Deserialize<FoundationManifest>(
            File.ReadAllBytes(manifestPath),
            new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidOperationException("Platform foundation test manifest is empty.");
        var migrations = manifest.Migrations
            .OrderBy(static migration => migration.Sequence, StringComparer.Ordinal)
            .Select(static migration => new CompiledMigrationStep(
                migration.Sequence,
                migration.MigrationId,
                migration.PreviousContentDigest,
                migration.ContentDigest,
                migration.FromModelDigest,
                migration.ToModelDigest,
                migration.Compatibility))
            .ToArray();
        var platformStream = new ModuleMigrationStream(
            manifest.ModuleId,
            PersistenceProvider.SqlServer,
            assemblyPath,
            manifestPath,
            manifest.BundleChecksum,
            manifest.ModelDigest,
            migrations[^1].MigrationId,
            IsPlatformStream: true,
            migrations)
        {
            AssemblyImage = new VerifiedArtifactImage(assemblyBytes)
        };
        return Build(PersistenceProvider.SqlServer, models, digest, platformStream);
    }

    private static CompiledPersistencePlan Build(
        PersistenceProvider provider,
        IReadOnlyList<ICP.Platform.Modeling.Definitions.ModelDefinition> models,
        string digest,
        ModuleMigrationStream platformStream) =>
        new(
            "test-candidate",
            digest,
            digest,
            provider,
            digest,
            Array.Empty<byte>(),
            models,
            [platformStream]);

    private static ModelDefinition[] AdaptOwner(
        IEnumerable<ModelDefinition> definitions,
        string ownerModuleId) =>
        definitions
            .Select(definition => definition with
            {
                OwnerModuleId = ownerModuleId,
                SchemaName = "dbo",
                ForeignKeys = definition.ForeignKeys
                    .Select(foreignKey => foreignKey with
                    {
                        PrincipalModelKey = $"{GetPrincipalOwner(ownerModuleId, foreignKey.PrincipalModelKey)}/{GetModelName(foreignKey.PrincipalModelKey)}"
                    })
                    .ToArray()
            })
            .OrderBy(static definition => definition.LogicalKey, StringComparer.Ordinal)
            .ToArray();

    private static ModuleMigrationStream LoadModuleStream(
        string moduleId,
        string manifestName,
        string assemblyPath,
        IReadOnlyList<ModelDefinition> models)
    {
        var manifestPath = Path.Combine(
            FindRepositoryRoot(),
            "modules",
            moduleId,
            "migrations",
            manifestName);
        var manifest = JsonSerializer.Deserialize<FoundationManifest>(
            File.ReadAllBytes(manifestPath),
            new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidOperationException($"{moduleId} test manifest is empty.");
        var assemblyBytes = File.ReadAllBytes(assemblyPath);
        var migrations = manifest.Migrations
            .OrderBy(static migration => migration.Sequence, StringComparer.Ordinal)
            .Select(static migration => new CompiledMigrationStep(
                migration.Sequence,
                migration.MigrationId,
                migration.PreviousContentDigest,
                migration.ContentDigest,
                migration.FromModelDigest,
                migration.ToModelDigest,
                migration.Compatibility))
            .ToArray();
        var modelDigest = PersistenceModelDigest.Compute(models);
        if (!string.Equals(modelDigest, manifest.ModelDigest, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{moduleId} test model digest does not match its manifest.");
        }

        return new ModuleMigrationStream(
            moduleId,
            PersistenceProvider.SqlServer,
            assemblyPath,
            manifestPath,
            manifest.BundleChecksum,
            modelDigest,
            migrations[^1].MigrationId,
            IsPlatformStream: false,
            migrations)
        {
            AssemblyImage = new VerifiedArtifactImage(assemblyBytes)
        };
    }

    private static string GetPrincipalOwner(string ownerModuleId, string logicalKey) =>
        ownerModuleId == "Authentication" && string.Equals(GetModelName(logicalKey), "IdentityAccount", StringComparison.Ordinal)
            ? "Identity"
            : ownerModuleId;

    private static string GetModelName(string logicalKey)
    {
        var separator = logicalKey.LastIndexOf('/');
        return separator >= 0 ? logicalKey[(separator + 1)..] : logicalKey;
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

    private sealed record FoundationManifest(
        string ModuleId,
        string ModelDigest,
        string BundleChecksum,
        IReadOnlyList<FoundationMigration> Migrations);

    private sealed record FoundationMigration(
        string Sequence,
        string MigrationId,
        string? PreviousContentDigest,
        string ContentDigest,
        string FromModelDigest,
        string ToModelDigest,
        string Compatibility);
}
