using System.Text;
using System.Text.Json;
using ICP.Platform.Modeling;
using ICP.Platform.Modeling.Definitions;
using ICP.Platform.Modeling.Metadata;
using ICP.Platform.Modeling.Projection;
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
    private const string OrganizationFoundationPositionMetadata = """
        {"schemaVersion":"1.0","type":"model","Position":{"kind":"Entity","builtinFields":["ENAME","ECODE"],"fields":[{"name":"CompanyId","storage":{"type":"guid"},"dataSource":{"type":"model","key":"Company","model":"Company"}},{"name":"DepartmentId","storage":{"type":"guid"},"dataSource":{"type":"model","key":"Department","model":"Department"}},{"name":"EmploymentStatus","storage":{"type":"string"},"contract":{"valueType":"enum","clrType":"EmploymentStatus"},"dataSource":{"type":"enum","key":"EmploymentStatus"}},{"name":"IsEnabled","storage":{"type":"bool"}},{"name":"SortOrder","storage":{"type":"string"}}],"indexes":[{"name":"IX_Position_Department","fields":["DepartmentId"],"unique":false},{"name":"IX_Position_Company","fields":["CompanyId"],"unique":false}]}}
        """;

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
        var historicalModels = new Dictionary<string, ModelDefinition>(StringComparer.Ordinal);
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
                AddSupportedHistoricalPrefix(
                    repositoryRoot,
                    current.Models,
                    stream,
                    withinCutoff,
                    streams,
                    historicalModels);
            }
        }

        var models = current.Models
            .Select(model => ReferenceEquals(model, currentModel) ? stageModel : model)
            .Where(model => !droppedModules.Contains(model.OwnerModuleId))
            .Select(model => historicalModels.GetValueOrDefault(model.LogicalKey) ?? model)
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

    // Example stage 2/3 之后新增的 Organization migration 会跨越 cutoff；fixture 只接受
    // 已冻结且摘要匹配的 Organization foundation 前缀，不为任意模块猜测历史模型。
    private static void AddSupportedHistoricalPrefix(
        string repositoryRoot,
        IReadOnlyList<ModelDefinition> currentModels,
        ModuleMigrationStream stream,
        int migrationCount,
        ICollection<ModuleMigrationStream> streams,
        IDictionary<string, ModelDefinition> historicalModels)
    {
        if (!string.Equals(stream.ModuleId, "Organization", StringComparison.Ordinal) || migrationCount != 1)
        {
            throw new InvalidOperationException(
                $"Module {stream.ModuleId} has an unsupported historical prefix of {migrationCount} migrations.");
        }

        var migrations = stream.Migrations.Take(migrationCount).ToArray();
        var organizationModels = BuildOrganizationFoundationModels(repositoryRoot, currentModels);
        var modelDigest = PersistenceModelDigest.Compute(organizationModels);
        if (!string.Equals(modelDigest, migrations[^1].ToModelDigest, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Organization foundation model digest {modelDigest} no longer matches accepted prefix {migrations[^1].ToModelDigest}.");
        }

        streams.Add(stream with
        {
            ModelDigest = modelDigest,
            ExpectedFrontier = migrations[^1].MigrationId,
            Migrations = migrations
        });
        foreach (var model in organizationModels)
        {
            historicalModels.Add(model.LogicalKey, model);
        }
    }

    // 该快照来自 000001 固定点：Position 使用当时的 authoring，Person 排除后续追加的 temporal 字段，
    // 并临时补回 000005 才删除的历史 Revision 列。
    // 快照仍经当前正式 loader、storage map 和 projector 生成，并由 manifest 摘要守卫阻止漂移。
    private static ModelDefinition[] BuildOrganizationFoundationModels(
        string repositoryRoot,
        IReadOnlyList<ModelDefinition> currentModels)
    {
        var organizationModels = currentModels
            .Where(static model => string.Equals(model.OwnerModuleId, "Organization", StringComparison.Ordinal))
            .ToArray();
        var position = organizationModels.Single(static model => model.Name == "Position");
        var person = organizationModels.Single(static model => model.Name == "Person");
        var sourcePath = Path.Combine(
            repositoryRoot,
            "modules",
            "Organization",
            "metadata",
            "models",
            "Position.model.json");
        var document = new JsonModelMetadataDocumentLoader().Load(
            Encoding.UTF8.GetBytes(OrganizationFoundationPositionMetadata),
            sourcePath);
        var storageTypeMap = new JsonStorageTypeMapLoader().LoadFromFile(Path.Combine(
            repositoryRoot,
            "server",
            "platform-modeling",
            "metadata",
            "storage-types.json"));
        var foundationPosition = new ModelMetadataProjector().Project(document, storageTypeMap, "SqlServer") with
        {
            OwnerModuleId = "Organization",
            SchemaName = "dbo"
        };
        var foundationPersonFields = person.Fields
            .Where(static field =>
                field.Name is not "BirthDate"
                    and not "EmploymentStartedAt"
                    and not "EmploymentEndedAt")
            .ToList();
        var isEnabledIndex = foundationPersonFields.FindIndex(static field => field.Name == "IsEnabled");
        foundationPersonFields.Insert(
            isEnabledIndex + 1,
            new FieldDefinition(
                "Revision",
                "Revision",
                FieldKind.Scalar,
                FieldValueType.Integer,
                "bigint",
                Required: true,
                IsPrimaryKey: false,
                IsAuditField: false));
        var foundationPerson = person with
        {
            Fields = foundationPersonFields
        };

        return organizationModels
            .Select(model => ReferenceEquals(model, position)
                ? foundationPosition
                : ReferenceEquals(model, person) ? foundationPerson : model)
            .OrderBy(static model => model.LogicalKey, StringComparer.Ordinal)
            .ToArray();
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
