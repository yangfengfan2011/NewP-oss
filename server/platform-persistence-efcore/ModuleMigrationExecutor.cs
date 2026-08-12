using System.Data;
using System.Data.Common;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using ICP.Platform.Kernel.ModuleLoading;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Persistence.Planning;
using ICP.Platform.Persistence.EfCore.ProviderTargets;
using ICP.Platform.Persistence.ProviderTargets;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

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
    private IReadOnlyList<CompiledMigrationHistoryEntry>? _expectedHistory;
    private ProviderTargetSnapshot? _providerTarget;

    public void ValidateBeforeConnection()
    {
        if (_validatedStreams is not null)
        {
            return;
        }

        if (!string.Equals(
                plan.HistoryVersion,
                PersistenceHistoryVersions.ReconciliationV1,
                StringComparison.Ordinal))
        {
            throw new PersistenceHistoryException(PersistenceHistoryFailureCodes.VersionUnsupported);
        }

        _validatedStreams = ValidationCache.GetValue(
            plan,
            _ => new Lazy<IReadOnlyList<ValidatedMigrationStream>>(
                CompileValidatedStreams,
                LazyThreadSafetyMode.ExecutionAndPublication)).Value;
        _expectedHistory = BuildExpectedHistory(_validatedStreams);
        _ = UnifiedMigrationHistoryReconciler.Reconcile(
            plan.HistoryVersion,
            [],
            _expectedHistory);
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

    private IReadOnlyList<CompiledMigrationHistoryEntry> BuildExpectedHistory(
        IReadOnlyList<ValidatedMigrationStream> streams)
    {
        var entries = new List<CompiledMigrationHistoryEntry>();
        var legacyStream = plan.Streams.SingleOrDefault(static stream => stream.IsLegacyBaseline);
        if (legacyStream is not null)
        {
            var legacyMigrations = dbContext.Database.GetMigrations().ToArray();
            var frontierIndex = Array.FindIndex(
                legacyMigrations,
                migrationId => string.Equals(
                    migrationId,
                    legacyStream.ExpectedFrontier,
                    StringComparison.Ordinal));
            if (frontierIndex < 0)
            {
                throw new PersistenceHistoryException(
                    PersistenceHistoryFailureCodes.FrontierMismatch,
                    legacyStream.ExpectedFrontier);
            }

            foreach (var migrationId in legacyMigrations[..(frontierIndex + 1)])
            {
                // Legacy Baseline 没有版本化 fact；这些合成字段只用于稳定锚定后续
                // module 前缀，不能依赖会随模块切出 baseline 而变化的当前模型摘要。
                var bundleDigest = Hash($"legacy-bundle/v1|{plan.Provider}|{migrationId}");
                var contentDigest = Hash($"legacy-content|{migrationId}");
                var modelDigest = Hash($"legacy-model/v1|{plan.Provider}|{migrationId}");
                var providerTargetDigest = Hash($"legacy-provider-target/v1|{plan.Provider}|{migrationId}");
                var transitionDigest = Hash($"legacy-transition|{plan.Provider}|{migrationId}");
                entries.Add(new CompiledMigrationHistoryEntry(
                    0,
                    plan.HistoryVersion,
                    migrationId,
                    legacyStream.ModuleId,
                    bundleDigest,
                    contentDigest,
                    modelDigest,
                    modelDigest,
                    string.Empty,
                    ProviderTargetSchemaVersions.TargetV1,
                    providerTargetDigest,
                    ProviderTargetSchemaVersions.TransitionV1,
                    transitionDigest));
            }
        }

        foreach (var stream in streams)
        {
            foreach (var migration in stream.Migrations)
            {
                entries.Add(new CompiledMigrationHistoryEntry(
                    0,
                    plan.HistoryVersion,
                    migration.MigrationId,
                    stream.Source.ModuleId,
                    UnifiedMigrationHistoryDigest.ComputeBundleDigest(
                        stream.Source.ModuleId,
                        stream.Source.Provider,
                        migration.Step),
                    migration.Step.ContentDigest,
                    migration.Step.FromModelDigest,
                    migration.Step.ToModelDigest,
                    string.Empty,
                    migration.Transition.To.SchemaVersion,
                    UnifiedMigrationHistoryDigest.ComputeProviderTargetFactDigest(
                        migration.Transition),
                    migration.Transition.SchemaVersion,
                    UnifiedMigrationHistoryDigest.ComputeProviderTransitionFactDigest(
                        migration.Transition)));
            }
        }

        var ordered = entries
            .OrderBy(static entry => entry.MigrationId, StringComparer.Ordinal)
            .Select((entry, position) => entry with { Position = position })
            .ToArray();
        return UnifiedMigrationHistoryDigest.BindPlanDigests(ordered);
    }

    private static string Hash(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    public async Task ApplyAsync(CancellationToken cancellationToken)
    {
        ValidateBeforeConnection();

        var connectionString = dbContext.Database.GetConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("The persistence connection string is unavailable.");
        }

        await ReconcileHistoryAsync(cancellationToken);
        var legacyStream = plan.Streams.SingleOrDefault(static stream => stream.IsLegacyBaseline);
        var appliedMigrations = (await dbContext.Database
                .GetAppliedMigrationsAsync(cancellationToken))
            .ToHashSet(StringComparer.Ordinal);

        // platform.core 以冻结 baseline 模型运行现有迁移程序集，避免外部模块模型被 EF
        // 误判为该程序集缺失的 migration；外部模型只由其自己的编译迁移流推进。
        if (legacyStream is not null && !appliedMigrations.Contains(legacyStream.ExpectedFrontier))
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
            await ReconcileHistoryAsync(cancellationToken);
        }

        var validatedById = (_validatedStreams ?? [])
            .SelectMany(stream => stream.Migrations.Select(migration => new { Stream = stream.Source, Migration = migration }))
            .ToDictionary(static item => item.Migration.MigrationId, StringComparer.Ordinal);
        foreach (var expected in _expectedHistory ?? [])
        {
            if (!validatedById.TryGetValue(expected.MigrationId, out var item)
                || appliedMigrations.Contains(expected.MigrationId))
            {
                continue;
            }

            await ApplyMigrationAsync(item.Stream, item.Migration, expected, cancellationToken);
            appliedMigrations.Add(expected.MigrationId);
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

    private async Task ReconcileHistoryAsync(CancellationToken cancellationToken)
    {
        await dbContext.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            var connection = dbContext.Database.GetDbConnection();
            await using var transaction = await connection.BeginTransactionAsync(
                IsolationLevel.ReadCommitted,
                cancellationToken);
            await AcquireHistoryLockAsync(connection, transaction, cancellationToken);
            await ReadAndReconcileHistoryAsync(connection, transaction, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        finally
        {
            await dbContext.Database.CloseConnectionAsync();
        }
    }

    private async Task<UnifiedMigrationHistoryReconciliation> ReadAndReconcileHistoryAsync(
        DbConnection connection,
        DbTransaction transaction,
        CancellationToken cancellationToken)
    {
        var expected = _expectedHistory
            ?? throw new InvalidOperationException("Migration history target has not been validated.");
        var migrationIds = await ReadAppliedMigrationIdsAsync(connection, transaction, cancellationToken);
        var facts = await ReadMigrationFactsAsync(connection, transaction, cancellationToken);
        var duplicateFact = facts
            .GroupBy(static fact => fact.MigrationId, StringComparer.Ordinal)
            .FirstOrDefault(static group => group.Count() > 1)?.First();
        if (duplicateFact is not null)
        {
            throw new PersistenceHistoryException(
                PersistenceHistoryFailureCodes.Duplicate,
                duplicateFact.MigrationId);
        }

        var factsById = facts.ToDictionary(static fact => fact.MigrationId, StringComparer.Ordinal);
        var expectedById = expected.ToDictionary(static fact => fact.MigrationId, StringComparer.Ordinal);
        var legacyOwner = plan.Streams.SingleOrDefault(static stream => stream.IsLegacyBaseline)?.ModuleId;
        var applied = new List<CompiledMigrationHistoryEntry>(migrationIds.Count);
        for (var position = 0; position < migrationIds.Count; position++)
        {
            var migrationId = migrationIds[position];
            if (factsById.TryGetValue(migrationId, out var fact))
            {
                applied.Add(fact);
                continue;
            }

            if (expectedById.TryGetValue(migrationId, out var legacy)
                && string.Equals(legacy.OwnerModuleId, legacyOwner, StringComparison.Ordinal))
            {
                // Legacy EF migrations 早于版本化执行事实。显式编译并排序的 ID 前缀是
                // 唯一兼容路径；这里不向数据库补写 fact。
                applied.Add(legacy);
                continue;
            }

            if (expectedById.ContainsKey(migrationId))
            {
                throw new PersistenceHistoryException(
                    PersistenceHistoryFailureCodes.FactMissing,
                    migrationId);
            }

            applied.Add(UnknownFact(position, migrationId));
        }

        var appliedIds = migrationIds.ToHashSet(StringComparer.Ordinal);
        var orphan = facts.FirstOrDefault(fact => !appliedIds.Contains(fact.MigrationId));
        if (orphan is not null)
        {
            throw new PersistenceHistoryException(
                PersistenceHistoryFailureCodes.Unknown,
                orphan.MigrationId);
        }

        return UnifiedMigrationHistoryReconciler.Reconcile(plan.HistoryVersion, applied, expected);
    }

    private static CompiledMigrationHistoryEntry UnknownFact(int position, string migrationId) =>
        new(
            position,
            PersistenceHistoryVersions.ReconciliationV1,
            migrationId,
            "unknown",
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            ProviderTargetSchemaVersions.TargetV1,
            string.Empty,
            ProviderTargetSchemaVersions.TransitionV1,
            string.Empty);

    private static async Task<IReadOnlyList<string>> ReadAppliedMigrationIdsAsync(
        DbConnection connection,
        DbTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(connection, transaction, """
            IF OBJECT_ID(N'[dbo].[__EFMigrationsHistory]', N'U') IS NULL
                SELECT CAST(NULL AS nvarchar(150)) AS [MigrationId] WHERE 1 = 0;
            ELSE
                SELECT [MigrationId] FROM [dbo].[__EFMigrationsHistory] ORDER BY [MigrationId];
            """);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var migrations = new List<string>();
        while (await reader.ReadAsync(cancellationToken))
        {
            migrations.Add(reader.GetString(0));
        }

        return migrations;
    }

    private static async Task<IReadOnlyList<CompiledMigrationHistoryEntry>> ReadMigrationFactsAsync(
        DbConnection connection,
        DbTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(connection, transaction, """
            IF OBJECT_ID(N'[dbo].[__NewPMigrationHistory]', N'U') IS NULL
                SELECT
                    CAST(NULL AS int) AS [Position],
                    CAST(NULL AS nvarchar(64)) AS [HistoryVersion],
                    CAST(NULL AS nvarchar(150)) AS [MigrationId],
                    CAST(NULL AS nvarchar(256)) AS [OwnerModuleId],
                    CAST(NULL AS char(64)) AS [BundleDigest],
                    CAST(NULL AS char(64)) AS [ContentDigest],
                    CAST(NULL AS char(64)) AS [FromModelDigest],
                    CAST(NULL AS char(64)) AS [ToModelDigest],
                    CAST(NULL AS char(64)) AS [PlanDigest],
                    CAST(NULL AS nvarchar(64)) AS [ProviderTargetVersion],
                    CAST(NULL AS char(64)) AS [ProviderTargetDigest],
                    CAST(NULL AS nvarchar(64)) AS [ProviderTransitionVersion],
                    CAST(NULL AS char(64)) AS [ProviderTransitionDigest]
                WHERE 1 = 0;
            ELSE
                SELECT
                    [Position], [HistoryVersion], [MigrationId], [OwnerModuleId],
                    [BundleDigest], [ContentDigest], [FromModelDigest], [ToModelDigest], [PlanDigest],
                    [ProviderTargetVersion], [ProviderTargetDigest],
                    [ProviderTransitionVersion], [ProviderTransitionDigest]
                FROM [dbo].[__NewPMigrationHistory]
                ORDER BY [Position];
            """);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var facts = new List<CompiledMigrationHistoryEntry>();
        while (await reader.ReadAsync(cancellationToken))
        {
            facts.Add(new CompiledMigrationHistoryEntry(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.GetString(7),
                reader.GetString(8),
                reader.GetString(9),
                reader.GetString(10),
                reader.GetString(11),
                reader.GetString(12)));
        }

        return facts;
    }

    private static async Task AcquireHistoryLockAsync(
        DbConnection connection,
        DbTransaction transaction,
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
        if (Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) < 0)
        {
            throw new PersistencePlanExecutionException(
                PersistenceMigrationFailureCodes.MigrationLockUnavailable,
                "platform.persistence");
        }
    }

    private async Task ApplyMigrationAsync(
        ModuleMigrationStream stream,
        ValidatedMigration migration,
        CompiledMigrationHistoryEntry expectedHistory,
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

            var reconciliation = await ReadAndReconcileHistoryAsync(
                connection,
                transaction,
                cancellationToken);
            if (reconciliation.Applied.Any(entry => string.Equals(
                    entry.MigrationId,
                    migration.MigrationId,
                    StringComparison.Ordinal)))
            {
                await transaction.CommitAsync(cancellationToken);
                return;
            }

            if (reconciliation.Pending.FirstOrDefault() is not { } next
                || !string.Equals(next.MigrationId, migration.MigrationId, StringComparison.Ordinal))
            {
                throw new PersistenceHistoryException(
                    PersistenceHistoryFailureCodes.FrontierMismatch,
                    migration.MigrationId);
            }

            await EnsureMigrationHistoryTablesAsync(connection, transaction, cancellationToken);

            await VerifyDataPreconditionsAsync(
                connection,
                transaction,
                stream,
                migration,
                cancellationToken);

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
            await InsertMigrationHistoryFactAsync(
                connection,
                transaction,
                expectedHistory,
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

    private static async Task EnsureMigrationHistoryTablesAsync(
        DbConnection connection,
        DbTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(connection, transaction, """
            IF OBJECT_ID(N'[dbo].[__EFMigrationsHistory]', N'U') IS NULL
            BEGIN
                CREATE TABLE [dbo].[__EFMigrationsHistory]
                (
                    [MigrationId] nvarchar(150) NOT NULL,
                    [ProductVersion] nvarchar(32) NOT NULL,
                    CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
                );
            END;

            IF OBJECT_ID(N'[dbo].[__NewPMigrationHistory]', N'U') IS NULL
            BEGIN
                CREATE TABLE [dbo].[__NewPMigrationHistory]
                (
                    [Position] int NOT NULL,
                    [HistoryVersion] nvarchar(64) NOT NULL,
                    [MigrationId] nvarchar(150) NOT NULL,
                    [OwnerModuleId] nvarchar(256) NOT NULL,
                    [BundleDigest] char(64) NOT NULL,
                    [ContentDigest] char(64) NOT NULL,
                    [FromModelDigest] char(64) NOT NULL,
                    [ToModelDigest] char(64) NOT NULL,
                    [PlanDigest] char(64) NOT NULL,
                    [ProviderTargetVersion] nvarchar(64) NOT NULL,
                    [ProviderTargetDigest] char(64) NOT NULL,
                    [ProviderTransitionVersion] nvarchar(64) NOT NULL,
                    [ProviderTransitionDigest] char(64) NOT NULL,
                    CONSTRAINT [PK___NewPMigrationHistory] PRIMARY KEY ([MigrationId]),
                    CONSTRAINT [UX___NewPMigrationHistory_Position] UNIQUE ([Position])
                );
            END;
            """);
        await command.ExecuteNonQueryAsync(cancellationToken);
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

    private static async Task InsertMigrationHistoryFactAsync(
        DbConnection connection,
        DbTransaction transaction,
        CompiledMigrationHistoryEntry fact,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(connection, transaction, """
            INSERT INTO [dbo].[__NewPMigrationHistory]
            (
                [Position], [HistoryVersion], [MigrationId], [OwnerModuleId],
                [BundleDigest], [ContentDigest], [FromModelDigest], [ToModelDigest], [PlanDigest],
                [ProviderTargetVersion], [ProviderTargetDigest],
                [ProviderTransitionVersion], [ProviderTransitionDigest]
            )
            VALUES
            (
                @position, @historyVersion, @migrationId, @ownerModuleId,
                @bundleDigest, @contentDigest, @fromModelDigest, @toModelDigest, @planDigest,
                @providerTargetVersion, @providerTargetDigest,
                @providerTransitionVersion, @providerTransitionDigest
            );
            """);
        AddParameter(command, "@position", fact.Position);
        AddParameter(command, "@historyVersion", fact.HistoryVersion);
        AddParameter(command, "@migrationId", fact.MigrationId);
        AddParameter(command, "@ownerModuleId", fact.OwnerModuleId);
        AddParameter(command, "@bundleDigest", fact.BundleDigest);
        AddParameter(command, "@contentDigest", fact.ContentDigest);
        AddParameter(command, "@fromModelDigest", fact.FromModelDigest);
        AddParameter(command, "@toModelDigest", fact.ToModelDigest);
        AddParameter(command, "@planDigest", fact.PlanDigest);
        AddParameter(command, "@providerTargetVersion", fact.ProviderTargetVersion);
        AddParameter(command, "@providerTargetDigest", fact.ProviderTargetDigest);
        AddParameter(command, "@providerTransitionVersion", fact.ProviderTransitionVersion);
        AddParameter(command, "@providerTransitionDigest", fact.ProviderTransitionDigest);
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

        if (stream.Migrations.Count == 0
            || !string.Equals(
                stream.Migrations[^1].MigrationId,
                stream.ExpectedFrontier,
                StringComparison.Ordinal))
        {
            throw Failure(
                PersistenceHistoryFailureCodes.FrontierMismatch,
                stream,
                stream.ExpectedFrontier);
        }

        var duplicateMigrationId = stream.Migrations
            .GroupBy(static migration => migration.MigrationId, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(static group => group.Count() > 1)?.Key;
        if (duplicateMigrationId is not null)
        {
            throw Failure(
                PersistenceMigrationFailureCodes.MigrationIdConflict,
                stream,
                duplicateMigrationId);
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

        var candidates = new List<ProviderMigrationCandidate>(stream.Migrations.Count);
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

            if (migration.DownOperations.Count != 0)
            {
                throw Failure(
                    PersistenceMigrationFailureCodes.OperationForbidden,
                    stream,
                    step.MigrationId);
            }

            candidates.Add(new ProviderMigrationCandidate(step, migration));
        }

        try
        {
            var validatedMigrations = new ProviderMigrationOperationValidator(plan, ProviderTarget)
                .Validate(stream, candidates)
                .Select(static migration => new ValidatedMigration(
                    migration.Step,
                    migration.MigrationId,
                    migration.Migration,
                    migration.Transition,
                    migration.TransitionDigest,
                    migration.Preconditions,
                    []))
                .ToArray();
            return new ValidatedMigrationStream(stream, assembly, validatedMigrations);
        }
        catch (ProviderOperationValidationException exception)
        {
            throw Failure(
                PersistenceMigrationFailureCodes.OperationForbidden,
                stream,
                exception.MigrationId,
                exception);
        }
    }

    private static async Task VerifyDataPreconditionsAsync(
        DbConnection connection,
        DbTransaction transaction,
        ModuleMigrationStream stream,
        ValidatedMigration migration,
        CancellationToken cancellationToken)
    {
        foreach (var precondition in migration.Preconditions)
        {
            var predicate = precondition.Column is null
                ? "1 = 1"
                : $"{QuoteIdentifier(precondition.Column)} IS NULL";
            // HOLDLOCK 把读取条件保留到当前 migration 事务结束，避免检查后、
            // DDL 前又插入不符合条件的新行。全局 app lock 另外负责串行化 Host。
            await using var command = CreateCommand(
                connection,
                transaction,
                $"SELECT CASE WHEN EXISTS (SELECT 1 FROM {QuoteTable(precondition.Schema, precondition.Table)} WITH (UPDLOCK, HOLDLOCK) WHERE {predicate}) THEN 1 ELSE 0 END;");
            if (Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) != 0)
            {
                throw Failure(
                    precondition.FailureCode,
                    stream,
                    migration.MigrationId);
            }
        }
    }

    private static string QuoteTable(string schema, string table) =>
        $"{QuoteIdentifier(schema)}.{QuoteIdentifier(table)}";

    private static string QuoteIdentifier(string identifier) =>
        $"[{identifier.Replace("]", "]]", StringComparison.Ordinal)}]";

    private ProviderTargetSnapshot ProviderTarget => _providerTarget ??=
        EfProviderTargetSource.Project(dbContext.GetService<IDesignTimeModel>().Model, plan);

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
        CompiledMigrationStep Step,
        string MigrationId,
        Migration Migration,
        ProviderTransitionSnapshot Transition,
        string TransitionDigest,
        IReadOnlyList<ProviderMigrationDataPrecondition> Preconditions,
        IReadOnlyList<string> Commands);

    private sealed record ValidatedMigrationStream(
        ModuleMigrationStream Source,
        Assembly Assembly,
        IReadOnlyList<ValidatedMigration> Migrations);
}
