using ICP.Platform.Testing;
using Microsoft.Data.SqlClient;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

[Collection(nameof(ReusableSqlServerDatabaseCollection))]
public sealed class ReusableSqlServerDatabaseTests
{
    [Fact]
    public void DevelopmentConnection_OnlySuppliesServerAndCredentials()
    {
        var actual = ReusableSqlServerDatabase.ResolveConnection(readEnvironment: key =>
            key == "NEWP_DEV_SQLSERVER_CONNECTION_STRING" ? "Server=localhost;Database=Development;Integrated Security=true" : null);
        var connection = new SqlConnectionStringBuilder(actual);
        Assert.Equal("NewP_Test", connection.InitialCatalog);
        Assert.Equal("localhost", connection.DataSource);
        Assert.True(connection.IntegratedSecurity);
        Assert.False(connection.Pooling);
        Assert.Equal(0, connection.ConnectRetryCount);
    }

    [Theory]
    [InlineData("master")]
    [InlineData("NewP_Development")]
    [InlineData("NewP_Test_2")]
    [InlineData("")]
    public void ExplicitTestConnection_RejectsOtherCatalogs(string catalog)
    {
        Assert.Throws<InvalidOperationException>(() => ReusableSqlServerDatabase.UseTestCatalog(
            $"Server=localhost;Database={catalog};Integrated Security=true", dedicated: true));
    }

    [Fact]
    public void ExplicitTestConnection_TakesPrecedence()
    {
        var connection = ReusableSqlServerDatabase.ResolveConnection(readEnvironment: key => key switch
        {
            "NEWP_TEST_SQLSERVER_CONNECTION_STRING" => "Server=test-server;Database=NewP_Test;Integrated Security=true",
            "NEWP_DEV_SQLSERVER_CONNECTION_STRING" => "Server=development;Database=Development;Integrated Security=true",
            _ => null
        });
        Assert.Equal("test-server", new SqlConnectionStringBuilder(connection).DataSource);
    }

    [EnvironmentGatedFact("NEWP_RUN_TEST_DATABASE_INTEGRATION", "专用测试库跨租约复用、清理与互斥")]
    public async Task Lease_ReusesCatalogCleansForeignKeysAndRejectsConcurrentAccess()
    {
        DateTime created;
        await using (var first = await ReusableSqlServerDatabase.AcquireAsync())
        {
            created = (DateTime)(await ExecuteAsync(first.ConnectionString,
                "SELECT create_date FROM sys.databases WHERE name=DB_NAME();"))!;
            await ExecuteAsync(first.ConnectionString,
                "CREATE TABLE dbo.TestParent (Id int PRIMARY KEY); CREATE TABLE dbo.TestChild (Id int PRIMARY KEY, ParentId int REFERENCES dbo.TestParent(Id)); INSERT dbo.TestParent VALUES(1); INSERT dbo.TestChild VALUES(2,1);");
            await Assert.ThrowsAsync<TimeoutException>(() => ReusableSqlServerDatabase.AcquireAsync(lockTimeoutMilliseconds: 0));
            using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ReusableSqlServerDatabase.AcquireAsync(cancellationToken: cancellation.Token));
            Assert.Equal(1, await ExecuteAsync(first.ConnectionString, "SELECT COUNT(*) FROM dbo.TestChild;"));
        }
        await using var second = await ReusableSqlServerDatabase.AcquireAsync();
        Assert.Equal(created, await ExecuteAsync(second.ConnectionString, "SELECT create_date FROM sys.databases WHERE name=DB_NAME();"));
        Assert.Equal(0, await ExecuteAsync(second.ConnectionString, "SELECT COUNT(*) FROM sys.tables WHERE is_ms_shipped=0;"));
    }

    [EnvironmentGatedFact("NEWP_RUN_TEST_DATABASE_INTEGRATION", "两库并行隔离数据、清理与等待，默认租约仍独占原库")]
    public async Task ParallelLeases_IsolateDataCleanupAndReuseWithoutChangingDefaultLease()
    {
        await using var first = await ReusableSqlServerDatabase.AcquireAsync();
        await ExecuteAsync(first.ConnectionString,
            "CREATE TABLE dbo.TestParent (Id int PRIMARY KEY); CREATE TABLE dbo.TestChild (Id int PRIMARY KEY, ParentId int REFERENCES dbo.TestParent(Id)); INSERT dbo.TestParent VALUES(1); INSERT dbo.TestChild VALUES(11,1); CREATE SEQUENCE dbo.TestSequence START WITH 100;");
        await using var second = await ReusableSqlServerDatabase.AcquireAsync(lockTimeoutMilliseconds: 0, allowParallel: true);
        Assert.Equal("NewP_Test", new SqlConnectionStringBuilder(first.ConnectionString).InitialCatalog);
        Assert.Equal("NewP_Test_2", new SqlConnectionStringBuilder(second.ConnectionString).InitialCatalog);
        await ExecuteAsync(second.ConnectionString,
            "CREATE TABLE dbo.TestParent (Id int PRIMARY KEY); CREATE TABLE dbo.TestChild (Id int PRIMARY KEY, ParentId int REFERENCES dbo.TestParent(Id)); INSERT dbo.TestParent VALUES(2); INSERT dbo.TestChild VALUES(22,2); CREATE SEQUENCE dbo.TestSequence START WITH 200;");
        var secondCreated = await ExecuteAsync(second.ConnectionString, "SELECT create_date FROM sys.databases WHERE name=DB_NAME();");
        await Assert.ThrowsAsync<TimeoutException>(() => ReusableSqlServerDatabase.AcquireAsync(lockTimeoutMilliseconds: 0));
        await Assert.ThrowsAsync<TimeoutException>(() => ReusableSqlServerDatabase.AcquireAsync(lockTimeoutMilliseconds: 0, allowParallel: true));
        using (var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100)))
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ReusableSqlServerDatabase.AcquireAsync(
                cancellationToken: cancellation.Token, allowParallel: true));
        Assert.Equal(11, await ExecuteAsync(first.ConnectionString, "SELECT Id FROM dbo.TestChild;"));
        Assert.Equal(22, await ExecuteAsync(second.ConnectionString, "SELECT Id FROM dbo.TestChild;"));

        var waiting = ReusableSqlServerDatabase.AcquireAsync(lockTimeoutMilliseconds: 5_000, allowParallel: true);
        await second.DisposeAsync();
        await using var reused = await waiting.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal("NewP_Test_2", new SqlConnectionStringBuilder(reused.ConnectionString).InitialCatalog);
        Assert.Equal(secondCreated, await ExecuteAsync(reused.ConnectionString, "SELECT create_date FROM sys.databases WHERE name=DB_NAME();"));
        Assert.Equal(0, await ExecuteAsync(reused.ConnectionString, "SELECT COUNT(*) FROM sys.tables WHERE is_ms_shipped=0;"));
        Assert.Equal(0, await ExecuteAsync(reused.ConnectionString, "SELECT COUNT(*) FROM sys.sequences;"));
        Assert.Equal(11, await ExecuteAsync(first.ConnectionString, "SELECT Id FROM dbo.TestChild;"));
        Assert.Equal(100L, await ExecuteAsync(first.ConnectionString, "SELECT NEXT VALUE FOR dbo.TestSequence;"));
    }

    [EnvironmentGatedFact("NEWP_RUN_TEST_DATABASE_INTEGRATION", "清理 DDL 失败时回滚整次清理并释放租约")]
    public async Task CleanupFailure_RollsBackTablesAndForeignKeys()
    {
        foreach (var useSecond in new[] { false, true })
        {
            await using var primary = useSecond ? await ReusableSqlServerDatabase.AcquireAsync() : null;
            var lease = await ReusableSqlServerDatabase.AcquireAsync(allowParallel: useSecond);
            try
            {
                await ExecuteAsync(lease.ConnectionString,
                    "CREATE TABLE dbo.CleanupParent (Id int PRIMARY KEY); CREATE TABLE dbo.CleanupChild (Id int PRIMARY KEY, ParentId int CONSTRAINT FK_Cleanup REFERENCES dbo.CleanupParent(Id));");
                await ExecuteAsync(lease.ConnectionString,
                    "CREATE VIEW dbo.CleanupBlocker WITH SCHEMABINDING AS SELECT Id FROM dbo.CleanupParent;");
                await Assert.ThrowsAsync<SqlException>(() => lease.DisposeAsync().AsTask());
                Assert.Equal(2, await ExecuteAsync(lease.ConnectionString, "SELECT COUNT(*) FROM sys.tables WHERE name IN ('CleanupParent','CleanupChild');"));
                Assert.Equal(1, await ExecuteAsync(lease.ConnectionString, "SELECT COUNT(*) FROM sys.foreign_keys WHERE name='FK_Cleanup';"));
            }
            finally
            {
                await ExecuteAsync(lease.ConnectionString, "DROP VIEW IF EXISTS dbo.CleanupBlocker;");
                await lease.DisposeAsync();
                await using var recovered = await ReusableSqlServerDatabase.AcquireAsync(lockTimeoutMilliseconds: 0, allowParallel: useSecond);
                Assert.Equal(0, await ExecuteAsync(recovered.ConnectionString, "SELECT COUNT(*) FROM sys.tables WHERE is_ms_shipped=0;"));
            }
        }
    }

    [EnvironmentGatedFact("NEWP_RUN_TEST_DATABASE_INTEGRATION", "第二库缺失标记时拒绝清理与接管，并释放租约")]
    public async Task SecondaryCatalog_MissingMarkerPreservesDataAndRejectsAcquisition()
    {
        await using var first = await ReusableSqlServerDatabase.AcquireAsync();
        await using var second = await ReusableSqlServerDatabase.AcquireAsync(allowParallel: true);
        await ExecuteAsync(first.ConnectionString, "CREATE TABLE dbo.MarkerGuard(Id int); INSERT dbo.MarkerGuard VALUES(1);");
        await ExecuteAsync(second.ConnectionString, "CREATE TABLE dbo.MarkerGuard(Id int); INSERT dbo.MarkerGuard VALUES(2);");
        try
        {
            await ExecuteAsync(second.ConnectionString, "EXEC sys.sp_dropextendedproperty @name=N'NewP.AutomatedTests';");
            await Assert.ThrowsAsync<SqlException>(() => second.DisposeAsync().AsTask());
            await Assert.ThrowsAsync<SqlException>(() => ReusableSqlServerDatabase.AcquireAsync(lockTimeoutMilliseconds: 0, allowParallel: true));
            Assert.Equal(1, await ExecuteAsync(first.ConnectionString, "SELECT Id FROM dbo.MarkerGuard;"));
            Assert.Equal(2, await ExecuteAsync(second.ConnectionString, "SELECT Id FROM dbo.MarkerGuard;"));
        }
        finally
        {
            await ExecuteAsync(second.ConnectionString,
                "IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'NewP.AutomatedTests') EXEC sys.sp_addextendedproperty @name=N'NewP.AutomatedTests', @value=N'disposable-schema';");
            await second.DisposeAsync();
            await using var recovered = await ReusableSqlServerDatabase.AcquireAsync(lockTimeoutMilliseconds: 0, allowParallel: true);
            Assert.Equal(0, await ExecuteAsync(recovered.ConnectionString, "SELECT COUNT(*) FROM sys.tables WHERE is_ms_shipped=0;"));
        }
    }

    [EnvironmentGatedFact("NEWP_RUN_TEST_DATABASE_INTEGRATION", "登记的数据库触发器只从当前租约的库清理")]
    public async Task TriggerCleanup_OnlyRemovesRegisteredTriggerFromOwnedCatalog()
    {
        await using var first = await ReusableSqlServerDatabase.AcquireAsync();
        await using var second = await ReusableSqlServerDatabase.AcquireAsync(allowParallel: true);
        try
        {
            const string create = "CREATE TRIGGER PoolOwnedTrigger ON DATABASE FOR CREATE_TABLE AS BEGIN SET NOCOUNT ON; END;";
            await ExecuteAsync(first.ConnectionString, create);
            await ExecuteAsync(second.ConnectionString, create);
            await ExecuteAsync(second.ConnectionString,
                "CREATE TRIGGER PoolOtherTrigger ON DATABASE FOR CREATE_TABLE AS BEGIN SET NOCOUNT ON; END;");
            await second.CleanupDatabaseTriggersAsync(["PoolOwnedTrigger"]);
            Assert.Equal(1, await ExecuteAsync(first.ConnectionString, "SELECT COUNT(*) FROM sys.triggers WHERE parent_class=0 AND name='PoolOwnedTrigger';"));
            Assert.Equal(0, await ExecuteAsync(second.ConnectionString, "SELECT COUNT(*) FROM sys.triggers WHERE parent_class=0 AND name='PoolOwnedTrigger';"));
            Assert.Equal(1, await ExecuteAsync(second.ConnectionString, "SELECT COUNT(*) FROM sys.triggers WHERE parent_class=0 AND name='PoolOtherTrigger';"));
        }
        finally
        {
            await first.CleanupDatabaseTriggersAsync(["PoolOwnedTrigger"]);
            await second.CleanupDatabaseTriggersAsync(["PoolOwnedTrigger", "PoolOtherTrigger"]);
        }
    }

    private static async Task<object?> ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        return await command.ExecuteScalarAsync();
    }
}

// 这些故障用例会暂时移除标记或留下阻止清理的 view，不能与业务回归争抢同一库。
[CollectionDefinition(nameof(ReusableSqlServerDatabaseCollection), DisableParallelization = true)]
public sealed class ReusableSqlServerDatabaseCollection;
