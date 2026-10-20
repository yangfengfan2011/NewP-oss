using System.Collections.Concurrent;
using System.Data.Common;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Persistence.Ownership;
using ICP.Platform.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;
using static ICP.Platform.HostApi.Tests.EntityOwnershipBackfillSqlServerHttpApiTests;

namespace ICP.Platform.HostApi.Tests;

public sealed class EntityOwnershipCostSqlServerHttpApiTests(ITestOutputHelper output)
{
    [EnvironmentGatedFact("NEWP_RUN_ENTITY_OWNERSHIP_SQLSERVER_CAPACITY", "显式101个真实旧Folder跨内部批次补齐，记录SQL、分配、锁区间及分页成本")]
    public async Task SelectedLegacyObjects_CrossIoBatchWithoutBusinessLimit_RecordsActualCosts()
    {
        using var measurement = new OwnershipReadPlanDiagnostics(output, "maintenance");
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        var position = await ContainerAsync(fixture, admin);
        var seed = await CreateAsync(fixture, admin, position, "WorkspaceFolder", "folders", "create", "容量旧对象原型");
        var ids = new List<string>();
        string database; string diagnosticConnection;
        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
            database = db.Database.GetDbConnection().Database;
            diagnosticConnection = db.Database.GetConnectionString()!;
            var folders = db.Set<Dictionary<string, object?>>("WorkspaceFolder");
            var links = db.Set<Dictionary<string, object?>>("WorkspaceFolderContent");
            var row = await folders.AsNoTracking().SingleAsync(row => EF.Property<string>(row, "EID") == seed.Id);
            var link = await links.AsNoTracking().SingleAsync(row => EF.Property<string>(row, "LID") == seed.LinkId);
            // 复制当前合法Folder的标量形状准备旧行；无子表、文件或版本树，历史旧值有意冻结。
            for (var index = 0; index < 101; index++)
            {
                var id = "WorkspaceFolder_" + Guid.NewGuid().ToString("N");
                var lid = "WorkspaceFolderContent_" + Guid.NewGuid().ToString("N");
                folders.Add(new(row) { ["EID"] = id, ["ENAME"] = $"旧归属-{index:D3}", ["OwnerId"] = null, ["OwnerDepartmentId"] = null });
                links.Add(new(link) { ["LID"] = lid, ["CEID"] = id, ["TreeOrdinal"] = 1000L + index });
                ids.Add(lid);
            }
            await db.SaveChangesAsync();
            // EF新增行会重算CreatedBy；恢复本场景明确冻结的旧创建账号后，才测量真实可解析存量。
            await folders.Where(row => EF.Property<string?>(row, "OwnerId") == null)
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => EF.Property<string>(row, "CreatedBy"), fixture.ActorId));
            Assert.Equal(101, await folders.CountAsync(row => EF.Property<string?>(row, "OwnerId") == null
                && EF.Property<string?>(row, "OwnerDepartmentId") == null && EF.Property<string>(row, "CreatedBy") == fixture.ActorId));
            var accounts = await scope.ServiceProvider.GetRequiredService<IAccountOwnershipDirectory>().FindManyAsync([fixture.ActorId]);
            Assert.False(string.IsNullOrWhiteSpace(accounts[fixture.ActorId].DepartmentId));
        }
        using var observation = new SqlObservation(database);
        measurement.Database = database;
        var point = Point(fixture, "workspace.folder.initializeOwnership");
        var allocated = GC.GetTotalAllocatedBytes();
        observation.Begin();
        measurement.Begin("pending-100-of-101", currentReadOnly: true);
        using var pending = await FeatureHttpTestRequests.SendAsync(admin, point, HttpMethod.Get, PendingUrl(position, pageSize: measurement.Enabled ? 100 : 20), dataAccess: OwnershipDeclaration(fixture, "workspace.folder.initializeOwnership", point));
        var page = await ReadOkAsync(pending);
        var pendingCost = observation.End();
        await measurement.EndAsync(Encoding.UTF8.GetByteCount(page.GetRawText()), observation.CommandCount, diagnosticConnection);
        Assert.Equal(101, page.GetProperty("total").GetInt32());
        Assert.Equal(measurement.Enabled ? 100 : 20, page.GetProperty("items").GetArrayLength());
        output.WriteLine(JsonSerializer.Serialize(new { scenario = "ownership pending", objects = 101, depth = 2, actors = 1, configuredRules = 0,
            responseBytes = System.Text.Encoding.UTF8.GetByteCount(page.GetRawText()), allocatedBytes = GC.GetTotalAllocatedBytes() - allocated, cost = pendingCost }));
        if (!measurement.Enabled)
        {
            using var cancellation = new CancellationTokenSource();
            var registrations = 0;
            fixture.Fault.AfterRegister = async (change, token) =>
            {
                if (change.EventType != "entity.ownership.backfilled" || ++registrations != 3) return;
                cancellation.Cancel();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            };
            allocated = GC.GetTotalAllocatedBytes();
            observation.Begin();
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, "/api/workspace/ownership/initialize") { Content = JsonContent.Create(new { position, linkIds = ids }) };
                request.Headers.Add("X-NewP-Feature", point.Position);
                request.Headers.Add("X-NewP-Data-Declaration", OwnershipDeclaration(fixture, "workspace.folder.initializeOwnership", point).Id);
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => admin.SendAsync(request, cancellation.Token));
            }
            finally { fixture.Fault.AfterRegister = null; }
            var cancelCost = observation.End();
            Assert.Equal(3, registrations);
            using (var checkScope = fixture.Services.CreateScope())
            {
                var check = checkScope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
                Assert.Equal(101, await check.Set<Dictionary<string, object?>>("WorkspaceFolder")
                    .CountAsync(row => EF.Property<string?>(row, "OwnerId") == null));
                Assert.False(await check.Set<Dictionary<string, object?>>("BusinessChange")
                    .AnyAsync(row => EF.Property<string>(row, "EventType") == "entity.ownership.backfilled"));
            }
            output.WriteLine(JsonSerializer.Serialize(new { scenario = "ownership cancellation rollback", objects = 101, updatedBeforeCancellation = 3,
                allocatedBytes = GC.GetTotalAllocatedBytes() - allocated, cost = cancelCost }));
        }
        allocated = GC.GetTotalAllocatedBytes();
        observation.Begin();
        measurement.Begin("initialize-101");
        using var initialized = await FeatureHttpTestRequests.SendAsync(admin, point, HttpMethod.Post, "/api/workspace/ownership/initialize", new { position, linkIds = ids }, OwnershipDeclaration(fixture, "workspace.folder.initializeOwnership", point));
        var receipt = await ReadOkAsync(initialized);
        var cost = observation.End();
        await measurement.EndAsync(Encoding.UTF8.GetByteCount(receipt.GetRawText()), observation.CommandCount, diagnosticConnection);
        Assert.Equal(101, receipt.GetProperty("changed").GetInt32());
        Assert.True(observation.CommandCount > 0);
        Assert.True(observation.ObservedLock);
        output.WriteLine(JsonSerializer.Serialize(new { scenario = "ownership selected batch", objects = 101, depth = 2, actors = 1, configuredRules = 0,
            responseBytes = System.Text.Encoding.UTF8.GetByteCount(receipt.GetRawText()), allocatedBytes = GC.GetTotalAllocatedBytes() - allocated, cost }));
        using var finalScope = fixture.Services.CreateScope();
        var finalDb = finalScope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
        Assert.Equal(101, await finalDb.Set<Dictionary<string, object?>>("BusinessChange")
            .CountAsync(row => EF.Property<string>(row, "EventType") == "entity.ownership.backfilled"));
        Assert.False(await finalDb.Set<Dictionary<string, object?>>("WorkspaceFolder")
            .AnyAsync(row => EF.Property<string?>(row, "OwnerId") == null || EF.Property<string?>(row, "OwnerDepartmentId") == null));
    }

    // 观察真实SqlClient命令而非仅EF回调，因此权限、图锁、原生命令及事件写入均计入同一窗口。
    private sealed class SqlObservation : IObserver<DiagnosticListener>, IObserver<KeyValuePair<string, object?>>, IDisposable
    {
        private readonly List<IDisposable> subscriptions = [];
        private readonly ConcurrentQueue<string> commands = new();
        private readonly Stopwatch clock = new();
        private readonly string database;
        private readonly IDisposable all;
        private bool active;
        private double? lockRequestedMs;
        internal int CommandCount => commands.Count;
        internal bool ObservedLock => lockRequestedMs is not null;

        internal SqlObservation(string database)
        {
            this.database = database;
            all = DiagnosticListener.AllListeners.Subscribe(this);
        }
        internal void Begin()
        {
            commands.Clear(); lockRequestedMs = null; clock.Restart(); active = true;
        }
        internal object End()
        {
            active = false; clock.Stop();
            return new { elapsedMs = clock.Elapsed.TotalMilliseconds, sqlCommands = commands.Count,
                roleReads = commands.Count(sql => sql.Contains("[Role]", StringComparison.Ordinal)),
                assignmentReads = commands.Count(sql => sql.Contains("[UserRole]", StringComparison.Ordinal)),
                folderReads = commands.Count(sql => sql.Contains("FROM [WorkspaceFolder]", StringComparison.Ordinal)),
                folderIdentityLocks = commands.Count(sql => sql.Contains("UPDATE", StringComparison.Ordinal) && sql.Contains("[EID] =", StringComparison.Ordinal)),
                lockRequestToReceiptMs = lockRequestedMs is { } start ? clock.Elapsed.TotalMilliseconds - start : (double?)null,
                contention = "none measured; interval includes acquisition wait and response" };
        }
        public void OnNext(DiagnosticListener listener) { if (listener.Name == "SqlClientDiagnosticListener") subscriptions.Add(listener.Subscribe(this)); }
        public void OnNext(KeyValuePair<string, object?> value)
        {
            if (!active || value.Value?.GetType().GetProperty("Command")?.GetValue(value.Value) is not DbCommand command || command.Connection?.Database != database) return;
            if (value.Key.EndsWith("WriteCommandAfter", StringComparison.Ordinal)) commands.Enqueue(command.CommandText);
            if (lockRequestedMs is null && value.Key.EndsWith("WriteCommandBefore", StringComparison.Ordinal)
                && command.CommandText.Contains("sp_getapplock", StringComparison.OrdinalIgnoreCase)) lockRequestedMs = clock.Elapsed.TotalMilliseconds;
        }
        public void OnCompleted() { }
        public void OnError(Exception error) { }
        public void Dispose() { active = false; foreach (var subscription in subscriptions) subscription.Dispose(); all.Dispose(); }
    }
}
