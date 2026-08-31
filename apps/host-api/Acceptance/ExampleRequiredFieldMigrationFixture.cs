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
using ICP.Platform.Persistence;
using ICP.Platform.Persistence.Composition;
using ICP.Platform.Persistence.EfCore.SqlServerMigrations;
using ICP.Platform.Persistence.Planning;
using ICP.Platform.HostApi.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;

namespace ICP.Platform.HostApi.Acceptance;

/// <summary>
/// 仅为显式 Acceptance run 构造并应用 Example required-field 的历史 migration 前缀。
/// </summary>
internal static class ExampleRequiredFieldMigrationFixture
{
    private const string BeforeRecordSourceModelDigest = "a5464035b4271ab8c1495edba7bfab00625a9ebb65978c459f05efd0758c4d9c";
    private const string NullableRecordSourceModelDigest = "05f1fa870d2f602d37cddd4c79d3e3931730c8d505223c0a35c57a7b662cc0cf";

    /// <summary>
    /// 在 runner-owned catalog 上经 Host persistence composition 应用一个已验证历史前缀。
    /// </summary>
    internal static async Task ApplyHistoricalStageAsync(
        string repositoryRoot,
        IConfiguration configuration,
        string expectedRunId,
        string environmentName,
        int stage,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedRunId);
        if (!string.Equals(environmentName, "Acceptance", StringComparison.Ordinal)
            || !configuration.GetValue<bool>("Acceptance:Enabled")
            || !string.Equals(configuration["Acceptance:RunId"], expectedRunId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("ACC_EXAMPLE_FIXTURE_NOT_ENABLED: exact Acceptance run identity is required.");
        }
        if (string.IsNullOrWhiteSpace(configuration.GetConnectionString("SqlServer")))
        {
            throw new InvalidOperationException("ACC_EXAMPLE_FIXTURE_CONFIG_MISSING: SQL Server connection is required.");
        }
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = environmentName,
            ContentRootPath = Path.GetFullPath(repositoryRoot)
        });
        builder.Services.AddPlatformPersistence(
            configuration,
            BuildHistoricalPlan(repositoryRoot, stage));
        await using var app = builder.Build();
        await app.ApplyPlatformPersistenceAsync(cancellationToken);
    }

    /// <summary>
    /// 从当前受验证 migration artifact 构造 Example stage 2/3 有效历史前缀的只读计划。
    /// </summary>
    internal static CompiledPersistencePlan BuildHistoricalPlan(string repositoryRoot, int stage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        if (stage is not (2 or 3))
        {
            throw new ArgumentOutOfRangeException(nameof(stage), stage, "Example historical stage must be 2 or 3.");
        }

        var current = BuildCurrentPlan(Path.GetFullPath(repositoryRoot));
        var currentModel = current.Models.Single(static model =>
            string.Equals(model.OwnerModuleId, "Example", StringComparison.Ordinal)
            && string.Equals(model.Name, "ExampleRecord", StringComparison.Ordinal));
        var stageModel = stage == 2
            ? currentModel with
            {
                Fields = currentModel.Fields
                    .Where(static field => !string.Equals(field.Name, "RecordSource", StringComparison.Ordinal))
                    .ToArray()
            }
            : currentModel with
            {
                Fields = currentModel.Fields
                    .Select(static field => string.Equals(field.Name, "RecordSource", StringComparison.Ordinal)
                        ? field with { Required = false }
                        : field)
                    .ToArray()
            };
        var expectedModelDigest = stage == 2
            ? BeforeRecordSourceModelDigest
            : NullableRecordSourceModelDigest;
        var actualModelDigest = PersistenceModelDigest.Compute([stageModel]);
        if (!string.Equals(actualModelDigest, expectedModelDigest, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Example historical model no longer matches the accepted migration prefix.");
        }

        var currentStream = current.Streams.Single(static stream =>
            string.Equals(stream.ModuleId, "Example", StringComparison.Ordinal));
        var migrations = currentStream.Migrations.Take(stage).ToArray();
        var stageStream = currentStream with
        {
            ModelDigest = expectedModelDigest,
            ExpectedFrontier = migrations[^1].MigrationId,
            Migrations = migrations
        };

        // 已应用 migration 必须是全局有序 migration 列表的位置前缀，否则 Host 接管时会以
        // PERSISTENCE_MIGRATION_HISTORY_NON_PREFIX 拒绝。只截断 Example 流不足以保证这一点：
        // 任何整体排序在 cutoff 之后的模块 baseline 会插到未应用的 Example migration 之后，
        // 使前缀断裂。这类模块在该历史时点尚不存在，连同其模型一起排除。
        var cutoff = stageStream.ExpectedFrontier;
        var droppedModules = new HashSet<string>(StringComparer.Ordinal);
        var streams = new List<ModuleMigrationStream>(current.Streams.Count);
        foreach (var stream in current.Streams)
        {
            if (ReferenceEquals(stream, currentStream))
            {
                streams.Add(stageStream);
                continue;
            }

            var withinCutoff = stream.Migrations.Count(step =>
                string.CompareOrdinal(step.MigrationId, cutoff) <= 0);
            if (withinCutoff == stream.Migrations.Count)
            {
                streams.Add(stream);
            }
            else if (withinCutoff == 0)
            {
                droppedModules.Add(stream.ModuleId);
            }
            else
            {
                // 部分跨越 cutoff 的非 Example 流需要该模块的历史 model digest，本 fixture 不持有；
                // 静默截断会产出与 ModelDigest 不一致的计划，因此显式失败。
                throw new InvalidOperationException(
                    $"Module {stream.ModuleId} straddles the Example stage-{stage} cutoff {cutoff}; "
                    + "historical stage plans cannot express a partially applied non-Example stream.");
            }
        }

        var models = current.Models
            .Select(model => ReferenceEquals(model, currentModel) ? stageModel : model)
            .Where(model => !droppedModules.Contains(model.OwnerModuleId))
            .ToArray();
        var planDigest = PersistenceModelDigest.Compute(models);
        return current with
        {
            CandidateId = $"acceptance-example-required-field-stage-{stage}",
            PlanDigest = planDigest,
            CanonicalBytes = [],
            Models = models,
            Streams = streams.ToArray()
        };
    }

    // 当前计划只从各 owner 的正式 Model provider、manifest 和 migration assembly 构造。
    private static CompiledPersistencePlan BuildCurrentPlan(string repositoryRoot)
    {
        var foundation = BuildFoundationPlan(repositoryRoot);
        var identityModels = AdaptOwner(new IdentityModelMetadataProvider().GetDefinitions(), "Identity");
        var authenticationModels = AdaptOwner(
            new AuthenticationModelMetadataProvider().GetDefinitions(),
            "Authentication");
        var organizationModels = AdaptOwner(
            new OrganizationModelMetadataProvider().GetDefinitions(),
            "Organization");
        var exampleModels = AdaptOwner(new ExampleModelMetadataProvider().GetDefinitions(), "Example");
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
                    repositoryRoot,
                    "Example",
                    "Example.SqlServerMigrations.json",
                    typeof(Example_000001_Foundation).Assembly.Location,
                    exampleModels),
                LoadModuleStream(
                    repositoryRoot,
                    "Identity",
                    "Identity.SqlServerMigrations.json",
                    typeof(IdentityFoundation).Assembly.Location,
                    identityModels),
                LoadModuleStream(
                    repositoryRoot,
                    "Authentication",
                    "Authentication.SqlServerMigrations.json",
                    typeof(AuthenticationFoundation).Assembly.Location,
                    authenticationModels),
                LoadModuleStream(
                    repositoryRoot,
                    "Organization",
                    "Organization.SqlServerMigrations.json",
                    typeof(Organization_000001_Foundation).Assembly.Location,
                    organizationModels)
            ])
            .ToArray();
        var digest = PersistenceModelDigest.Compute(models);
        return new CompiledPersistencePlan(
            "acceptance-current-sqlserver-candidate",
            digest,
            digest,
            PersistenceProvider.SqlServer,
            digest,
            [],
            models,
            streams);
    }

    // Platform Foundation 仍使用正式 SQL Server migration assembly 与 manifest。
    private static CompiledPersistencePlan BuildFoundationPlan(string repositoryRoot)
    {
        var models = PlatformFoundationModelSource.GetDefinitions()
            .Select(static definition => definition with { SchemaName = "dbo" })
            .OrderBy(static definition => definition.LogicalKey, StringComparer.Ordinal)
            .ToArray();
        var digest = PersistenceModelDigest.Compute(models);
        var assemblyPath = typeof(SqlServerDesignTimeDbContextFactory).Assembly.Location;
        var manifestPath = ResolveManifest(
            repositoryRoot,
            "server/platform-persistence-efcore.migrations.sqlserver/PlatformFoundation.SqlServerMigrations.json",
            "PlatformFoundation.SqlServerMigrations.json");
        var stream = ReadStream(
            "platform.foundation",
            assemblyPath,
            manifestPath,
            models,
            isPlatformStream: true);
        return new CompiledPersistencePlan(
            "acceptance-foundation-candidate",
            digest,
            digest,
            PersistenceProvider.SqlServer,
            digest,
            [],
            models,
            [stream]);
    }

    // 模块 model 只补齐 owner/schema/FK 归属投影，不重新声明字段事实。
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

    // 模块 stream 必须通过 manifest model digest 复核后才能用于前缀 fixture。
    private static ModuleMigrationStream LoadModuleStream(
        string repositoryRoot,
        string moduleId,
        string manifestName,
        string assemblyPath,
        IReadOnlyList<ModelDefinition> models)
    {
        var manifestPath = Path.Combine(repositoryRoot, "modules", moduleId, "migrations", manifestName);
        return ReadStream(moduleId, assemblyPath, manifestPath, models, isPlatformStream: false);
    }

    // 读取并固定 migration artifact 映像，防止执行期重新打开可变路径。
    private static ModuleMigrationStream ReadStream(
        string expectedModuleId,
        string assemblyPath,
        string manifestPath,
        IReadOnlyList<ModelDefinition> models,
        bool isPlatformStream)
    {
        var manifest = JsonSerializer.Deserialize<MigrationManifest>(
            File.ReadAllBytes(manifestPath),
            new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidOperationException($"{expectedModuleId} acceptance manifest is empty.");
        if (!string.Equals(manifest.ModuleId, expectedModuleId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{expectedModuleId} acceptance manifest owner changed.");
        }

        var modelDigest = PersistenceModelDigest.Compute(models);
        if (!string.Equals(modelDigest, manifest.ModelDigest, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{expectedModuleId} acceptance model digest does not match its manifest.");
        }

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
        return new ModuleMigrationStream(
            expectedModuleId,
            PersistenceProvider.SqlServer,
            assemblyPath,
            manifestPath,
            manifest.BundleChecksum,
            modelDigest,
            migrations[^1].MigrationId,
            isPlatformStream,
            migrations)
        {
            AssemblyImage = new VerifiedArtifactImage(File.ReadAllBytes(assemblyPath))
        };
    }

    // 源树 manifest 为首选，打包输出只作为 runner 分发时的等价后备。
    private static string ResolveManifest(string repositoryRoot, string sourceRelativePath, string outputName)
    {
        var sourcePath = Path.Combine(repositoryRoot, sourceRelativePath.Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(sourcePath) ? sourcePath : Path.Combine(AppContext.BaseDirectory, outputName);
    }

    // Authentication 中指向 IdentityAccount 的 FK 仍保留 Identity owner。
    private static string GetPrincipalOwner(string ownerModuleId, string logicalKey) =>
        ownerModuleId == "Authentication" && string.Equals(GetModelName(logicalKey), "IdentityAccount", StringComparison.Ordinal)
            ? "Identity"
            : ownerModuleId;

    // Model key 可能已带 owner 前缀，投影时只取稳定 model name。
    private static string GetModelName(string logicalKey)
    {
        var separator = logicalKey.LastIndexOf('/');
        return separator >= 0 ? logicalKey[(separator + 1)..] : logicalKey;
    }

    private sealed record MigrationManifest(
        string ModuleId,
        string ModelDigest,
        string BundleChecksum,
        IReadOnlyList<MigrationManifestStep> Migrations);

    private sealed record MigrationManifestStep(
        string Sequence,
        string MigrationId,
        string? PreviousContentDigest,
        string ContentDigest,
        string FromModelDigest,
        string ToModelDigest,
        string Compatibility);
}
