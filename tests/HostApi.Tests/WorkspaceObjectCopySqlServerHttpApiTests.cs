using System.Net;
using System.Text.Json;
using System.Net.Http.Json;
using System.Data.Common;
using ICP.Platform.Modules.Authorization.DTOs;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed partial class WorkspaceClipboardSqlServerHttpApiTests
{
    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "副本提交期间固定真实Part源；正常编辑在源读取之后不能穿越未提交快照，短事务结束即继续")]
    public async Task ObjectCopy_HoldsOneSourceSnapshotUntilItsShortCommitAndThenReleasesOrdinaryEdit()
    {
        var observer = new ObjectCopyWriteFault();
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(services => services.AddDbContext<EfPlatformDbContext>((_, options) => options.AddInterceptors(observer)), includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync(); var root = await Root(fixture, admin);
        var source = await Folder(fixture, admin, root, "固定快照来源"); var target = await Folder(fixture, admin, root, "固定快照目标");
        await Command(fixture, admin, "workspace.folder.createPart", "/api/workspace/content/parts", new { position = source, values = new { ECODE = "SNAPSHOT-SOURCE", ENAME = "确认的源", Description = "确认的说明" } });
        var link = Assert.Single(await Rows(fixture, "WorkspaceFolderContent"), row => Equals(row["ChildModelKey"], "Part"));
        var prepared = await PartCopyCommand(fixture, admin, PartCopyDataPhase.Prepare, new { mode = "copy", sourcePosition = source, linkIds = new[] { link["LID"] }, targetPosition = target });
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Fault.AfterRegister = async (change, token) => { if (change.EventType == "workspace.object.copied") { reached.TrySetResult(); await resume.Task.WaitAsync(token); } };
        var pending = PartCopySend(fixture, admin, PartCopyDataPhase.Submit, new { operationId = prepared.GetProperty("operationId").GetString(), values = new { ECODE = "SNAPSHOT-COPY", ENAME = "确认的源", Description = "确认的说明" } });
        Task<HttpResponseMessage>? edit = null;
        try
        {
            await reached.Task.WaitAsync(TimeSpan.FromSeconds(20));
            observer.WatchEditGraph = true;
            var point = RowPoint(fixture, "edit");
            edit = FeatureHttpTestRequests.SendAsync(admin, point, HttpMethod.Put, "/api/workspace/content/" + link["LID"], new { position = source, values = new { ENAME = "之后的源", Description = "之后的说明" } });
            await observer.EditGraphReached.Task.WaitAsync(TimeSpan.FromSeconds(20));
            await Task.WhenAny(edit, Task.Delay(TimeSpan.FromMilliseconds(500)));
            Assert.False(edit.IsCompleted, "正常源编辑不能在副本事务固定快照后、提交前穿越。");
            resume.TrySetResult();
            using var response = await pending; Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var updated = await edit.WaitAsync(TimeSpan.FromSeconds(10)); Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        }
        finally { resume.TrySetResult(); (await pending).Dispose(); if (edit is not null) (await edit).Dispose(); fixture.Fault.AfterRegister = null; }
        var parts = await Rows(fixture, "Part");
        Assert.Equal("确认的说明", Assert.Single(parts, row => Equals(row["ECODE"], "SNAPSHOT-COPY"))["Description"]);
        Assert.Equal("之后的说明", Assert.Single(parts, row => Equals(row["ECODE"], "SNAPSHOT-SOURCE"))["Description"]);
    }
    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "普通Part创建与真副本在数据库唯一索引处竞争时，副本冲突定位编码且整图回滚")]
    public async Task ObjectCopy_ConcurrentNormalCreateKeepsTheCodeErrorAndRollsBackItsLink()
    {
        var barrier = new ObjectCopyWriteFault();
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(services => services.AddDbContext<EfPlatformDbContext>((_, options) => options.AddInterceptors(barrier)), includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync(); var root = await Root(fixture, admin);
        var source = await Folder(fixture, admin, root, "编码并发来源"); var target = await Folder(fixture, admin, root, "编码并发目标");
        await Command(fixture, admin, "workspace.folder.createPart", "/api/workspace/content/parts", new { position = source, values = new { ECODE = "NORMAL-RACE-SOURCE", ENAME = "来源" } });
        var lid = (string)Assert.Single(await Rows(fixture, "WorkspaceFolderContent"), row => Equals(row["ChildModelKey"], "Part"))["LID"]!;
        var preparation = await PartCopyCommand(fixture, admin, PartCopyDataPhase.Prepare, new { mode = "copy", sourcePosition = source, linkIds = new[] { lid }, targetPosition = target });
        barrier.PauseTable = "Part"; barrier.Pause = true;
        var pending = PartCopySend(fixture, admin, PartCopyDataPhase.Submit, new { operationId = preparation.GetProperty("operationId").GetString(), values = new { ECODE = "NORMAL-RACE-CODE", ENAME = "副本" } });
        try
        {
            await barrier.Reached.Task.WaitAsync(TimeSpan.FromSeconds(20));
            // 暂停仅消费第一条写入，正常创建仍走真实HTTP，不能用直接SQL绕过所属规则。
            barrier.Pause = false;
            var create = Assert.Single(fixture.FeatureCatalog, point => point.ModuleId == "Part" && point.BehaviorKey == "part.part.create" && point.ActionScope == "list.pageActions");
            using var winner = await FeatureHttpTestRequests.SendAsync(admin, create, HttpMethod.Post, "/api/part/parts", new { ECODE = "NORMAL-RACE-CODE", ENAME = "正常创建赢家" });
            Assert.Equal(HttpStatusCode.Created, winner.StatusCode);
            barrier.Release.TrySetResult();
            using var loser = await pending; Assert.Equal(HttpStatusCode.Conflict, loser.StatusCode);
            var problem = await loser.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Contains(problem.GetProperty("fieldErrors").EnumerateArray(), error => error.GetProperty("field").GetString() == "ECODE");
        }
        finally { barrier.Release.TrySetResult(); (await pending).Dispose(); }
        Assert.Equal(2, (await Rows(fixture, "Part")).Length);
        Assert.Single(await Rows(fixture, "WorkspaceFolderContent"), row => Equals(row["ChildModelKey"], "Part"));
        Assert.DoesNotContain(await Rows(fixture, "BusinessChange"), row => Equals(row["EventType"], "workspace.object.copied"));
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "私人来源完整位置强制隔离，跨空间副本采用新对象规则，撤销源read和移除目标位置不泄露或换路")]
    public async Task ObjectCopy_PersonalSourceCannotBeBorrowedAndRemovedTargetInvalidatesTheOperation()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync(); var root = await Root(fixture, admin);
        var target = await Folder(fixture, admin, root, "跨空间目标");
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "PrivateObjectCopy", "私人副本", "private-object-copy", "私人副本账号",
            Point(fixture, "workspace.spaces.access"), Point(fixture, "workspace.folder.create"), Point(fixture, "workspace.folder.createPart"), Point(fixture, "workspace.folder.pasteObjectCopy"));
        using var alice = actor.Client;
        var bobActor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "ForeignObjectCopy", "外部副本", "foreign-object-copy", "外部账号",
            Point(fixture, "workspace.spaces.access"), Point(fixture, "workspace.folder.pasteObjectCopy"));
        using var bob = bobActor.Client;
        var personal = await Command(fixture, alice, "workspace.spaces.access", "/api/workspace/spaces/personal/ensure");
        var personalRoot = new ICP.Platform.Modules.Workspace.WorkspacePosition(personal.GetProperty("spaceId").GetString()!, personal.GetProperty("rootFolderId").GetString()!, []);
        var source = await Folder(fixture, alice, personalRoot, "不可泄露私人来源");
        await Command(fixture, alice, "workspace.folder.createPart", "/api/workspace/content/parts", new { position = source, values = new { ECODE = "PRIVATE-PART-SOURCE", ENAME = "不可泄露零组件" } });
        var link = Assert.Single(await Rows(fixture, "WorkspaceFolderContent"), row => Equals(row["ChildModelKey"], "Part"));
        var input = new { mode = "copy", sourcePosition = source, linkIds = new[] { link["LID"] }, targetPosition = target };
        using (var foreign = await PartCopySend(fixture, bob, PartCopyDataPhase.Prepare, input))
        { Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode); Assert.DoesNotContain("不可泄露", await foreign.Content.ReadAsStringAsync()); }
        await Rule(fixture, admin, "PrivateSourceHidden", "part.part", (string)link["CEID"]!, "read", "deny");
        using (var unreadable = await PartCopySend(fixture, alice, PartCopyDataPhase.Prepare, input))
        { Assert.Equal(HttpStatusCode.NotFound, unreadable.StatusCode); Assert.DoesNotContain("不可泄露", await unreadable.Content.ReadAsStringAsync()); }
        await Rule(fixture, admin, "PrivateSourceHidden", "part.part", (string)link["CEID"]!, "read", "allow");
        var prepared = await PartCopyCommand(fixture, alice, PartCopyDataPhase.Prepare, input);
        await PartCopyCommand(fixture, alice, PartCopyDataPhase.Submit, new { operationId = prepared.GetProperty("operationId").GetString(), values = new { ECODE = "PUBLIC-NEW-PART", ENAME = "独立公开副本" } });
        var copiedId = Assert.Single(await Rows(fixture, "Part"), row => Equals(row["ECODE"], "PUBLIC-NEW-PART"))["EID"];
        Assert.Equal(false, Assert.Single(await Rows(fixture, "WorkspaceFolderContent"), row => Equals(row["CEID"], copiedId))["IsScopeNeutral"]);
        prepared = await PartCopyCommand(fixture, alice, PartCopyDataPhase.Prepare, input);
        await Command(fixture, admin, "workspace.folder.removeSelected", "/api/workspace/content/references/remove", new { position = root, linkIds = new[] { target.LinkIds[^1] } });
        using var removed = await PartCopySend(fixture, alice, PartCopyDataPhase.Submit, new { operationId = prepared.GetProperty("operationId").GetString(), values = new { ECODE = "NO-TARGET", ENAME = "不能换目标" } });
        Assert.Equal(HttpStatusCode.NotFound, removed.StatusCode);
        Assert.Equal(2, (await Rows(fixture, "Part")).Length);
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "来源复制权在创建和审计登记期间撤销时，最终裁决回滚新对象、Link和成功记录")]
    public async Task ObjectCopy_RevocationAfterOwnerCreationStillRejectsBeforeFinalCommit()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync(); var root = await Root(fixture, admin);
        var source = await Folder(fixture, admin, root, "最后裁决来源"); var target = await Folder(fixture, admin, root, "最后裁决目标");
        await Command(fixture, admin, "workspace.folder.createPart", "/api/workspace/content/parts", new { position = source, values = new { ECODE = "LAST-SOURCE", ENAME = "最后来源" } });
        var link = Assert.Single(await Rows(fixture, "WorkspaceFolderContent"), row => Equals(row["ChildModelKey"], "Part"));
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "LateCopyActor", "最终资格", "late-copy", "最终资格账号",
            Point(fixture, "workspace.spaces.access"), Point(fixture, "workspace.folder.pasteObjectCopy"));
        using var ordinary = actor.Client;
        var parentId = FolderId(target, await Rows(fixture, "WorkspaceFolderContent"));
        foreach (var (resource, id, action, code) in new[] {
            ("part.part", (string)link["CEID"]!, "partObjectCopy", "Workspace.ObjectCopyDenied"),
            ("workspace.folder", parentId, "edit", "Workspace.ParentWriteDenied") })
        {
            var prepared = await PartCopyCommand(fixture, ordinary, PartCopyDataPhase.Prepare,
                new { mode = "copy", sourcePosition = source, linkIds = new[] { link["LID"] }, targetPosition = target });
            var operationId = prepared.GetProperty("operationId").GetString();
            var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            fixture.Fault.AfterRegister = async (change, token) => { if (change.EventType == "workspace.object.copied") { reached.TrySetResult(); await resume.Task.WaitAsync(token); } };
            var pending = PartCopySend(fixture, ordinary, PartCopyDataPhase.Submit, new { operationId, values = new { ECODE = "LATE-DENIED", ENAME = "不得提交" } });
            try
            {
                await reached.Task.WaitAsync(TimeSpan.FromSeconds(20));
                await Rule(fixture, admin, "LateCopyDenied-" + action, resource, id, action, "deny");
                resume.TrySetResult();
                using var response = await pending;
                await Problem(response, HttpStatusCode.Forbidden, code);
            }
            finally { resume.TrySetResult(); (await pending).Dispose(); fixture.Fault.AfterRegister = null; }
            Assert.Single(await Rows(fixture, "Part"));
            Assert.Single(await Rows(fixture, "WorkspaceFolderContent"), row => Equals(row["ChildModelKey"], "Part"));
            Assert.DoesNotContain(await Rows(fixture, "BusinessChange"), row => Equals(row["EventType"], "workspace.object.copied"));
            await Rule(fixture, admin, "LateCopyDenied-" + action, resource, id, action, "allow");
        }
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "真实提交前取消回滚；提交后丢失回执保留对象/Link/审计，重启后受控结果核对不重放")]
    public async Task ObjectCopy_PreCommitCancellationAndLostCommitReceiptRecoverFromDurableState()
    {
        var beforeCommit = new PasteCommitControl(); var afterCommit = new ObjectCopyCommitFault();
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(services => services.AddDbContext<EfPlatformDbContext>((_, options) => options.AddInterceptors(beforeCommit, afterCommit)), includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync(); var root = await Root(fixture, admin);
        var source = await Folder(fixture, admin, root, "中断来源"); var target = await Folder(fixture, admin, root, "中断目标");
        await Command(fixture, admin, "workspace.folder.createPart", "/api/workspace/content/parts", new { position = source, values = new { ECODE = "DURABLE-SOURCE", ENAME = "持久源" } });
        var lid = (string)Assert.Single(await Rows(fixture, "WorkspaceFolderContent"), row => Equals(row["ChildModelKey"], "Part"))["LID"]!;
        var preparation = await PartCopyCommand(fixture, admin, PartCopyDataPhase.Prepare, new { mode = "copy", sourcePosition = source, linkIds = new[] { lid }, targetPosition = target });
        var operationId = preparation.GetProperty("operationId").GetString();
        beforeCommit.Pause = true; using var cancellation = new CancellationTokenSource();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/workspace/content/objects/copy") { Content = JsonContent.Create(new { operationId, values = new { ECODE = "DURABLE-COPY", ENAME = "中断副本" } }, options: new JsonSerializerOptions()) };
        var submitPoint = Point(fixture, "workspace.folder.pasteObjectCopy");
        request.Headers.Add("X-NewP-Feature", submitPoint.Position);
        request.Headers.Add("X-NewP-Data-Declaration", new FeatureHttpTestRequests(fixture).Declaration("workspace.objectCopy", submitPoint).Id);
        var pending = admin.SendAsync(request, cancellation.Token);
        try
        {
            await beforeCommit.Reached.Task.WaitAsync(TimeSpan.FromSeconds(20)); cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await pending);
            await beforeCommit.RolledBack.Task.WaitAsync(TimeSpan.FromSeconds(20));
        }
        finally { cancellation.Cancel(); beforeCommit.Resume.TrySetResult(); beforeCommit.Pause = false; try { (await pending).Dispose(); } catch (OperationCanceledException) { } }
        Assert.Single(await Rows(fixture, "Part"));
        var ready = await Command(fixture, admin, "workspace.folder.resolveObjectCopy", "/api/workspace/content/objects/copy/result", new { operationId });
        Assert.Equal("ready", ready.GetProperty("state").GetString());
        afterCommit.Enabled = true;
        using (var unknown = await PartCopySend(fixture, admin, PartCopyDataPhase.Submit, new { operationId, values = new { ECODE = "DURABLE-COPY", ENAME = "已提交副本" } }))
            Assert.False(unknown.IsSuccessStatusCode);
        Assert.True(afterCommit.Triggered);
        Assert.Equal(2, (await Rows(fixture, "Part")).Length);
        Assert.Single(await Rows(fixture, "BusinessChange"), row => Equals(row["EventType"], "workspace.object.copied"));
        await fixture.RestartWithActionsAsync(new Dictionary<string, bool>());
        using var restarted = fixture.CreateClient();
        await BusinessAuditHostFixture.RefreshAntiforgeryAsync(restarted);
        using var login = await restarted.PostAsJsonAsync("/api/auth/login", new { ECODE = "admin", Password = "Audit-test-secure-938!" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        await BusinessAuditHostFixture.RefreshAntiforgeryAsync(restarted);
        var recovered = await Command(fixture, restarted, "workspace.folder.resolveObjectCopy", "/api/workspace/content/objects/copy/result", new { operationId });
        Assert.True(recovered.GetProperty("succeeded").GetBoolean()); Assert.Equal("committed", recovered.GetProperty("state").GetString());
        Assert.Equal(2, (await Rows(fixture, "Part")).Length);
    }

    private sealed class ObjectCopyCommitFault : DbTransactionInterceptor
    {
        public bool Enabled { get; set; }
        public bool Triggered { get; private set; }
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken token = default)
        {
            if (Enabled) { Enabled = false; Triggered = true; throw new InvalidOperationException("OBJECT_COPY_COMMIT_RECEIPT_LOST"); }
            return Task.CompletedTask;
        }
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "Part、Link、审计写入故障均回滚对象图，持久操作仍可核对为未提交")]
    public async Task ObjectCopy_RollsBackEachWriteFailureWithoutLosingRecoveryState()
    {
        var fault = new ObjectCopyWriteFault();
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(services => services.AddDbContext<EfPlatformDbContext>((_, options) => options.AddInterceptors(fault)), includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync(); var root = await Root(fixture, admin);
        var source = await Folder(fixture, admin, root, "故障来源"); var target = await Folder(fixture, admin, root, "故障目标");
        await Command(fixture, admin, "workspace.folder.createPart", "/api/workspace/content/parts", new { position = source, values = new { ECODE = "FAULT-SOURCE", ENAME = "故障源" } });
        var lid = (string)Assert.Single(await Rows(fixture, "WorkspaceFolderContent"), row => Equals(row["ChildModelKey"], "Part"))["LID"]!;
        var parts = Snapshot(await Rows(fixture, "Part")); var links = Snapshot(await Rows(fixture, "WorkspaceFolderContent")); var audit = Snapshot(await Rows(fixture, "BusinessChange"));
        foreach (var table in new[] { "Part", "WorkspaceFolderContent", "BusinessChange" })
        {
            var prepared = await PartCopyCommand(fixture, admin, PartCopyDataPhase.Prepare, new { mode = "copy", sourcePosition = source, linkIds = new[] { lid }, targetPosition = target });
            var operationId = prepared.GetProperty("operationId").GetString(); fault.Table = table;
            using (var failed = await PartCopySend(fixture, admin, PartCopyDataPhase.Submit, new { operationId, values = new { ECODE = "FAULT-" + table, ENAME = "不会残留" } })) Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
            fault.Table = null;
            Assert.True(fault.Triggered); fault.Triggered = false;
            Assert.Equal(parts, Snapshot(await Rows(fixture, "Part"))); Assert.Equal(links, Snapshot(await Rows(fixture, "WorkspaceFolderContent"))); Assert.Equal(audit, Snapshot(await Rows(fixture, "BusinessChange")));
            var result = await Command(fixture, admin, "workspace.folder.resolveObjectCopy", "/api/workspace/content/objects/copy/result", new { operationId });
            Assert.Equal("ready", result.GetProperty("state").GetString()); Assert.False(result.GetProperty("succeeded").GetBoolean());
        }
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "提交与取消竞争报告真实已提交事实，并发同码只产生一个对象，记录单Part和并发关键成本")]
    public async Task ObjectCopy_CancelRaceAndConcurrentCodeHaveDurableOutcomesAndMeasuredCosts()
    {
        var barrier = new ObjectCopyWriteFault(); var costs = new ObjectCopyCost(); var connections = new ObjectCopyCostConnections();
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(services => services.AddDbContext<EfPlatformDbContext>((_, options) => options.AddInterceptors(barrier, costs, connections)), includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync(); var root = await Root(fixture, admin);
        var source = await Folder(fixture, admin, root, "竞争来源"); var target = await Folder(fixture, admin, root, "竞争目标");
        await Command(fixture, admin, "workspace.folder.createPart", "/api/workspace/content/parts", new { position = source, values = new { ECODE = "RACE-SOURCE", ENAME = "并发源" } });
        var lid = (string)Assert.Single(await Rows(fixture, "WorkspaceFolderContent"), row => Equals(row["ChildModelKey"], "Part"))["LID"]!;
        // 成本前后使用同一普通主体；默认完整回归仍沿原管理员、竞争与取消流程。
        var measurement = Environment.GetEnvironmentVariable("NEWP_PART_OBJECT_COPY_COST_STAGE") is not null;
        using var ordinary = measurement ? (await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "PartCopyCostActor", "Part复制成本",
            "part-copy-cost", "Part复制成本账号", Point(fixture, "workspace.spaces.access"), Point(fixture, "workspace.folder.pasteObjectCopy"),
            Point(fixture, "workspace.folder.resolveObjectCopy"))).Client : null;
        var copyClient = ordinary ?? admin;
        var input = new { mode = "copy", sourcePosition = source, linkIds = new[] { lid }, targetPosition = target };
        var prepared = await PartCopyCommand(fixture, copyClient, PartCopyDataPhase.Prepare, input);
        var operationId = prepared.GetProperty("operationId").GetString();
        barrier.Pause = true;
        var pending = PartCopySend(fixture, copyClient, PartCopyDataPhase.Submit, new { operationId, values = new { ECODE = "RACE-WINNER", ENAME = "已提交副本" } });
        try
        {
            await barrier.Reached.Task.WaitAsync(TimeSpan.FromSeconds(20));
            var cancelling = Send(fixture, copyClient, "workspace.folder.pasteObjectCopy", "objects/copy/cancel", new { operationId });
            barrier.Release.TrySetResult();
            using (var result = await pending) Assert.Equal(HttpStatusCode.OK, result.StatusCode);
            using (var cancelled = await cancelling)
            { Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode); Assert.Equal("committed", (await cancelled.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("state").GetString()); }
        }
        finally { barrier.Release.TrySetResult(); (await pending).Dispose(); }
        barrier.Pause = false;
        var single = await PartCopyCommand(fixture, copyClient, PartCopyDataPhase.Prepare, input);
        costs.Begin(); connections.Begin(); var singleAllocated = GC.GetTotalAllocatedBytes(); var singleWatch = System.Diagnostics.Stopwatch.StartNew();
        await PartCopyCommand(fixture, copyClient, PartCopyDataPhase.Submit, new { operationId = single.GetProperty("operationId").GetString(), values = new { ECODE = "SINGLE-COST", ENAME = "单对象测量" } });
        singleWatch.Stop(); var singleCost = costs.End(); connections.End();
        var singleReport = new { scenario = "PartObjectCopySingle", elapsedMilliseconds = singleWatch.Elapsed.TotalMilliseconds,
            sqlCommands = singleCost.Commands, graphLocks = singleCost.GraphLocks, graphLockWaitMilliseconds = singleCost.LockWaitMilliseconds,
            graphLockHoldUpperBoundMilliseconds = singleWatch.Elapsed.TotalMilliseconds, allocatedBytes = GC.GetTotalAllocatedBytes() - singleAllocated, temporaryFileBytes = 0 };
        output.WriteLine(JsonSerializer.Serialize(singleReport));
        await SavePartCopyCostAsync(fixture, costs, connections, "single", singleReport);
        var ids = new List<string>();
        for (var index = 0; index < 4; index++) ids.Add((await PartCopyCommand(fixture, copyClient, PartCopyDataPhase.Prepare, input)).GetProperty("operationId").GetString()!);
        costs.Begin(); connections.Begin(); var allocated = GC.GetTotalAllocatedBytes();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var replies = await Task.WhenAll(ids.Select(id => PartCopySend(fixture, copyClient, PartCopyDataPhase.Submit, new { operationId = id, values = new { ECODE = "CONCURRENT-CODE", ENAME = "并发确认" } })));
        watch.Stop();
        try { Assert.Single(replies, reply => reply.StatusCode == HttpStatusCode.OK); Assert.Equal(3, replies.Count(reply => reply.StatusCode == HttpStatusCode.Conflict)); }
        finally { foreach (var reply in replies) reply.Dispose(); }
        var after = costs.End(); connections.End();
        var concurrentReport = new { scenario = "PartObjectCopy", environment = "net10 Release TestServer / real SQL Server", concurrency = 4,
            elapsedMilliseconds = watch.Elapsed.TotalMilliseconds, allocatedBytes = GC.GetTotalAllocatedBytes() - allocated, managedHeapBytes = GC.GetTotalMemory(false),
            sqlCommands = after.Commands, graphLocks = after.GraphLocks, graphLockWaitMilliseconds = after.LockWaitMilliseconds,
            graphLockHoldUpperBoundMilliseconds = watch.Elapsed.TotalMilliseconds, success = 1, conflict = 3, temporaryFileBytes = 0 };
        output.WriteLine(JsonSerializer.Serialize(concurrentReport));
        await SavePartCopyCostAsync(fixture, costs, connections, "concurrency-4", concurrentReport);
        Assert.Equal(4, (await Rows(fixture, "Part")).Length);
        var delete = Point(fixture, "workspace.folder.deleteObjects");
        using (var cleanup = await FeatureHttpTestRequests.SendAsync(admin, delete, HttpMethod.Post, "/api/workspace/content/delete",
            new { position = source, linkIds = new[] { lid } }, new FeatureHttpTestRequests(fixture).Declaration("workspace.content.delete", delete)))
            Assert.Equal(HttpStatusCode.OK, cleanup.StatusCode);
        Assert.Equal(3, (await Rows(fixture, "Part")).Length);
        Assert.Equal(3, (await Rows(fixture, "WorkspaceFolderContent")).Count(row => Equals(row["ChildModelKey"], "Part")));
    }

    private sealed class ObjectCopyCost : DbCommandInterceptor
    {
        private int active, commands, graphLocks; private long lockWaitTicks;
        internal System.Collections.Concurrent.ConcurrentQueue<string> Sql { get; } = new();
        internal System.Collections.Concurrent.ConcurrentQueue<ExampleExecutionSqlPlanTests.Sample> Reads { get; } = new();
        public void Begin() { commands = graphLocks = 0; lockWaitTicks = 0; Sql.Clear(); Reads.Clear(); active = 1; }
        public (int Commands, int GraphLocks, double LockWaitMilliseconds) End()
        { active = 0; return (commands, graphLocks, TimeSpan.FromTicks(lockWaitTicks).TotalMilliseconds); }
        // 同原Before计数点快照SQL/参数；不订阅全局事件，也不引入终结配对或取消协议。
        private void Record(DbCommand command)
        {
            if (active != 1) return;
            Interlocked.Increment(ref commands);
            if (Environment.GetEnvironmentVariable("NEWP_PART_OBJECT_COPY_COST_STAGE") is null) return;
            Sql.Enqueue(command.CommandText);
            if (command.CommandText.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)
                && new[] { "[DataScope", "[Role]", "[UserRole]" }.Any(table => command.CommandText.Contains(table, StringComparison.Ordinal))
                && !new[] { "[Part]", "[Workspace", "[Authentication", "[Identity" }.Any(table => command.CommandText.Contains(table, StringComparison.Ordinal)))
                Reads.Enqueue(new(command.CommandText, command.Parameters.Cast<Microsoft.Data.SqlClient.SqlParameter>()
                    .Select(parameter => (Microsoft.Data.SqlClient.SqlParameter)((ICloneable)parameter).Clone()).ToArray()));
        }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData data, InterceptionResult<DbDataReader> result, CancellationToken token = default)
        { Record(command); return ValueTask.FromResult(result); }
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData data, InterceptionResult<int> result, CancellationToken token = default)
        { Record(command); if (active == 1 && command.CommandText.Contains("Workspace.FolderGraph", StringComparison.Ordinal)) Interlocked.Increment(ref graphLocks); return ValueTask.FromResult(result); }
        public override ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData data, int result, CancellationToken token = default)
        { if (active == 1 && command.CommandText.Contains("Workspace.FolderGraph", StringComparison.Ordinal)) Interlocked.Add(ref lockWaitTicks, data.Duration.Ticks); return ValueTask.FromResult(result); }
        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command, CommandEventData data, InterceptionResult<object> result, CancellationToken token = default)
        { Record(command); return ValueTask.FromResult(result); }
    }
    private sealed class ObjectCopyWriteFault : DbCommandInterceptor
    {
        public bool WatchEditGraph { get; set; }
        public TaskCompletionSource EditGraphReached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData data, InterceptionResult<int> result, CancellationToken token = default)
        { if (WatchEditGraph && command.CommandText.Contains("Workspace.FolderGraph", StringComparison.Ordinal)) EditGraphReached.TrySetResult(); return ValueTask.FromResult(result); }
        public string? Table { get; set; }
        public bool Triggered { get; set; }
        public bool Pause { get; set; }
        public string PauseTable { get; set; } = "WorkspaceFolderContent";
        public TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken token = default)
        {
            if (Table is not null && command.CommandText.Contains("INSERT INTO", StringComparison.Ordinal)
                && command.CommandText.Contains("[" + Table + "]", StringComparison.Ordinal)) { Triggered = true; throw new InvalidOperationException("OBJECT_COPY_WRITE_FAULT_" + Table); }
            if (Pause && command.CommandText.Contains("INSERT INTO", StringComparison.Ordinal) && command.CommandText.Contains("[" + PauseTable + "]", StringComparison.Ordinal))
            { Reached.TrySetResult(); await Release.Task.WaitAsync(token); }
            return result;
        }
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "单Part边界拒绝多选、混合、cut、根目标、伪造字段，并复核精确LID，不按EID替换来源")]
    public async Task ObjectCopy_RejectsSelectionContextForgeryAndRemovedSourceWithoutChangingBusinessState()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync(); var root = await Root(fixture, admin);
        var source = await Folder(fixture, admin, root, "边界来源"); var target = await Folder(fixture, admin, root, "边界目标");
        await Command(fixture, admin, "workspace.folder.createPart", "/api/workspace/content/parts", new { position = source, values = new { ECODE = "BOUNDARY-PART", ENAME = "来源" } });
        var part = Assert.Single(await Rows(fixture, "Part"));
        var link = Assert.Single(await Rows(fixture, "WorkspaceFolderContent"), row => Equals(row["ChildModelKey"], "Part"));
        var lid = (string)link["LID"]!;
        var prepared = await PartCopyCommand(fixture, admin, PartCopyDataPhase.Prepare, new { mode = "copy", sourcePosition = source, linkIds = new[] { lid }, targetPosition = target });
        foreach (var invalid in new object[] {
            new { mode = "cut", sourcePosition = source, linkIds = new[] { lid }, targetPosition = target },
            new { mode = "copy", sourcePosition = source, linkIds = new[] { lid, lid }, targetPosition = target },
            new { mode = "copy", sourcePosition = source, linkIds = new[] { lid, source.LinkIds[^1] }, targetPosition = target },
            new { mode = "copy", sourcePosition = root, linkIds = new[] { source.LinkIds[^1] }, targetPosition = target },
            new { mode = "copy", sourcePosition = source, linkIds = new[] { lid }, targetPosition = root } })
        {
            using var response = await PartCopySend(fixture, admin, PartCopyDataPhase.Prepare, invalid);
            Assert.InRange((int)response.StatusCode, 400, 499);
        }
        var operationId = prepared.GetProperty("operationId").GetString();
        foreach (var field in new[] { "EID", "CreatedBy", "UpdatedAt", "Owner", "StorageKey" })
        {
            using var forged = await PartCopySend(fixture, admin, PartCopyDataPhase.Submit, new { operationId, values = new Dictionary<string, object?> { ["ECODE"] = "FORGED", ["ENAME"] = "输入", [field] = "forged" } });
            await Problem(forged, HttpStatusCode.BadRequest, "Workspace.ServerFieldForbidden");
        }
        await Command(fixture, admin, "workspace.folder.paste", "/api/workspace/content/paste", new { mode = "copy", sourcePosition = source, linkIds = new[] { lid }, targetPosition = target });
        await Command(fixture, admin, "workspace.folder.removeSelected", "/api/workspace/content/references/remove", new { position = source, linkIds = new[] { lid } });
        using var removed = await PartCopySend(fixture, admin, PartCopyDataPhase.Submit, new { operationId, values = new { ECODE = "REMOVED", ENAME = "不换路径" } });
        await Problem(removed, HttpStatusCode.NotFound, "Workspace.ReferenceUnavailable");
        Assert.Single(await Rows(fixture, "Part"));
        Assert.Equal(part["EID"], Assert.Single(await Rows(fixture, "Part"))["EID"]);
        Assert.DoesNotContain(await Rows(fixture, "BusinessChange"), row => Equals(row["EventType"], "workspace.object.copied"));
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "来源变化拒绝旧确认；重新确认后采用用户编辑值，编码规范/重复定位与取消状态不产生半副本")]
    public async Task ObjectCopy_ReconfirmsChangedSourceRetainsExplicitValuesAndCancelsOnlyUncommittedOperations()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync(); var root = await Root(fixture, admin);
        var source = await Folder(fixture, admin, root, "变化来源"); var target = await Folder(fixture, admin, root, "变化目标");
        await Command(fixture, admin, "workspace.folder.createPart", "/api/workspace/content/parts", new { position = source, values = new { ECODE = "CHANGE-SOURCE", ENAME = "打开时名称", Description = "旧说明" } });
        var lid = (string)Assert.Single(await Rows(fixture, "WorkspaceFolderContent"), row => Equals(row["ChildModelKey"], "Part"))["LID"]!;
        var input = new { mode = "copy", sourcePosition = source, linkIds = new[] { lid }, targetPosition = target };
        var prepared = await PartCopyCommand(fixture, admin, PartCopyDataPhase.Prepare, input);
        var operationId = prepared.GetProperty("operationId").GetString();
        using (var update = await FeatureHttpTestRequests.SendAsync(admin, RowPoint(fixture, "edit"), HttpMethod.Put, "/api/workspace/content/" + lid,
            new { position = source, values = new { ENAME = "提交前名称", Description = "新说明" } }, new FeatureHttpTestRequests(fixture).Declaration("workspace.content.edit", RowPoint(fixture, "edit")))) Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        using (var changed = await PartCopySend(fixture, admin, PartCopyDataPhase.Submit, new { operationId, values = new { ECODE = "CHANGED-COPY", ENAME = "我的编辑", Description = "我的说明" } }))
            await Problem(changed, HttpStatusCode.Conflict, "Workspace.CopySourceChanged");
        Assert.Single(await Rows(fixture, "Part"));
        prepared = await PartCopyCommand(fixture, admin, PartCopyDataPhase.Prepare, input);
        operationId = prepared.GetProperty("operationId").GetString();
        using (var duplicate = await PartCopySend(fixture, admin, PartCopyDataPhase.Submit, new { operationId, values = new { ECODE = "CHANGE-SOURCE", ENAME = "我的编辑" } }))
        { Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode); Assert.Contains("ECODE", await duplicate.Content.ReadAsStringAsync()); }
        using (var unnormalized = await PartCopySend(fixture, admin, PartCopyDataPhase.Submit, new { operationId, values = new { ECODE = " NEW-COPY ", ENAME = "我的编辑" } }))
            await Problem(unnormalized, HttpStatusCode.BadRequest, "StandardCrud.ShortTextNotNormalized");
        await PartCopyCommand(fixture, admin, PartCopyDataPhase.Submit, new { operationId, values = new { ECODE = "NEW-COPY", ENAME = "我的编辑", Description = "我的说明" } });
        var copied = Assert.Single(await Rows(fixture, "Part"), row => Equals(row["ECODE"], "NEW-COPY"));
        Assert.Equal("我的编辑", copied["ENAME"]); Assert.Equal("我的说明", copied["Description"]);
        var committedCancel = await Command(fixture, admin, "workspace.folder.pasteObjectCopy", "/api/workspace/content/objects/copy/cancel", new { operationId });
        Assert.Equal("committed", committedCancel.GetProperty("state").GetString());
        var fresh = await PartCopyCommand(fixture, admin, PartCopyDataPhase.Prepare, input);
        var cancelledId = fresh.GetProperty("operationId").GetString();
        var cancelled = await Command(fixture, admin, "workspace.folder.pasteObjectCopy", "/api/workspace/content/objects/copy/cancel", new { operationId = cancelledId });
        Assert.Equal("cancelled", cancelled.GetProperty("state").GetString());
        using var afterCancel = await PartCopySend(fixture, admin, PartCopyDataPhase.Submit, new { operationId = cancelledId, values = new { ECODE = "CANCELLED", ENAME = "不会创建" } });
        await Problem(afterCancel, HttpStatusCode.Conflict, "Workspace.CopyCancelled");
        Assert.Equal(2, (await Rows(fixture, "Part")).Length);
        var copyLink = Assert.Single(await Rows(fixture, "WorkspaceFolderContent"), row => Equals(row["CEID"], copied["EID"]));
        using (var editCopy = await FeatureHttpTestRequests.SendAsync(admin, RowPoint(fixture, "edit"), HttpMethod.Put, "/api/workspace/content/" + copyLink["LID"], new { position = target, values = new { ENAME = "只编辑副本" } }, new FeatureHttpTestRequests(fixture).Declaration("workspace.content.edit", RowPoint(fixture, "edit")))) Assert.Equal(HttpStatusCode.OK, editCopy.StatusCode);
        Assert.Equal("提交前名称", Assert.Single(await Rows(fixture, "Part"), row => Equals(row["ECODE"], "CHANGE-SOURCE"))["ENAME"]);
        var delete = Point(fixture, "workspace.folder.deleteObjects");
        using (var deleted = await FeatureHttpTestRequests.SendAsync(admin, delete, HttpMethod.Post, "/api/workspace/content/delete",
            new { position = target, linkIds = new[] { copyLink["LID"] } }, new FeatureHttpTestRequests(fixture).Declaration("workspace.content.delete", delete)))
            Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
        Assert.Equal("CHANGE-SOURCE", Assert.Single(await Rows(fixture, "Part"))["ECODE"]);
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "有限权限隔离引用copy和真副本；不要求源edit/delete，提交重验撤权/目标edit/create，操作归属和安全成功")]
    public async Task ObjectCopy_UsesIndependentRightsRevalidatesAndHidesUnReadableResults()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync(); var root = await Root(fixture, admin);
        var source = await Folder(fixture, admin, root, "权限来源"); var target = await Folder(fixture, admin, root, "权限目标");
        await Command(fixture, admin, "workspace.folder.createPart", "/api/workspace/content/parts", new { position = source, values = new { ECODE = "RIGHT-SOURCE", ENAME = "源名称不可泄露" } });
        var link = Assert.Single(await Rows(fixture, "WorkspaceFolderContent"), row => Equals(row["ChildModelKey"], "Part"));
        var lid = (string)link["LID"]!; var sourceId = (string)link["CEID"]!;
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "ObjectCopyActor", "副本权限", "object-copy", "副本账号",
            Point(fixture, "workspace.spaces.access"), Point(fixture, "workspace.folder.copy"), Point(fixture, "workspace.folder.pasteObjectCopy"), Point(fixture, "workspace.folder.paste"));
        using var ordinary = actor.Client;
        var input = new { mode = "copy", sourcePosition = source, linkIds = new[] { lid }, targetPosition = target };
        await Rule(fixture, admin, "NoTrueCopy", "part.part", sourceId, "partObjectCopy", "deny");
        await Command(fixture, ordinary, "workspace.folder.copy", "/api/workspace/content/clipboard/validate", new { mode = "copy", sourcePosition = source, linkIds = new[] { lid } });
        using (var denied = await PartCopySend(fixture, ordinary, PartCopyDataPhase.Prepare, input)) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        await Rule(fixture, admin, "NoTrueCopy", "part.part", sourceId, "partObjectCopy", "allow");
        await Rule(fixture, admin, "NoReferenceCopy", "part.part", sourceId, "partCopy", "deny");
        await Rule(fixture, admin, "NoSourceEdit", "part.part", sourceId, "edit", "deny");
        await Rule(fixture, admin, "NoSourceDelete", "part.part", sourceId, "delete", "deny");
        await Rule(fixture, admin, "NoSourceParentEdit", "workspace.folder", FolderId(source, await Rows(fixture, "WorkspaceFolderContent")), "edit", "deny");
        await Command(fixture, ordinary, "workspace.folder.copy", "/api/workspace/content/clipboard/validate", new { mode = "copy", sourcePosition = source, linkIds = new[] { lid } });
        using (var oldPaste = await Send(fixture, ordinary, "workspace.folder.paste", "paste", input)) await Problem(oldPaste, HttpStatusCode.Forbidden, "Workspace.ObjectCopyDenied");
        var preparation = await PartCopyCommand(fixture, ordinary, PartCopyDataPhase.Prepare, input);
        var operationId = preparation.GetProperty("operationId").GetString();
        var submit = new { operationId, values = new { ECODE = "HIDDEN-COPY", ENAME = "新对象" } };
        await Rule(fixture, admin, "NoTrueCopy", "part.part", sourceId, "partObjectCopy", "deny");
        using (var revoked = await PartCopySend(fixture, ordinary, PartCopyDataPhase.Submit, submit)) Assert.Equal(HttpStatusCode.Forbidden, revoked.StatusCode);
        await Rule(fixture, admin, "NoTrueCopy", "part.part", sourceId, "partObjectCopy", "allow");
        await Rule(fixture, admin, "NoTargetEdit", "workspace.folder", FolderId(target, await Rows(fixture, "WorkspaceFolderContent")), "edit", "deny");
        using (var parentDenied = await PartCopySend(fixture, ordinary, PartCopyDataPhase.Submit, submit)) await Problem(parentDenied, HttpStatusCode.Forbidden, "Workspace.ParentWriteDenied");
        await Rule(fixture, admin, "NoTargetEdit", "workspace.folder", FolderId(target, await Rows(fixture, "WorkspaceFolderContent")), "edit", "allow");
        foreach (var action in new[] { "create", "read" })
        {
            var rule = new DataScopeRuleDto("CopyCandidate", "CopyCandidate", null, "CopyCandidate", true, "part.part",
                new("attribute", "ECODE", "equal", JsonSerializer.SerializeToElement("HIDDEN-COPY")),
                [new("CopyCandidateSubject", "all", null, 0, new Dictionary<string, string> { [action] = "deny" })], []);
            using var write = await FeatureHttpTestRequests.SendAsync(admin, Assert.Single(fixture.FeatureCatalog, point => point.BehaviorKey == "authorization.dataScope.save"), HttpMethod.Put, "/api/authorization/data-scope/rules/CopyCandidate", rule);
            Assert.Equal(HttpStatusCode.NoContent, write.StatusCode);
            if (action == "create")
            { using var createDenied = await PartCopySend(fixture, ordinary, PartCopyDataPhase.Submit, submit); Assert.Equal(HttpStatusCode.Forbidden, createDenied.StatusCode); Assert.Single(await Rows(fixture, "Part")); }
        }
        var result = await PartCopyCommand(fixture, ordinary, PartCopyDataPhase.Submit, submit);
        Assert.True(result.GetProperty("succeeded").GetBoolean()); Assert.False(result.TryGetProperty("id", out _));
        using var otherActorResult = await Send(fixture, admin, "workspace.folder.resolveObjectCopy", "objects/copy/result", new { operationId });
        await Problem(otherActorResult, HttpStatusCode.NotFound, "Workspace.CopyOperationUnavailable");
        Assert.Equal(2, (await Rows(fixture, "Part")).Length);
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "真实Part副本由正式owner创建，独立身份、目标引用和审计共同提交，持久结果核对不重复创建")]
    public async Task ObjectCopy_PartCreatesIndependentObjectAndRecoversItsCommittedReceipt()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        var root = await Root(fixture, admin);
        var source = await Folder(fixture, admin, root, "对象副本来源");
        var target = await Folder(fixture, admin, root, "对象副本目标");
        await Command(fixture, admin, "workspace.folder.createPart", "/api/workspace/content/parts", new {
            position = source, values = new { ECODE = "COPY-PART-SOURCE", ENAME = "源零组件", Description = "完整说明" } });
        var original = Assert.Single(await Rows(fixture, "Part"));
        var originalLink = Assert.Single((await Rows(fixture, "WorkspaceFolderContent")).Where(row => Equals(row["ChildModelKey"], "Part")));
        var input = new { mode = "copy", sourcePosition = source, linkIds = new[] { originalLink["LID"] }, targetPosition = target };
        var preparation = await PartCopyCommand(fixture, admin, PartCopyDataPhase.Prepare, input);
        Assert.Equal("", preparation.GetProperty("values").GetProperty("ECODE").GetString());
        Assert.Equal("源零组件", preparation.GetProperty("values").GetProperty("ENAME").GetString());
        var operationId = preparation.GetProperty("operationId").GetString();
        var result = await PartCopyCommand(fixture, admin, PartCopyDataPhase.Submit, new {
            operationId, values = new { ECODE = "COPY-PART-TARGET", ENAME = "独立零组件", Description = "确认说明" } });
        Assert.True(result.GetProperty("succeeded").GetBoolean());
        Assert.Equal("committed", result.GetProperty("state").GetString());
        var parts = await Rows(fixture, "Part");
        Assert.Equal(2, parts.Length);
        Assert.Equal(JsonSerializer.Serialize(original), JsonSerializer.Serialize(Assert.Single(parts, row => Equals(row["EID"], original["EID"]))));
        var copied = Assert.Single(parts, row => Equals(row["ECODE"], "COPY-PART-TARGET"));
        Assert.NotEqual(original["EID"], copied["EID"]);
        Assert.Equal("独立零组件", copied["ENAME"]);
        Assert.Equal("确认说明", copied["Description"]);
        Assert.Equal(fixture.ActorId, copied["CreatedBy"]);
        var copiedLink = Assert.Single(await Rows(fixture, "WorkspaceFolderContent"), row => Equals(row["CEID"], copied["EID"]));
        Assert.Equal(FolderId(target, await Rows(fixture, "WorkspaceFolderContent")), copiedLink["PEID"]);
        var recovery = await Command(fixture, admin, "workspace.folder.resolveObjectCopy", "/api/workspace/content/objects/copy/result", new { operationId });
        Assert.True(recovery.GetProperty("succeeded").GetBoolean());
        Assert.False(recovery.TryGetProperty("id", out _));
        Assert.Equal(2, (await Rows(fixture, "Part")).Length);
        var change = Assert.Single(await Rows(fixture, "BusinessChange"), row => Equals(row["EventType"], "workspace.object.copied"));
        Assert.Equal(copiedLink["LID"], change["TargetId"]); Assert.Equal(fixture.ActorId, change["ActorId"]);
        Assert.False(string.IsNullOrWhiteSpace(change["TraceId"] as string));
        var facts = JsonSerializer.Deserialize<ICP.Platform.Modules.Audit.Contracts.BusinessAuditField[]>((string)change["FieldsJson"]!)!;
        Assert.Equal(new[] { "SourceLID", "SourcePEID", "SourceObject", "PEID", "ChildModelKey", "CEID" }, facts.Select(field => field.Field));
        Assert.All(facts, field => Assert.Null(field.Before));
        Assert.Equal(originalLink["LID"], facts[0].After.Value); Assert.Equal(originalLink["PEID"], facts[1].After.Value);
        Assert.Equal(original["EID"], facts[2].After.Value); Assert.Equal(copiedLink["PEID"], facts[3].After.Value);
        Assert.Equal("Part", facts[4].After.Value); Assert.Equal(copied["EID"], facts[5].After.Value);
    }
}
