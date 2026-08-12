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
/// EF Core provider 内部的完整 Up 模块迁移执行器。先静态检查整份候选计划，再允许
/// ownership guard 建立连接；这样非法 bundle 不会留下 marker、history 或业务 schema 残留。
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
        var executableStreams = plan.Streams
            .Where(static stream => stream.Migrations.Count > 0)
            .ToArray();
        if (executableStreams.Length == 0)
        {
            return [];
        }

        if (plan.Provider is not PersistenceProvider.SqlServer)
        {
            var stream = executableStreams[0];
            throw Failure(
                PersistenceMigrationFailureCodes.ProviderUnsupported,
                stream);
        }

        var assemblyContext = new VerifiedMigrationAssemblyLoadContext(executableStreams);
        var validatedStreams = executableStreams
            .Select(stream => ValidateStream(stream, assemblyContext))
            .ToArray();
        return GenerateSqlCommands(validatedStreams);
    }

    private IReadOnlyList<CompiledMigrationHistoryEntry> BuildExpectedHistory(
        IReadOnlyList<ValidatedMigrationStream> streams)
    {
        var entries = new List<CompiledMigrationHistoryEntry>();
        var compatibilityPlatformStream = plan.Streams.SingleOrDefault(static stream =>
            stream.IsPlatformStream && stream.Migrations.Count == 0);
        if (compatibilityPlatformStream is not null)
        {
            var legacyMigrations = dbContext.Database.GetMigrations().ToArray();
            var frontierIndex = Array.FindIndex(
                legacyMigrations,
                migrationId => string.Equals(
                    migrationId,
                    compatibilityPlatformStream.ExpectedFrontier,
                    StringComparison.Ordinal));
            if (frontierIndex < 0)
            {
                throw new PersistenceHistoryException(
                    PersistenceHistoryFailureCodes.FrontierMismatch,
                    compatibilityPlatformStream.ExpectedFrontier);
            }

            foreach (var migrationId in legacyMigrations[..(frontierIndex + 1)])
            {
                // 非 SQL Server provider 尚未重建基线，继续使用既有只读合成事实；
                // SQL Server 平台基础流拥有完整版本化 migration step，不进入这里。
                var bundleDigest = Hash($"platform-bundle/v1|{plan.Provider}|{migrationId}");
                var contentDigest = Hash($"platform-content|{migrationId}");
                var modelDigest = Hash($"platform-model/v1|{plan.Provider}|{migrationId}");
                var providerTargetDigest = Hash($"platform-provider-target/v1|{plan.Provider}|{migrationId}");
                var transitionDigest = Hash($"platform-transition|{plan.Provider}|{migrationId}");
                entries.Add(new CompiledMigrationHistoryEntry(
                    0,
                    plan.HistoryVersion,
                    migrationId,
                    compatibilityPlatformStream.ModuleId,
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
        var compatibilityPlatformStream = plan.Streams.SingleOrDefault(static stream =>
            stream.IsPlatformStream && stream.Migrations.Count == 0);
        var appliedMigrations = (await dbContext.Database
                .GetAppliedMigrationsAsync(cancellationToken))
            .ToHashSet(StringComparer.Ordinal);

        // Oracle/MySQL 尚未重建其平台流，继续通过既有 provider migrations assembly 推进；
        // SQL Server 平台基础流和模块流均走上面的版本化执行器。
        if (compatibilityPlatformStream is not null
            && !appliedMigrations.Contains(compatibilityPlatformStream.ExpectedFrontier))
        {
            var moduleOwnedMigrationIds = plan.Streams
                .Where(static stream => !stream.IsPlatformStream)
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
                Streams = plan.Streams.Where(static stream => stream.IsPlatformStream).ToArray()
            };
            var coreOptionsBuilder = new DbContextOptionsBuilder<EfPlatformDbContext>();
            EfCoreProviderOptions.Configure(
                coreOptionsBuilder,
                plan.Provider,
                connectionString,
                EfCoreProviderOptions.GetDefaultMigrationsAssembly(plan.Provider));
            coreOptionsBuilder.ReplaceService<IModelCacheKeyFactory, EfPlatformModelCacheKeyFactory>();
            // legacy snapshot 仍保留迁移生成时的未限定实体名；物理模型由编译计划与
            // migration 后只读 Provider Target 检查校验，不能让纯逻辑键限定被误判为缺少 DDL migration。
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

    public async Task VerifyAppliedHistoryAsync(CancellationToken cancellationToken)
    {
        ValidateBeforeConnection();
        var reconciliation = await ReconcileHistoryAsync(cancellationToken);
        if (reconciliation.Pending.FirstOrDefault() is { } pending)
        {
            throw new PersistenceHistoryException(
                PersistenceHistoryFailureCodes.FrontierMismatch,
                pending.MigrationId);
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
        // AlterColumn 等完整 Up 会让 SQL Server generator 重建受影响索引；该路径需要
        // design-time facet，不能使用裁剪过的 read-optimized runtime model。
        var designTimeModel = generationContext.GetService<IDesignTimeModel>().Model;

        return streams
            .Select(stream => stream with
            {
                Migrations = stream.Migrations
                    .Select(migration =>
                    {
                        var commands = generator.Generate(
                            migration.Migration.UpOperations,
                            designTimeModel);
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

    private async Task<UnifiedMigrationHistoryReconciliation> ReconcileHistoryAsync(CancellationToken cancellationToken)
    {
        await dbContext.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            var connection = dbContext.Database.GetDbConnection();
            await using var transaction = await connection.BeginTransactionAsync(
                IsolationLevel.ReadCommitted,
                cancellationToken);
            await AcquireHistoryLockAsync(connection, transaction, cancellationToken);
            var reconciliation = await ReadAndReconcileHistoryAsync(connection, transaction, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return reconciliation;
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
        var compatibilityPlatformOwner = plan.Streams.SingleOrDefault(static stream =>
            stream.IsPlatformStream && stream.Migrations.Count == 0)?.ModuleId;
        var applied = new List<CompiledMigrationHistoryEntry>(migrationIds.Count);
        for (var position = 0; position < migrationIds.Count; position++)
        {
            var migrationId = migrationIds[position];
            if (factsById.TryGetValue(migrationId, out var fact))
            {
                applied.Add(fact);
                continue;
            }

            if (expectedById.TryGetValue(migrationId, out var compatibilityFact)
                && string.Equals(
                    compatibilityFact.OwnerModuleId,
                    compatibilityPlatformOwner,
                    StringComparison.Ordinal))
            {
                // 仅非 SQL Server 的既有 provider stream 缺少版本化事实；不补写数据库。
                applied.Add(compatibilityFact);
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

            // Down 可以作为 migration 的人工回退定义存在，但 Host 永远只验证、生成并
            // 执行 UpOperations；不能因定义了 Down 而拒绝合法 Up，也不能自动调用 Down。
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
