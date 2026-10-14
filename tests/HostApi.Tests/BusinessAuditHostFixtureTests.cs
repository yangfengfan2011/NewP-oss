using ICP.Platform.Modules.Files;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Hosting;
using Xunit;
using Xunit.Abstractions;
using Microsoft.Data.SqlClient;
using System.Text.Json;

namespace ICP.Platform.HostApi.Tests;

public sealed class BusinessAuditHostFixtureTests(ITestOutputHelper output)
{
    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "真实 Document/Files fixture 重启保留私有存储根与首次服务配置")]
    public async Task DocumentHost_RestartsWithTheSameStorageRootModelsAndServiceConfiguration()
    {
        var marker = new ConfigurationMarker();
        var configurations = 0;
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(services =>
        {
            configurations++;
            services.AddSingleton(marker);
            services.PostConfigure<FilesOptions>(options => options.UploadLeaseHours = 12);
        }, includeWorkspace: true);

        AssertConfiguration();
        Directory.CreateDirectory(fixture.StorageRoot);
        var sentinel = Path.Combine(fixture.StorageRoot, "fixture-owned-sentinel.txt");
        await File.WriteAllTextAsync(sentinel, "preserved across host instances");
        await fixture.RestartWithActionsAsync(new Dictionary<string, bool> { ["delete"] = false });
        AssertConfiguration();
        await fixture.RestartWithReadActionAsync(true);
        AssertConfiguration();
        Assert.Equal(3, configurations);
        Assert.Equal("preserved across host instances", await File.ReadAllTextAsync(sentinel));

        // TestServer 仅验证组合与重启配置；正式迁移和 OS 重启另由固定发行物 fixture 证明。
        void AssertConfiguration()
        {
            Assert.Same(marker, fixture.Services.GetRequiredService<ConfigurationMarker>());
            Assert.Equal(fixture.StorageRoot, fixture.Services.GetRequiredService<IConfiguration>()["Files:StorageRoot"]);
            var options = fixture.Services.GetRequiredService<IOptions<FilesOptions>>().Value;
            Assert.Equal(fixture.StorageRoot, options.StorageRoot);
            Assert.Equal(12, options.UploadLeaseHours);
            using var scope = fixture.Services.CreateScope();
            var model = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>().Model;
            foreach (var name in new[] { "Document", "DocumentVersion", "DocumentIteration", "DocumentFileLink", "FileRecord", "FileUploadLease", "FileUsage" })
                Assert.NotNull(model.FindEntityType(name));
        }
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "真实 fixture 释放前先停止已启动服务并通知 ApplicationStopping")]
    public async Task DocumentHost_DisposalStopsHostedServicesBeforeReleasingTheirLifetime()
    {
        ShutdownProbe? probe = null;
        await using (var fixture = await BusinessAuditHostFixture.CreateAsync(services =>
        {
            services.AddSingleton<IHostedService>(provider =>
                probe = new ShutdownProbe(provider.GetRequiredService<IHostApplicationLifetime>()));
        }, includeDocument: true))
        {
            Assert.NotNull(probe);
            Assert.False(probe.StopWasCalled);
        }
        Assert.True(probe!.StopWasCalled);
        Assert.True(probe.DisposedAfterStopping);
    }

    private sealed class ShutdownProbe(IHostApplicationLifetime lifetime) : IHostedService, IDisposable
    {
        internal bool StopWasCalled { get; private set; }
        internal bool DisposedAfterStopping { get; private set; }
        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken)
        {
            StopWasCalled = true;
            return Task.CompletedTask;
        }
        public void Dispose() => DisposedAfterStopping = StopWasCalled && lifetime.ApplicationStopping.IsCancellationRequested;
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "同一 typed SQL reader 读通空结果和自有进程实际会话的全部列类型")]
    public async Task DatabaseStageReader_ReadsEmptyAndSelfProcessRowsWithProviderTypes()
    {
        var connectionString = ReusableSqlServerDatabase.ResolveConnection();
        await using var ownConnection = new SqlConnection(connectionString);
        await ownConnection.OpenAsync();
        var empty = await FeatureCatalogHostFixture.ReadDatabaseStageAsync(connectionString, -1, true);
        var self = await FeatureCatalogHostFixture.ReadDatabaseStageAsync(connectionString, Environment.ProcessId, false);
        Assert.Empty(empty.GetProperty("sessions").EnumerateArray());
        Assert.NotEmpty(self.GetProperty("sessions").EnumerateArray());
        Assert.Equal(ReusableSqlServerDatabase.Catalog, self.GetProperty("catalog").GetString());
        Assert.Equal(empty.GetProperty("databaseId").GetInt32(), self.GetProperty("databaseId").GetInt32());
        var columns = self.GetProperty("columns").EnumerateArray().ToArray();
        Assert.Equal(new[] { "sessionId", "databaseId", "sessionStatus", "command", "requestStatus", "waitType", "waitMilliseconds", "blockingSessionId", "isAppLockRequest", "isHistoryRequest" },
            columns.Select(column => column.GetProperty("ColumnName").GetString()));
        foreach (var session in self.GetProperty("sessions").EnumerateArray())
        {
            Assert.True(session.GetProperty("sessionId").GetInt32() > 0);
            Assert.Equal(self.GetProperty("databaseId").GetInt32(), session.GetProperty("databaseId").GetInt32());
            foreach (var key in new[] { "sessionStatus", "command", "requestStatus", "waitType" })
                Assert.Equal(JsonValueKind.String, session.GetProperty(key).ValueKind);
            _ = session.GetProperty("waitMilliseconds").GetInt32();
            _ = session.GetProperty("blockingSessionId").GetInt32();
            _ = session.GetProperty("isAppLockRequest").GetBoolean();
            _ = session.GetProperty("isHistoryRequest").GetBoolean();
        }
        output.WriteLine(JsonSerializer.Serialize(new { empty, self }));
    }

    private sealed class ConfigurationMarker { }
}
