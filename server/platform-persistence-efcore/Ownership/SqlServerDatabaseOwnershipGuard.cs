using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using ICP.Platform.Persistence.Abstractions;
using ICP.Platform.Persistence.Ownership;
using Microsoft.EntityFrameworkCore;

namespace ICP.Platform.Persistence.EfCore.Ownership;

/// <summary>
/// SQL Server migration 前置所有权守卫。除在已证明为空的目录中建立 marker 外，
/// 所有拒绝路径都发生在业务 schema DDL 之前。
/// </summary>
public sealed class SqlServerDatabaseOwnershipGuard : IPersistenceMigrationGuard
{
    internal const string MarkerTableName = "__NewPDatabaseOwnership";

    private readonly EfPlatformDbContext _dbContext;
    private readonly WorktreeDatabaseIdentity _identity;

    public SqlServerDatabaseOwnershipGuard(
        EfPlatformDbContext dbContext,
        WorktreeDatabaseIdentity identity)
    {
        _dbContext = dbContext;
        _identity = identity;
    }

    public async Task<PersistenceMigrationGuardResult> VerifyAsync(
        CancellationToken cancellationToken = default)
    {
        var connection = _dbContext.Database.GetDbConnection();
        await _dbContext.Database.OpenConnectionAsync(cancellationToken);

        try
        {
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            await AcquireBootstrapLockAsync(connection, transaction, cancellationToken);

            var expectedMarker = await BuildExpectedMarkerAsync(connection, transaction, cancellationToken);
            var markerExists = await MarkerTableExistsAsync(connection, transaction, cancellationToken);
            var marker = markerExists
                ? await ReadMarkerAsync(connection, transaction, cancellationToken)
                : null;
            var tables = await ReadUserTablesAsync(connection, transaction, cancellationToken);
            var appliedMigrations = await ReadAppliedMigrationsAsync(connection, transaction, cancellationToken);
            var knownMigrations = _dbContext.Database.GetMigrations().ToArray();

            var inspection = new DatabaseOwnershipInspection(
                marker,
                tables.Count(static table => !string.Equals(
                    table,
                    MarkerTableName,
                    StringComparison.OrdinalIgnoreCase)),
                appliedMigrations,
                tables);
            var decision = DatabaseOwnershipPolicy.Evaluate(expectedMarker, inspection, knownMigrations);

            if (decision.Kind is DatabaseOwnershipDecisionKind.Reject)
            {
                throw new DatabaseOwnershipException(
                    decision.FailureCode ?? DatabaseOwnershipFailureCodes.MarkerInvalid);
            }

            if (decision.Kind is DatabaseOwnershipDecisionKind.EstablishMarker)
            {
                await CreateMarkerAsync(connection, transaction, expectedMarker, cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            return new PersistenceMigrationGuardResult(decision.DatabaseAhead);
        }
        finally
        {
            await _dbContext.Database.CloseConnectionAsync();
        }
    }

    private static async Task AcquireBootstrapLockAsync(
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
        var result = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
        if (result < 0)
        {
            throw new DatabaseOwnershipException(DatabaseOwnershipFailureCodes.MarkerInvalid);
        }
    }

    private async Task<DatabaseOwnershipMarker> BuildExpectedMarkerAsync(
        DbConnection connection,
        DbTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(connection, transaction, """
            SELECT CONVERT(nvarchar(36), [database_guid])
            FROM [sys].[database_recovery_status]
            WHERE [database_id] = DB_ID();
            """);
        var databaseGuid = Convert.ToString(await command.ExecuteScalarAsync(cancellationToken));
        if (string.IsNullOrWhiteSpace(databaseGuid)
            || string.IsNullOrWhiteSpace(connection.DataSource)
            || string.IsNullOrWhiteSpace(connection.Database))
        {
            throw new DatabaseOwnershipException(DatabaseOwnershipFailureCodes.MarkerInvalid);
        }

        return DatabaseOwnershipMarker.Create(
            _identity,
            "SqlServer",
            Fingerprint(connection.DataSource),
            Fingerprint($"{connection.Database}|{databaseGuid}"));
    }

    private static async Task<bool> MarkerTableExistsAsync(
        DbConnection connection,
        DbTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(connection, transaction, """
            SELECT CASE WHEN OBJECT_ID(N'[dbo].[__NewPDatabaseOwnership]', N'U') IS NULL THEN 0 ELSE 1 END;
            """);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) == 1;
    }

    private static async Task<DatabaseOwnershipMarker> ReadMarkerAsync(
        DbConnection connection,
        DbTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(connection, transaction, """
            SELECT [Version], [RepositoryId], [WorktreeId], [Provider], [ServerFingerprint], [CatalogFingerprint]
            FROM [dbo].[__NewPDatabaseOwnership]
            WHERE [MarkerId] = 1;
            """);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new DatabaseOwnershipException(DatabaseOwnershipFailureCodes.MarkerInvalid);
        }

        var marker = new DatabaseOwnershipMarker(
            reader.GetInt32(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5));

        if (await reader.ReadAsync(cancellationToken))
        {
            throw new DatabaseOwnershipException(DatabaseOwnershipFailureCodes.MarkerInvalid);
        }

        return marker;
    }

    private static async Task<HashSet<string>> ReadUserTablesAsync(
        DbConnection connection,
        DbTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(connection, transaction, """
            SELECT [name]
            FROM [sys].[tables]
            WHERE [is_ms_shipped] = 0;
            """);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var tables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (await reader.ReadAsync(cancellationToken))
        {
            tables.Add(reader.GetString(0));
        }

        return tables;
    }

    private static async Task<string[]> ReadAppliedMigrationsAsync(
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

        return migrations.ToArray();
    }

    private static async Task CreateMarkerAsync(
        DbConnection connection,
        DbTransaction transaction,
        DatabaseOwnershipMarker marker,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(connection, transaction, """
            CREATE TABLE [dbo].[__NewPDatabaseOwnership]
            (
                [MarkerId] int NOT NULL CONSTRAINT [PK___NewPDatabaseOwnership] PRIMARY KEY,
                [Version] int NOT NULL,
                [RepositoryId] char(64) NOT NULL,
                [WorktreeId] char(64) NOT NULL,
                [Provider] nvarchar(32) NOT NULL,
                [ServerFingerprint] char(64) NOT NULL,
                [CatalogFingerprint] char(64) NOT NULL,
                [CreatedAtUtc] datetime2 NOT NULL CONSTRAINT [DF___NewPDatabaseOwnership_CreatedAtUtc] DEFAULT SYSUTCDATETIME(),
                CONSTRAINT [CK___NewPDatabaseOwnership_SingleRow] CHECK ([MarkerId] = 1)
            );

            INSERT INTO [dbo].[__NewPDatabaseOwnership]
                ([MarkerId], [Version], [RepositoryId], [WorktreeId], [Provider], [ServerFingerprint], [CatalogFingerprint])
            VALUES
                (1, @version, @repositoryId, @worktreeId, @provider, @serverFingerprint, @catalogFingerprint);

            EXEC(N'
                CREATE TRIGGER [dbo].[TR___NewPDatabaseOwnership_Immutable]
                ON [dbo].[__NewPDatabaseOwnership]
                INSTEAD OF UPDATE, DELETE
                AS
                BEGIN
                    THROW 51000, ''The NewP database ownership marker is immutable.'', 1;
                END');
            """);
        AddParameter(command, "@version", marker.Version);
        AddParameter(command, "@repositoryId", marker.RepositoryId);
        AddParameter(command, "@worktreeId", marker.WorktreeId);
        AddParameter(command, "@provider", marker.Provider);
        AddParameter(command, "@serverFingerprint", marker.ServerFingerprint);
        AddParameter(command, "@catalogFingerprint", marker.CatalogFingerprint);
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

    private static string Fingerprint(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value.Trim().ToUpperInvariant())))
            .ToLowerInvariant();
}
