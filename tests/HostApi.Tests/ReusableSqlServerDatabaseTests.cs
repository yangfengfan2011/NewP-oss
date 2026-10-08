using ICP.Platform.Testing;
using Microsoft.Data.SqlClient;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

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

    [EnvironmentGatedFact("NEWP_RUN_TEST_DATABASE_INTEGRATION", "清理 DDL 失败时回滚整次清理并释放租约")]
    public async Task CleanupFailure_RollsBackTablesAndForeignKeys()
    {
        var lease = await ReusableSqlServerDatabase.AcquireAsync();
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
            await using var recovered = await ReusableSqlServerDatabase.AcquireAsync(lockTimeoutMilliseconds: 0);
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
