using System.Collections;
using System.Data;
using System.Data.Common;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using ICP.Platform.Kernel.ModuleLoading;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Modeling.Definitions;
using ICP.Platform.Persistence.Planning;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace ICP.Platform.Persistence.EfCore;

/// <summary>
/// EF Core provider 内部的模块迁移执行器。先静态检查整份候选计划，再允许 ownership
/// guard 建立连接；这样非法 bundle 不会留下 marker、history 或业务 schema 残留。
/// </summary>
internal sealed class ModuleMigrationExecutor(
    EfPlatformDbContext dbContext,
    CompiledPersistencePlan plan)
{
    private const string SqlServerProviderName = "Microsoft.EntityFrameworkCore.SqlServer";
    private static readonly ConditionalWeakTable<
        CompiledPersistencePlan,
        Lazy<IReadOnlyList<ValidatedMigrationStream>>> ValidationCache = new();
    private IReadOnlyList<ValidatedMigrationStream>? _validatedStreams;

    public void ValidateBeforeConnection()
    {
        if (_validatedStreams is not null)
        {
            return;
        }

        _validatedStreams = ValidationCache.GetValue(
            plan,
            _ => new Lazy<IReadOnlyList<ValidatedMigrationStream>>(
                CompileValidatedStreams,
                LazyThreadSafetyMode.ExecutionAndPublication)).Value;
    }

    private IReadOnlyList<ValidatedMigrationStream> CompileValidatedStreams()
    {
        var externalStreams = plan.Streams
            .Where(static stream => !stream.IsLegacyBaseline)
            .ToArray();
        if (externalStreams.Length == 0)
        {
            return [];
        }

        if (plan.Provider is not PersistenceProvider.SqlServer)
        {
            var stream = externalStreams[0];
            throw Failure(
                PersistenceMigrationFailureCodes.ProviderUnsupported,
                stream);
        }

        var assemblyContext = new VerifiedMigrationAssemblyLoadContext(externalStreams);
        var validatedStreams = externalStreams
            .Select(stream => ValidateStream(stream, assemblyContext))
            .ToArray();
        return GenerateSqlCommands(validatedStreams);
    }

    public async Task ApplyAsync(CancellationToken cancellationToken)
    {
        ValidateBeforeConnection();

        var connectionString = dbContext.Database.GetConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("The persistence connection string is unavailable.");
        }

        var appliedMigrations = (await dbContext.Database
                .GetAppliedMigrationsAsync(cancellationToken))
            .ToHashSet(StringComparer.Ordinal);
        var legacyStream = plan.Streams.SingleOrDefault(static stream => stream.IsLegacyBaseline);

        // platform.core 以冻结 baseline 模型运行现有迁移程序集，避免外部模块模型被 EF
        // 误判为该程序集缺失的 migration；外部模型只由其自己的编译迁移流推进。
        if (legacyStream is null || !appliedMigrations.Contains(legacyStream.ExpectedFrontier))
        {
            var moduleOwnedMigrationIds = plan.Streams
                .Where(static stream => !stream.IsLegacyBaseline)
                .Select(static stream => stream.ModuleId)
                .ToHashSet(StringComparer.Ordinal);
            var coreModels = plan.Models
                .Where(model => !moduleOwnedMigrationIds.Contains(model.OwnerModuleId))
                .ToArray();
            var coreDigest = PersistenceModelDigest.Compute(coreModels);
            var corePlan = plan with
            {
                PlanDigest = coreDigest,
                Models = coreModels,
                Streams = plan.Streams.Where(static stream => stream.IsLegacyBaseline).ToArray()
            };
            var coreOptionsBuilder = new DbContextOptionsBuilder<EfPlatformDbContext>();
            EfCoreProviderOptions.Configure(
                coreOptionsBuilder,
                plan.Provider,
                connectionString,
                EfCoreProviderOptions.GetDefaultMigrationsAssembly(plan.Provider));
            coreOptionsBuilder.ReplaceService<IModelCacheKeyFactory, EfPlatformModelCacheKeyFactory>();
            // legacy snapshot 仍保留迁移生成时的未限定实体名；物理模型由编译计划与
            // DevSchemaSync 报告校验，不能让纯逻辑键限定被误判为缺少 DDL migration。
            coreOptionsBuilder.ConfigureWarnings(warnings =>
                warnings.Ignore(RelationalEventId.PendingModelChangesWarning));
            using (var coreContext = new EfPlatformDbContext(coreOptionsBuilder.Options, corePlan))
            {
                await coreContext.Database.MigrateAsync(cancellationToken);
            }

            appliedMigrations = (await dbContext.Database
                    .GetAppliedMigrationsAsync(cancellationToken))
                .ToHashSet(StringComparer.Ordinal);
        }

        foreach (var stream in _validatedStreams ?? [])
        {
            foreach (var migration in stream.Migrations)
            {
                if (appliedMigrations.Contains(migration.MigrationId))
                {
                    continue;
                }

                await ApplyMigrationAsync(stream.Source, migration, cancellationToken);
                appliedMigrations.Add(migration.MigrationId);
            }
        }
    }

    private IReadOnlyList<ValidatedMigrationStream> GenerateSqlCommands(
        IReadOnlyList<ValidatedMigrationStream> streams)
    {
        var connectionString = dbContext.Database.GetConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("The persistence connection string is unavailable.");
        }

        var optionsBuilder = new DbContextOptionsBuilder<EfPlatformDbContext>();
        EfCoreProviderOptions.Configure(optionsBuilder, plan.Provider, connectionString);
        optionsBuilder.ReplaceService<IModelCacheKeyFactory, EfPlatformModelCacheKeyFactory>();
        using var generationContext = new EfPlatformDbContext(optionsBuilder.Options, plan);
        var generator = generationContext.GetService<IMigrationsSqlGenerator>();

        return streams
            .Select(stream => stream with
            {
                Migrations = stream.Migrations
                    .Select(migration =>
                    {
                        var commands = generator.Generate(
                            migration.Migration.UpOperations,
                            generationContext.Model);
                        if (commands.Any(static command => command.TransactionSuppressed))
                        {
                            throw Failure(
                                PersistenceMigrationFailureCodes.OperationForbidden,
                                stream.Source,
                                migration.MigrationId);
                        }

                        return migration with
                        {
                            Commands = commands
                                .Select(static command => command.CommandText)
                                .ToArray()
                        };
                    })
                    .ToArray()
            })
            .ToArray();
    }

    private async Task ApplyMigrationAsync(
        ModuleMigrationStream stream,
        ValidatedMigration migration,
        CancellationToken cancellationToken)
    {
        await dbContext.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            var connection = dbContext.Database.GetDbConnection();
            await using var transaction = await connection.BeginTransactionAsync(
                IsolationLevel.ReadCommitted,
                cancellationToken);
            await AcquireMigrationLockAsync(
                connection,
                transaction,
                stream,
                migration.MigrationId,
                cancellationToken);

            if (await MigrationHistoryContainsAsync(
                    connection,
                    transaction,
                    migration.MigrationId,
                    cancellationToken))
            {
                await transaction.CommitAsync(cancellationToken);
                return;
            }

            foreach (var sql in migration.Commands)
            {
                await using var command = CreateCommand(connection, transaction, sql);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            await InsertMigrationHistoryAsync(
                connection,
                transaction,
                migration.MigrationId,
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        finally
        {
            await dbContext.Database.CloseConnectionAsync();
        }
    }

    private static async Task AcquireMigrationLockAsync(
        DbConnection connection,
        DbTransaction transaction,
        ModuleMigrationStream stream,
        string migrationId,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(connection, transaction, """
            DECLARE @result int;
            EXEC @result = sys.sp_getapplock
                @Resource = N'NewP.PersistenceBootstrap',
                @LockMode = N'Exclusive',
                @LockOwner = N'Transaction',
                @LockTimeout = 15000;
            SELECT @result;
            """);
        var result = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
        if (result < 0)
        {
            throw Failure(
                PersistenceMigrationFailureCodes.MigrationLockUnavailable,
                stream,
                migrationId);
        }
    }

    private static async Task<bool> MigrationHistoryContainsAsync(
        DbConnection connection,
        DbTransaction transaction,
        string migrationId,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(connection, transaction, """
            SELECT COUNT(*)
            FROM [dbo].[__EFMigrationsHistory]
            WHERE [MigrationId] = @migrationId;
            """);
        AddParameter(command, "@migrationId", migrationId);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) == 1;
    }

    private static async Task InsertMigrationHistoryAsync(
        DbConnection connection,
        DbTransaction transaction,
        string migrationId,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(connection, transaction, """
            INSERT INTO [dbo].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
            VALUES (@migrationId, @productVersion);
            """);
        AddParameter(command, "@migrationId", migrationId);
        AddParameter(command, "@productVersion", ProductInfo.GetVersion());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static DbCommand CreateCommand(
        DbConnection connection,
        DbTransaction transaction,
        string commandText)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = commandText;
        return command;
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private ValidatedMigrationStream ValidateStream(
        ModuleMigrationStream stream,
        VerifiedMigrationAssemblyLoadContext assemblyContext)
    {
        if (stream.Provider != plan.Provider)
        {
            throw Failure(PersistenceMigrationFailureCodes.ProviderUnsupported, stream);
        }

        var mismatchedCutover = stream.Migrations.FirstOrDefault(step =>
            step.LegacyCutover is not null && !step.LegacyCutover.Matches(stream, step));
        if (mismatchedCutover is not null)
        {
            throw Failure(
                PersistenceMigrationFailureCodes.OperationForbidden,
                stream,
                mismatchedCutover.MigrationId);
        }

        var assembly = LoadMigrationAssembly(stream, assemblyContext);
        IReadOnlyDictionary<string, Type> migrationTypes;
        try
        {
            migrationTypes = assembly
                .GetTypes()
                .Where(static type => !type.IsAbstract && typeof(Migration).IsAssignableFrom(type))
                .Select(static type => new
                {
                    Type = type,
                    Id = type.GetCustomAttribute<MigrationAttribute>()?.Id
                })
                .Where(static candidate => !string.IsNullOrWhiteSpace(candidate.Id))
                .ToDictionary(
                    static candidate => candidate.Id!,
                    static candidate => candidate.Type,
                    StringComparer.Ordinal);
        }
        catch (Exception exception) when (exception is not PersistencePlanExecutionException)
        {
            throw Failure(
                PersistenceMigrationFailureCodes.AssemblyInvalid,
                stream,
                innerException: exception);
        }

        var validatedMigrations = new List<ValidatedMigration>(stream.Migrations.Count);
        foreach (var step in stream.Migrations)
        {
            if (!migrationTypes.TryGetValue(step.MigrationId, out var migrationType))
            {
                throw Failure(
                    PersistenceMigrationFailureCodes.MigrationMissing,
                    stream,
                    step.MigrationId);
            }

            Migration migration;
            try
            {
                migration = (Migration)(Activator.CreateInstance(migrationType)
                    ?? throw new InvalidOperationException("Migration construction returned null."));
                typeof(Migration)
                    .GetProperty(nameof(Migration.ActiveProvider))!
                    .SetValue(migration, SqlServerProviderName);
            }
            catch (Exception exception)
            {
                throw Failure(
                    PersistenceMigrationFailureCodes.AssemblyInvalid,
                    stream,
                    step.MigrationId,
                    exception);
            }

            ValidateOperations(stream, step, migration);
            validatedMigrations.Add(new ValidatedMigration(step.MigrationId, migration, []));
        }

        return new ValidatedMigrationStream(stream, assembly, validatedMigrations);
    }

    private void ValidateOperations(
        ModuleMigrationStream stream,
        CompiledMigrationStep step,
        Migration migration)
    {
        if (migration.DownOperations.Count != 0)
        {
            throw Failure(
                PersistenceMigrationFailureCodes.OperationForbidden,
                stream,
                step.MigrationId);
        }

        var ownedModels = plan.Models
            .Where(model => string.Equals(model.OwnerModuleId, stream.ModuleId, StringComparison.Ordinal))
            .ToArray();
        var schemas = ownedModels
            .Select(static model => model.SchemaName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var tables = ownedModels
            .Select(static model => PhysicalKey(model.SchemaName, model.TableName))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var currentTables = plan.Models
            .Select(static model => PhysicalKey(model.SchemaName, model.TableName))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var operation in migration.UpOperations)
        {
            var isAllowed = operation switch
            {
                EnsureSchemaOperation ensureSchema => schemas.Contains(ensureSchema.Name),
                CreateTableOperation createTable =>
                    ownedModels.SingleOrDefault(model =>
                        string.Equals(
                            PhysicalKey(model.SchemaName, model.TableName),
                            PhysicalKey(createTable.Schema, createTable.Name),
                            StringComparison.OrdinalIgnoreCase)) is { } ownedModel
                    && ForeignKeysMatch(createTable, ownedModel)
                    && createTable.CheckConstraints.Count == 0
                    && createTable.Columns.All(IsSafeColumnDefinition),
                CreateIndexOperation createIndex => IsOwnedIndex(createIndex, ownedModels),
                AddColumnOperation addColumn =>
                    tables.Contains(PhysicalKey(addColumn.Schema, addColumn.Table))
                    && IsSafeColumnDefinition(addColumn)
                    && (addColumn.IsNullable || addColumn.DefaultValue is not null),
                DropTableOperation dropTable =>
                    step.LegacyCutover?.AllowsDrop(
                        stream,
                        step,
                        dropTable.Schema ?? "dbo",
                        dropTable.Name) == true
                    && !currentTables.Contains(PhysicalKey(dropTable.Schema, dropTable.Name)),
                _ => false
            };

            if (!isAllowed)
            {
                throw Failure(
                    PersistenceMigrationFailureCodes.OperationForbidden,
                    stream,
                    step.MigrationId);
            }
        }
    }

    private bool IsOwnedIndex(
        CreateIndexOperation operation,
        IReadOnlyList<ModelDefinition> ownedModels)
    {
        var ownedModel = ownedModels.SingleOrDefault(candidate =>
            string.Equals(
                PhysicalKey(candidate.SchemaName, candidate.TableName),
                PhysicalKey(operation.Schema, operation.Table),
                StringComparison.OrdinalIgnoreCase));
        if (ownedModel is null)
        {
            return false;
        }

        // EF 的目标模型还包含外键约定生成的索引；执行端必须与最终 provider
        // 模型精确比对，不能只看 authoring 中显式声明的 IndexDefinition。
        var targetEntity = dbContext.GetService<IDesignTimeModel>().Model.GetEntityTypes().SingleOrDefault(entity =>
            string.Equals(entity.GetTableName(), operation.Table, StringComparison.OrdinalIgnoreCase)
            && string.Equals(
                entity.GetSchema() ?? "dbo",
                operation.Schema ?? "dbo",
                StringComparison.OrdinalIgnoreCase));
        var expected = targetEntity?.GetIndexes().SingleOrDefault(index =>
            string.Equals(index.GetDatabaseName(), operation.Name, StringComparison.Ordinal));
        if (expected is null
            || expected.IsUnique != operation.IsUnique
            || !NullableSequenceEqual(operation.IsDescending, expected.IsDescending)
            || !SqlServerAnnotationsMatch(operation, expected))
        {
            return false;
        }

        var storeObject = StoreObjectIdentifier.Table(operation.Table, operation.Schema ?? "dbo");
        var expectedColumns = expected.Properties
            .Select(property => property.GetColumnName(storeObject))
            .ToArray();
        if (expectedColumns.Any(static column => column is null)
            || !operation.Columns.SequenceEqual(expectedColumns!, StringComparer.Ordinal))
        {
            return false;
        }

        return string.Equals(operation.Filter, expected.GetFilter(), StringComparison.Ordinal);
    }

    private static bool SqlServerAnnotationsMatch(
        CreateIndexOperation operation,
        IReadOnlyIndex expected)
    {
        var actualAnnotations = operation.GetAnnotations()
            .Where(static annotation => annotation.Name.StartsWith("SqlServer:", StringComparison.Ordinal))
            .ToDictionary(static annotation => annotation.Name, static annotation => annotation.Value, StringComparer.Ordinal);
        var expectedAnnotations = expected.GetAnnotations()
            .Where(static annotation => annotation.Name.StartsWith("SqlServer:", StringComparison.Ordinal))
            .ToDictionary(static annotation => annotation.Name, static annotation => annotation.Value, StringComparer.Ordinal);
        return actualAnnotations.Count == expectedAnnotations.Count
            && actualAnnotations.All(pair =>
                expectedAnnotations.TryGetValue(pair.Key, out var expectedValue)
                && AnnotationValueEquals(pair.Value, expectedValue));
    }

    private static bool AnnotationValueEquals(object? actual, object? expected)
    {
        if (actual is string || expected is string)
        {
            return Equals(actual, expected);
        }

        if (actual is IEnumerable actualValues && expected is IEnumerable expectedValues)
        {
            return actualValues.Cast<object?>().SequenceEqual(expectedValues.Cast<object?>());
        }

        return Equals(actual, expected);
    }

    private static bool NullableSequenceEqual<T>(
        IReadOnlyList<T>? actual,
        IReadOnlyList<T>? expected) =>
        actual is null
            ? expected is null
            : expected is not null && actual.SequenceEqual(expected);

    private bool ForeignKeysMatch(CreateTableOperation operation, ModelDefinition model)
    {
        if (operation.ForeignKeys.Count != model.ForeignKeys.Count)
        {
            return false;
        }

        foreach (var actual in operation.ForeignKeys)
        {
            var expected = model.ForeignKeys.SingleOrDefault(foreignKey =>
                string.Equals(foreignKey.Name, actual.Name, StringComparison.Ordinal));
            var principal = expected is null
                ? null
                : plan.Models.SingleOrDefault(candidate =>
                    string.Equals(candidate.LogicalKey, expected.PrincipalModelKey, StringComparison.Ordinal));
            if (expected is null
                || principal is null
                || !actual.Columns.SequenceEqual(
                    expected.Fields.Select(field => model.Fields.Single(candidate => candidate.Name == field).StoreName),
                    StringComparer.Ordinal)
                || !string.Equals(
                    PhysicalKey(actual.PrincipalSchema, actual.PrincipalTable),
                    PhysicalKey(principal.SchemaName, principal.TableName),
                    StringComparison.OrdinalIgnoreCase)
                || !(actual.PrincipalColumns ?? []).SequenceEqual(
                    expected.PrincipalFields.Select(field => principal.Fields.Single(candidate => candidate.Name == field).StoreName),
                    StringComparer.Ordinal)
                || actual.OnDelete != MapDeleteBehavior(expected.DeleteBehavior))
            {
                return false;
            }
        }

        return true;
    }

    private static ReferentialAction MapDeleteBehavior(ForeignKeyDeleteBehavior behavior) => behavior switch
    {
        ForeignKeyDeleteBehavior.Restrict => ReferentialAction.Restrict,
        ForeignKeyDeleteBehavior.Cascade => ReferentialAction.Cascade,
        ForeignKeyDeleteBehavior.SetNull => ReferentialAction.SetNull,
        ForeignKeyDeleteBehavior.NoAction => ReferentialAction.NoAction,
        _ => throw new ArgumentOutOfRangeException(nameof(behavior), behavior, null)
    };

    private static bool IsSafeColumnDefinition(ColumnOperation column) =>
        string.IsNullOrWhiteSpace(column.ComputedColumnSql)
        && string.IsNullOrWhiteSpace(column.DefaultValueSql);

    private static string PhysicalKey(string? schema, string table) =>
        $"{schema ?? "dbo"}.{table}";

    private static Assembly LoadMigrationAssembly(
        ModuleMigrationStream stream,
        VerifiedMigrationAssemblyLoadContext context)
    {
        try
        {
            if (stream.AssemblyImage is not { } image)
            {
                throw new InvalidDataException(
                    "Migration assembly is absent from the compiled verified artifact image.");
            }

            var verifiedBytes = image.CopyBytes();
            var checksum = Convert.ToHexStringLower(SHA256.HashData(verifiedBytes));
            if (!string.Equals(checksum, stream.BundleChecksum, StringComparison.Ordinal))
            {
                throw new InvalidDataException("Migration assembly checksum does not match the compiled plan.");
            }

            var expectedIdentity = ReadIdentity(verifiedBytes);
            var assembly = context.LoadVerifiedImage(stream.ModuleId, verifiedBytes);
            if (!VerifiedAssemblyIdentityPolicy.MatchesExact(assembly.GetName(), expectedIdentity.AssemblyName)
                || assembly.ManifestModule.ModuleVersionId != expectedIdentity.Mvid)
            {
                throw new InvalidDataException(
                    "Loaded migration assembly identity differs from its verified artifact image.");
            }

            return assembly;
        }
        catch (Exception exception)
        {
            throw Failure(
                PersistenceMigrationFailureCodes.AssemblyInvalid,
                stream,
                innerException: exception);
        }
    }

    private static VerifiedAssemblyIdentity ReadIdentity(ReadOnlyMemory<byte> image)
    {
        using var stream = new MemoryStream(image.ToArray(), writable: false);
        using var peReader = new PEReader(stream);
        var metadata = peReader.GetMetadataReader();
        var definition = metadata.GetAssemblyDefinition();
        var assemblyName = new AssemblyName
        {
            Name = metadata.GetString(definition.Name),
            Version = definition.Version,
            CultureName = definition.Culture.IsNil ? null : metadata.GetString(definition.Culture)
        };
#pragma warning disable SYSLIB0037
        assemblyName.ProcessorArchitecture = ReadProcessorArchitecture(peReader.PEHeaders);
#pragma warning restore SYSLIB0037
        if (!definition.PublicKey.IsNil)
        {
            assemblyName.SetPublicKey(metadata.GetBlobBytes(definition.PublicKey));
        }
        if (definition.Flags.HasFlag(AssemblyFlags.Retargetable))
        {
            assemblyName.Flags |= AssemblyNameFlags.Retargetable;
        }
        if (definition.Flags.HasFlag(AssemblyFlags.WindowsRuntime))
        {
            assemblyName.ContentType = AssemblyContentType.WindowsRuntime;
        }

        return new VerifiedAssemblyIdentity(
            assemblyName,
            metadata.GetGuid(metadata.GetModuleDefinition().Mvid));
    }

    private sealed class VerifiedMigrationAssemblyLoadContext
        : AssemblyLoadContext
    {
        private readonly IReadOnlyDictionary<string, VerifiedMigrationImage> _verifiedImages;

        internal VerifiedMigrationAssemblyLoadContext(IReadOnlyCollection<ModuleMigrationStream> streams)
            : base(isCollectible: false)
        {
            _verifiedImages = streams
                .Where(static stream => stream.AssemblyImage is not null)
                .Select(stream =>
                {
                    var bytes = stream.AssemblyImage!.CopyBytes();
                    return new VerifiedMigrationImage(ReadIdentity(bytes), bytes);
                })
                .GroupBy(static image => image.Identity.AssemblyName.Name!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    static group => group.Key,
                    static group => group.Single(),
                    StringComparer.OrdinalIgnoreCase);
        }

        internal Assembly LoadVerifiedImage(string owner, byte[] image)
        {
            var expected = ReadIdentity(image);
            var existing = Assemblies.FirstOrDefault(assembly =>
                VerifiedAssemblyIdentityPolicy.MatchesExact(assembly.GetName(), expected.AssemblyName)
                && assembly.ManifestModule.ModuleVersionId == expected.Mvid);
            if (existing is not null)
            {
                return existing;
            }

            using var stream = new MemoryStream(image, writable: false);
            var assembly = LoadFromStream(stream);
            if (!VerifiedAssemblyIdentityPolicy.MatchesExact(assembly.GetName(), expected.AssemblyName)
                || assembly.ManifestModule.ModuleVersionId != expected.Mvid)
            {
                throw new InvalidDataException(
                    $"Migration assembly for '{owner}' differs from its verified artifact image.");
            }

            return assembly;
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            if (assemblyName.Name is { } name && _verifiedImages.TryGetValue(name, out var image))
            {
                if (!VerifiedAssemblyIdentityPolicy.MatchesExact(image.Identity.AssemblyName, assemblyName))
                {
                    throw new FileLoadException($"Verified migration dependency '{assemblyName.FullName}' does not match the planned image identity.");
                }

                return LoadVerifiedImage(name, image.Bytes);
            }

            if (!VerifiedAssemblyIdentityPolicy.IsHostPlatformSharedDependency(assemblyName))
            {
                throw new FileLoadException($"Migration dependency '{assemblyName.FullName}' is outside the Host/platform shared dependency boundary.");
            }

            return ResolveSharedDependency(assemblyName);
        }

        private static Assembly ResolveSharedDependency(AssemblyName assemblyName)
        {
            var loaded = Default.Assemblies.FirstOrDefault(candidate =>
                VerifiedAssemblyIdentityPolicy.MatchesExact(candidate.GetName(), assemblyName));
            if (loaded is not null)
            {
                return loaded;
            }

            try
            {
                var resolved = Default.LoadFromAssemblyName(assemblyName);
                if (!VerifiedAssemblyIdentityPolicy.MatchesExact(resolved.GetName(), assemblyName))
                {
                    throw new FileLoadException($"Resolved Host/platform shared dependency '{assemblyName.FullName}' has a different identity.");
                }

                return resolved;
            }
            catch (FileNotFoundException)
            {
                // 非 shared-framework 平台程序集继续从受控应用目录的字节装载。
            }

            var root = Path.GetFullPath(AppContext.BaseDirectory);
            var path = Path.GetFullPath(Path.Combine(root, $"{assemblyName.Name}.dll"));
            if (!PathContainment.Contains(root, path) || !File.Exists(path))
            {
                throw new FileLoadException($"Exact Host/platform shared dependency '{assemblyName.FullName}' is unavailable.");
            }

            var bytes = PathContainment.ReadContainedFile(root, path);
            var identity = ReadIdentity(bytes);
            if (!VerifiedAssemblyIdentityPolicy.MatchesExact(identity.AssemblyName, assemblyName))
            {
                throw new FileLoadException($"Host/platform shared dependency '{assemblyName.FullName}' differs from its distribution image.");
            }

            using var stream = new MemoryStream(bytes, writable: false);
            var assembly = Default.LoadFromStream(stream);
            if (!VerifiedAssemblyIdentityPolicy.MatchesExact(assembly.GetName(), assemblyName)
                || assembly.ManifestModule.ModuleVersionId != identity.Mvid)
            {
                throw new FileLoadException($"Loaded Host/platform shared dependency '{assemblyName.FullName}' differs from its distribution image.");
            }

            return assembly;
        }
    }

#pragma warning disable SYSLIB0037
    private static ProcessorArchitecture ReadProcessorArchitecture(PEHeaders headers) =>
        headers.CoffHeader.Machine switch
        {
            Machine.I386 when headers.CorHeader?.Flags.HasFlag(CorFlags.Requires32Bit) == true => ProcessorArchitecture.X86,
            Machine.I386 => ProcessorArchitecture.MSIL,
            Machine.Amd64 => ProcessorArchitecture.Amd64,
            Machine.IA64 => ProcessorArchitecture.IA64,
            Machine.Arm or Machine.ArmThumb2 or Machine.Arm64 => ProcessorArchitecture.Arm,
            _ => ProcessorArchitecture.None
        };
#pragma warning restore SYSLIB0037

    private sealed record VerifiedAssemblyIdentity(AssemblyName AssemblyName, Guid Mvid);

    private sealed record VerifiedMigrationImage(VerifiedAssemblyIdentity Identity, byte[] Bytes);

    private static PersistencePlanExecutionException Failure(
        string failureCode,
        ModuleMigrationStream stream,
        string? migrationId = null,
        Exception? innerException = null) =>
        new(failureCode, stream.ModuleId, migrationId, innerException);

    private sealed record ValidatedMigration(
        string MigrationId,
        Migration Migration,
        IReadOnlyList<string> Commands);

    private sealed record ValidatedMigrationStream(
        ModuleMigrationStream Source,
        Assembly Assembly,
        IReadOnlyList<ValidatedMigration> Migrations);
}
