using System.Reflection;
using ICP.Platform.Acceptance;
using ICP.Platform.Testing;
using Microsoft.Data.SqlClient;
using Xunit;

namespace ICP.Platform.Acceptance.Tests;

[Collection(AcceptanceGateCollection.Name)]
public sealed class SharedDatabaseTeardownTests
{
    [EnvironmentGatedFact("NEWP_RUN_TEST_DATABASE_INTEGRATION", "Personnel 启动失败保留 schema 并释放共享库锁")]
    public Task Personnel_OwnershipFailure_ReleasesLeaseWithoutClearingSchema() => VerifyFailureAsync("Personnel");

    [EnvironmentGatedFact("NEWP_RUN_TEST_DATABASE_INTEGRATION", "Authorization 收尾异常保留 schema 并释放共享库锁")]
    public Task Authorization_OwnershipException_ReleasesLeaseWithoutClearingSchema() => VerifyFailureAsync("Authorization");

    [EnvironmentGatedFact("NEWP_RUN_TEST_DATABASE_INTEGRATION", "Workspace 收尾异常保留 schema 并释放共享库锁")]
    public Task Workspace_OwnershipException_ReleasesLeaseWithoutClearingSchema() => VerifyFailureAsync("Workspace");

    // 直接构造已取得真实租约、但启动未完成的现场，调用正式收尾方法；无需启动浏览器来证明锁的生命周期。
    private static async Task VerifyFailureAsync(string owner)
    {
        var lease = await ReusableSqlServerDatabase.AcquireAsync();
        try
        {
            await ExecuteAsync(lease.ConnectionString, "CREATE TABLE dbo.TeardownSentinel (Id int PRIMARY KEY); INSERT dbo.TeardownSentinel VALUES(1);");
            var root = Path.GetTempPath();
            AcceptanceRunner runner = owner switch
            {
                "Authorization" => new AuthorizationAcceptanceRunner(root, root, new string('a', 24)),
                "Workspace" => new WorkspaceAcceptanceRunner(root, root, new string('a', 24)),
                _ => new PersonnelAcceptanceRunner(root, root, new string('a', 24))
            };
            SetField(runner, "_databaseLease", lease);
            if (owner != "Workspace") SetField(runner, "_databasePrepared", true);
            if (owner != "Personnel") SetField(runner, "_connection", lease.ConnectionString);
            var method = runner.GetType().GetMethod("TeardownAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var teardown = (Task<AcceptanceCleanupResult>)method.Invoke(runner, [CancellationToken.None])!;
            if (owner != "Personnel") await Assert.ThrowsAsync<SqlException>(() => teardown);
            else Assert.Equal("failed", (await teardown).Status);

            Assert.Equal(1, await ExecuteAsync(lease.ConnectionString, "SELECT COUNT(*) FROM dbo.TeardownSentinel;"));
            await using var next = await ReusableSqlServerDatabase.AcquireAsync(lockTimeoutMilliseconds: 0);
            Assert.Equal(0, await ExecuteAsync(next.ConnectionString, "SELECT COUNT(*) FROM sys.tables WHERE is_ms_shipped=0;"));
        }
        finally
        {
            // RED 时旧实现仍持锁，由测试自己兜底归还；修复后该调用无副作用。
            await lease.DisposeAsync();
        }
    }

    private static void SetField(object target, string name, object value) =>
        target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);

    private static async Task<object?> ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        return await command.ExecuteScalarAsync();
    }
}
