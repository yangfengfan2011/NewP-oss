using System.Text.Json;
using ICP.Platform.Acceptance;
using ICP.Platform.Testing;
using Microsoft.Data.SqlClient;
using Xunit;
using Xunit.Abstractions;

namespace ICP.Platform.Acceptance.Tests;

[Collection(AcceptanceGateCollection.Name)]
public sealed class PublishedFileRecoveryObservationTests(ITestOutputHelper output)
{
    [EnvironmentGatedFact("NEWP_RUN_TEST_DATABASE_INTEGRATION", "真实NewP_Test同文件事务更新与正式观察联查交错20次，锁等待不持数据S锁且状态一致")]
    public async Task RecoveryObservation_SerializesWithFileWriterForTwentyInterleavings()
    {
        await using var database = await ReusableSqlServerDatabase.AcquireAsync();
        await using var writer = new SqlConnection(database.ConnectionString);
        await using var observer = new SqlConnection(database.ConnectionString);
        await using var control = new SqlConnection(database.ConnectionString);
        await writer.OpenAsync(); await observer.OpenAsync(); await control.OpenAsync();
        await SetupAsync(control);
        var isolation = await ReadIsolationAsync(control);
        await ExecuteAsync(observer, "SET DEADLOCK_PRIORITY LOW;");
        for (var iteration = 1; iteration <= 20; iteration++)
        {
            await using var transaction = (SqlTransaction)await writer.BeginTransactionAsync();
            await ExecuteAsync(writer, "DECLARE @result int; EXEC @result=sys.sp_getapplock @Resource=N'Files.file', @LockMode=N'Exclusive', @LockOwner=N'Transaction'; IF @result<0 THROW 51000,'writer lock failed',1;", transaction);
            await ExecuteAsync(writer, "UPDATE dbo.FileUploadLease SET State=N'phase-" + iteration + "' WHERE EID=N'lease';", transaction);
            var read = PublishedComponentAcceptanceFixture.ReadRecoveryFileAsync(observer, "fixture.pdf", CancellationToken.None, iteration % 2 == 0 ? "file" : null);
            var locks = await WaitForLockAsync(control, observer.ServerProcessId, read);
            try
            {
                await ExecuteAsync(writer, "UPDATE dbo.FileRecord SET State=N'phase-" + iteration + "' WHERE EID=N'file';", transaction);
                await transaction.CommitAsync();
            }
            catch
            {
                await transaction.RollbackAsync();
                try { await read; } catch (SqlException) { }
                throw;
            }
            var pair = await read;
            Assert.NotNull(pair);
            Assert.Equal("phase-" + iteration, pair.FileState);
            Assert.Equal(pair.FileState, pair.LeaseState);
            Assert.Equal("APPLICATION", locks.Resource);
            Assert.Equal(0, locks.DataLocks);
            output.WriteLine(JsonSerializer.Serialize(new { iteration, database = "NewP_Test", isolation, waitingResource = locks.Resource,
                dataLocksWhileWaiting = locks.DataLocks, pair.FileState, pair.LeaseState, sql1205 = 0 }));
        }
        Assert.Equal(isolation, await ReadIsolationAsync(control));
    }

    [EnvironmentGatedFact("NEWP_RUN_TEST_DATABASE_INTEGRATION", "真实锁等待取消后释放观察事务；数据库语句错误保持原SqlException")]
    public async Task RecoveryObservation_CancellationAndOtherSqlErrorsPropagate()
    {
        await using var database = await ReusableSqlServerDatabase.AcquireAsync();
        await using var writer = new SqlConnection(database.ConnectionString);
        await using var observer = new SqlConnection(database.ConnectionString);
        await using var control = new SqlConnection(database.ConnectionString);
        await writer.OpenAsync(); await observer.OpenAsync(); await control.OpenAsync();
        await SetupAsync(control);
        await using (var transaction = (SqlTransaction)await writer.BeginTransactionAsync())
        {
            await ExecuteAsync(writer, "DECLARE @result int; EXEC @result=sys.sp_getapplock @Resource=N'Files.file',@LockMode=N'Exclusive',@LockOwner=N'Transaction'; IF @result<0 THROW 51000,'writer lock failed',1;", transaction);
            using var cancelled = new CancellationTokenSource();
            var read = PublishedComponentAcceptanceFixture.ReadRecoveryFileAsync(observer, "fixture.pdf", cancelled.Token, "file");
            Assert.Equal("APPLICATION", (await WaitForLockAsync(control, observer.ServerProcessId, read)).Resource);
            cancelled.Cancel();
            var cancellation = await Record.ExceptionAsync(() => read);
            Assert.True(cancelled.IsCancellationRequested);
            Assert.NotNull(cancellation);
            output.WriteLine(JsonSerializer.Serialize(new { cancellationType = cancellation.GetType().Name,
                sqlNumber = (cancellation as SqlException)?.Number, sqlErrors = (cancellation as SqlException)?.Errors.Cast<SqlError>().Select(error => error.Number).ToArray() }));
            Assert.True(cancellation is OperationCanceledException || cancellation is SqlException { Number: 0 });
            if (cancellation is SqlException sqlCancellation) Assert.All(sqlCancellation.Errors.Cast<SqlError>(), error => Assert.Equal(0, error.Number));
        }
        await ExecuteAsync(control, "DROP TABLE dbo.FileUploadLease;");
        var error = await Assert.ThrowsAsync<SqlException>(() => PublishedComponentAcceptanceFixture.ReadRecoveryFileAsync(observer, "fixture.pdf", CancellationToken.None, "file"));
        Assert.Equal(208, error.Number);
        await using var locks = control.CreateCommand();
        locks.CommandText = "SELECT COUNT(*) FROM sys.dm_tran_locks WHERE request_session_id=@session AND resource_type='APPLICATION';";
        locks.Parameters.AddWithValue("@session", observer.ServerProcessId);
        Assert.Equal(0, (int)(await locks.ExecuteScalarAsync())!);
    }

    [EnvironmentGatedFact("NEWP_RUN_TEST_DATABASE_INTEGRATION", "同一真实读取在文件写锁下由原30秒期限取消，不捕获或改写异常")]
    public async Task RecoveryObservation_KeepsOriginalThirtySecondDeadline()
    {
        await using var database = await ReusableSqlServerDatabase.AcquireAsync();
        await using var writer = new SqlConnection(database.ConnectionString);
        await using var observer = new SqlConnection(database.ConnectionString);
        await writer.OpenAsync(); await observer.OpenAsync();
        await SetupAsync(observer);
        await using var transaction = (SqlTransaction)await writer.BeginTransactionAsync();
        await ExecuteAsync(writer, "DECLARE @result int; EXEC @result=sys.sp_getapplock @Resource=N'Files.file',@LockMode=N'Exclusive',@LockOwner=N'Transaction'; IF @result<0 THROW 51000,'writer lock failed',1;", transaction);
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        CancellationToken observedToken = default;
        var cancellation = await Record.ExceptionAsync(() => PublishedComponentAcceptanceFixture.WaitForRecoveryFileAsync(token =>
        {
            observedToken = token;
            return PublishedComponentAcceptanceFixture.ReadRecoveryFileAsync(observer, "fixture.pdf", token, "file");
        }, _ => true, CancellationToken.None));
        Assert.True(observedToken.IsCancellationRequested);
        Assert.True(cancellation is OperationCanceledException || cancellation is SqlException { Number: 0 });
        if (cancellation is SqlException sqlCancellation) Assert.All(sqlCancellation.Errors.Cast<SqlError>(), error => Assert.Equal(0, error.Number));
        output.WriteLine(JsonSerializer.Serialize(new { deadlineSeconds = 30, elapsedSeconds = elapsed.Elapsed.TotalSeconds, cancellationType = cancellation!.GetType().Name }));
    }

    [EnvironmentGatedFact("NEWP_RUN_TEST_DATABASE_INTEGRATION", "真实sp_getapplock死锁负返回拒绝观察，不吞错或重试")]
    public async Task RecoveryObservation_NegativeApplicationLockResultIsAnError()
    {
        await using var database = await ReusableSqlServerDatabase.AcquireAsync();
        await using var writer = new SqlConnection(database.ConnectionString);
        await using var observer = new SqlConnection(database.ConnectionString);
        await using var control = new SqlConnection(database.ConnectionString);
        await writer.OpenAsync(); await observer.OpenAsync(); await control.OpenAsync();
        await SetupAsync(control);
        await ExecuteAsync(observer, "SET DEADLOCK_PRIORITY LOW; EXEC sys.sp_getapplock @Resource=N'fixture.guard',@LockMode=N'Exclusive',@LockOwner=N'Session';");
        await using var transaction = (SqlTransaction)await writer.BeginTransactionAsync();
        await ExecuteAsync(writer, "EXEC sys.sp_getapplock @Resource=N'Files.file',@LockMode=N'Exclusive',@LockOwner=N'Transaction';", transaction);
        var read = PublishedComponentAcceptanceFixture.ReadRecoveryFileAsync(observer, "fixture.pdf", CancellationToken.None, "file");
        await WaitForLockAsync(control, observer.ServerProcessId, read);
        var acquireGuard = ExecuteAsync(writer, "EXEC sys.sp_getapplock @Resource=N'fixture.guard',@LockMode=N'Exclusive',@LockOwner=N'Transaction',@LockTimeout=10000;", transaction);
        try
        {
            var error = await Assert.ThrowsAsync<SqlException>(() => read);
            Assert.Equal(51000, error.Number);
            Assert.Contains("File recovery observation lock failed", error.Message);
        }
        finally
        {
            await ExecuteAsync(observer, "EXEC sys.sp_releaseapplock @Resource=N'fixture.guard',@LockOwner=N'Session';");
            await acquireGuard;
        }
    }

    private static async Task SetupAsync(SqlConnection connection) => await ExecuteAsync(connection, """
        CREATE TABLE dbo.FileRecord(EID nvarchar(128) PRIMARY KEY, FileName nvarchar(256), State nvarchar(256), StorageKey nvarchar(256));
        CREATE TABLE dbo.FileUploadLease(EID nvarchar(128) PRIMARY KEY, FileId nvarchar(128) REFERENCES dbo.FileRecord(EID), State nvarchar(256));
        INSERT dbo.FileRecord VALUES(N'file',N'fixture.pdf',N'phase-0',N'key');
        INSERT dbo.FileUploadLease VALUES(N'lease',N'file',N'phase-0');
        """);

    // 不用时间猜交错：确认实际观察SPID已在等待锁后才允许写事务完成。
    private static async Task<(string Resource, int DataLocks)> WaitForLockAsync(SqlConnection control, int session, Task read)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!read.IsCompleted)
        {
            await using var command = control.CreateCommand();
            command.CommandText = """
                SELECT TOP(1) resource_type,
                    (SELECT COUNT(*) FROM sys.dm_tran_locks WHERE request_session_id=@session AND resource_type IN('KEY','PAGE','OBJECT')
                        AND request_status='GRANT' AND request_mode IN('S','IS','SIX','U','X'))
                FROM sys.dm_tran_locks WHERE request_session_id=@session AND request_status='WAIT';
                """;
            command.Parameters.AddWithValue("@session", session);
            await using var rows = await command.ExecuteReaderAsync(deadline.Token);
            if (await rows.ReadAsync(deadline.Token)) return (rows.GetString(0), rows.GetInt32(1));
            await Task.Yield();
        }
        await read;
        throw new InvalidOperationException("未在真实写事务持锁期间观察到读取等待。");
    }

    private static async Task<string> ReadIsolationAsync(SqlConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT CONCAT(is_read_committed_snapshot_on,'|',snapshot_isolation_state_desc) FROM sys.databases WHERE name=DB_NAME();";
        return (string)(await command.ExecuteScalarAsync())!;
    }

    private static async Task ExecuteAsync(SqlConnection connection, string sql, SqlTransaction? transaction = null)
    {
        await using var command = new SqlCommand(sql, connection, transaction);
        await command.ExecuteNonQueryAsync();
    }
}
