using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ICP.Platform.Modules.Workspace;
using ICP.Platform.Testing;
using ICP.Platform.Persistence.EfCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using System.Data.Common;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed partial class WorkspaceClipboardSqlServerHttpApiTests
{
    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "cut及batch remove实际写入后故障和提交前取消整批回滚，成功才改变源关系")]
    public async Task CutAndRemove_RollBackDatabaseWritesOnFaultAndCancellation()
    {
        var control = new ReferenceCommitControl();
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(services => services.AddDbContext<EfPlatformDbContext>((_, options) => options.AddInterceptors(control)), includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync(); var root = await Root(fixture, admin);
        var a = await Folder(fixture, admin, root, "A"); var b = await Folder(fixture, admin, root, "B");
        var x = await Folder(fixture, admin, a, "X"); var y = await Folder(fixture, admin, a, "Y");
        var ids = new[] { x.LinkIds[^1], y.LinkIds[^1] };
        var before = Snapshot(await Rows(fixture, "WorkspaceFolderContent")); var objects = Snapshot(await Rows(fixture, "WorkspaceFolder"));
        var changesBefore = Snapshot(await Rows(fixture, "BusinessChange")); control.DeletedLinkIds = ids;
        foreach (var removing in new[] { false, true })
        {
            var behavior = removing ? "workspace.folder.removeSelected" : "workspace.folder.paste";
            var path = removing ? "references/remove" : "paste";
            object body = removing ? new { position = a, linkIds = ids } : new { mode = "cut", sourcePosition = a, linkIds = ids, targetPosition = b };
            control.Fail = true;
            using (var failure = await Send(fixture, admin, behavior, path, body)) Assert.Equal(HttpStatusCode.InternalServerError, failure.StatusCode);
            control.Fail = false; Assert.Equal(2, control.WrittenChanges); Assert.Equal(0, control.RemainingSourceReferences);
            Assert.Equal(before, Snapshot(await Rows(fixture, "WorkspaceFolderContent"))); Assert.Equal(changesBefore, Snapshot(await Rows(fixture, "BusinessChange")));
            control.ArmPause();
            using var cancellation = new CancellationTokenSource();
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/workspace/content/" + path) { Content = JsonContent.Create(body) };
            request.Headers.Add("X-NewP-Feature", Point(fixture, behavior).Position);
            request.Headers.Add("X-NewP-Data-Declaration", ClipboardDeclaration(fixture, Point(fixture, behavior))!.Id);
            var pending = admin.SendAsync(request, cancellation.Token);
            try
            {
                await control.Reached.Task.WaitAsync(TimeSpan.FromSeconds(20)); cancellation.Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await pending);
                await control.RolledBack.Task.WaitAsync(TimeSpan.FromSeconds(20));
            }
            finally { cancellation.Cancel(); control.Resume.TrySetResult(); try { (await pending).Dispose(); } catch (OperationCanceledException) { } control.Pause = false; }
            Assert.Equal(before, Snapshot(await Rows(fixture, "WorkspaceFolderContent"))); Assert.Equal(objects, Snapshot(await Rows(fixture, "WorkspaceFolder")));
            Assert.Equal(2, control.WrittenChanges); Assert.Equal(0, control.RemainingSourceReferences);
            Assert.Equal(changesBefore, Snapshot(await Rows(fixture, "BusinessChange")));
        }
        await Command(fixture, admin, "workspace.folder.paste", "/api/workspace/content/paste", new { mode = "cut", sourcePosition = a, linkIds = ids, targetPosition = b });
        var rows = await Rows(fixture, "WorkspaceFolderContent"); var movedIds = rows.Where(row => Equals(row["PEID"], FolderId(b, rows))).Select(row => (string)row["LID"]!).ToArray();
        Assert.Equal(2, movedIds.Length);
        Assert.Equal(2, (await Rows(fixture, "BusinessChange")).Count(row => Equals(row["EventType"], "workspace.reference.moved")));
        await Command(fixture, admin, "workspace.folder.removeSelected", "/api/workspace/content/references/remove", new { position = b, linkIds = movedIds });
        Assert.Equal(objects, Snapshot(await Rows(fixture, "WorkspaceFolder")));
        Assert.Equal(2, (await Rows(fixture, "BusinessChange")).Count(row => Equals(row["EventType"], "workspace.reference.removed")));
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "cut及移除在登记第2条审计时故障、审计写入或保存失败时关系和记录整体回滚")]
    public async Task CutAndRemove_AuditRegistrationAndSaveFailuresRollBackEveryFact()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync(); var root = await Root(fixture, admin);
        var a = await Folder(fixture, admin, root, "A"); var b = await Folder(fixture, admin, root, "B");
        var x = await Folder(fixture, admin, a, "X"); var y = await Folder(fixture, admin, a, "Y");
        var ids = new[] { x.LinkIds[^1], y.LinkIds[^1] };
        var references = Snapshot(await Rows(fixture, "WorkspaceFolderContent")); var objects = Snapshot(await Rows(fixture, "WorkspaceFolder"));
        var changes = Snapshot(await Rows(fixture, "BusinessChange"));
        foreach (var removing in new[] { false, true })
        {
            var behavior = removing ? "workspace.folder.removeSelected" : "workspace.folder.paste";
            var path = removing ? "references/remove" : "paste";
            object body = removing ? new { position = a, linkIds = ids } : new { mode = "cut", sourcePosition = a, linkIds = ids, targetPosition = b };
            foreach (var mode in new[] { "register", "write", "save", "second-registration" })
            {
                var registered = 0; fixture.Fault.Mode = mode;
                if (mode == "second-registration") fixture.Fault.AfterRegister = (change, _) => {
                    if (change.EventType == (removing ? "workspace.reference.removed" : "workspace.reference.moved") && ++registered == 2)
                        throw new InvalidOperationException("Injected second reference audit registration failure.");
                    return Task.CompletedTask;
                };
                try
                {
                    using var failed = await Send(fixture, admin, behavior, path, body);
                    Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
                }
                finally { fixture.Fault.Mode = ""; fixture.Fault.AfterRegister = null; }
                if (mode == "second-registration") Assert.Equal(2, registered);
                Assert.Equal(references, Snapshot(await Rows(fixture, "WorkspaceFolderContent")));
                Assert.Equal(objects, Snapshot(await Rows(fixture, "WorkspaceFolder"))); Assert.Equal(changes, Snapshot(await Rows(fixture, "BusinessChange")));
            }
        }
    }

    private sealed class ReferenceCommitControl : SaveChangesInterceptor, IDbTransactionInterceptor
    {
        public bool Fail { get; set; }
        public bool Pause { get; set; }
        public string[] DeletedLinkIds { get; set; } = [];
        public int WrittenChanges { get; private set; }
        public int RemainingSourceReferences { get; private set; }
        public TaskCompletionSource Reached { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Resume { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource RolledBack { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void ArmPause() { Reached = new(TaskCreationOptions.RunContinuationsAsynchronously); Resume = new(TaskCreationOptions.RunContinuationsAsynchronously); RolledBack = new(TaskCreationOptions.RunContinuationsAsynchronously); Pause = true; }
        public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken token = default)
        {
            if (Fail || Pause)
            {
                // 从当前SQL事务观察已写记录与已删关系，不能用仅登记或ChangeTracker状态代替原子保存事实。
                var transaction = eventData.Context!.Database.CurrentTransaction!.GetDbTransaction();
                await using var command = transaction.Connection!.CreateCommand(); command.Transaction = transaction;
                command.CommandText = "SELECT (SELECT COUNT(*) FROM [BusinessChange] WHERE [EventType] IN ('workspace.reference.moved','workspace.reference.removed')), (SELECT COUNT(*) FROM [WorkspaceFolderContent] WHERE [LID] IN (@first,@second))";
                var first = command.CreateParameter(); first.ParameterName = "@first"; first.Value = DeletedLinkIds[0]; command.Parameters.Add(first);
                var second = command.CreateParameter(); second.ParameterName = "@second"; second.Value = DeletedLinkIds[1]; command.Parameters.Add(second);
                await using var reader = await command.ExecuteReaderAsync(token); Assert.True(await reader.ReadAsync(token));
                WrittenChanges = reader.GetInt32(0); RemainingSourceReferences = reader.GetInt32(1);
            }
            if (Fail) throw new InvalidOperationException("Injected reference mutation failure after SQL writes.");
            if (Pause) { Reached.TrySetResult(); await Resume.Task.WaitAsync(token); }
            return result;
        }
        public Task TransactionRolledBackAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken token = default)
        { if (Pause) RolledBack.TrySetResult(); return Task.CompletedTask; }
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "共享源父cut竞争只成功一次，并发跨父互成环整批拒绝一个，文件夹按最新末尾追加")]
    public async Task Cut_SerializesConcurrentMovesAndAppendsFolderSelectionOrder()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync(); var root = await Root(fixture, admin);
        var a = await Folder(fixture, admin, root, "A"); var b = await Folder(fixture, admin, root, "B"); var c = await Folder(fixture, admin, root, "C");
        var x = await Folder(fixture, admin, a, "X"); var y = await Folder(fixture, admin, a, "Y"); var first = await Folder(fixture, admin, b, "先前文件夹");
        var original = await Rows(fixture, "WorkspaceFolderContent"); var xId = FolderId(x, original); var yId = FolderId(y, original);
        var body = new { mode = "cut", sourcePosition = a, linkIds = new[] { y.LinkIds[^1], x.LinkIds[^1] }, targetPosition = b };
        var race = await Task.WhenAll(Send(fixture, admin, "workspace.folder.paste", "paste", body), Send(fixture, admin, "workspace.folder.paste", "paste", body));
        try { Assert.Single(race, response => response.StatusCode == HttpStatusCode.OK); await Problem(Assert.Single(race, response => response.StatusCode == HttpStatusCode.NotFound), HttpStatusCode.NotFound, "Workspace.ReferenceUnavailable"); }
        finally { foreach (var response in race) response.Dispose(); }
        var rows = await Rows(fixture, "WorkspaceFolderContent");
        Assert.Equal(new[] { FolderId(first, rows), yId, xId }, rows.Where(row => Equals(row["PEID"], FolderId(b, rows))).OrderBy(row => row["TreeOrdinal"]).Select(row => (string)row["CEID"]!));
        var cycles = await Task.WhenAll(Send(fixture, admin, "workspace.folder.paste", "paste", new { mode = "cut", sourcePosition = root, linkIds = new[] { b.LinkIds[0] }, targetPosition = c }),
            Send(fixture, admin, "workspace.folder.paste", "paste", new { mode = "cut", sourcePosition = root, linkIds = new[] { c.LinkIds[0] }, targetPosition = b }));
        try { Assert.Single(cycles, response => response.StatusCode == HttpStatusCode.OK); Assert.Single(cycles, response => response.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.NotFound); }
        finally { foreach (var response in cycles) response.Dispose(); }

    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "剪切只读预检、同父零写与真正移动精确LID，保留共享入向和非空文件夹内容")]
    public async Task Cut_QualifiesWithoutWritesAndMovesOnlyTheSelectedReference()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync(); var root = await Root(fixture, admin);
        var a = await Folder(fixture, admin, root, "A"); var b = await Folder(fixture, admin, root, "B");
        var c = await Folder(fixture, admin, root, "C"); var g = await Folder(fixture, admin, a, "G");
        await Command(fixture, admin, "workspace.folder.createPart", "/api/workspace/content/parts", new { position = g, values = new { ECODE = "CUT-CHILD", ENAME = "子内容" } });
        var links = await Rows(fixture, "WorkspaceFolderContent"); var gid = FolderId(g, links);
        await Command(fixture, admin, "workspace.folder.add", "/api/workspace/content/references", new { position = b, target = new { modelKey = "WorkspaceFolder", id = gid } });
        var before = Snapshot(await Rows(fixture, "WorkspaceFolderContent")); var folders = Snapshot(await Rows(fixture, "WorkspaceFolder"));
        var auditBefore = Snapshot(await Rows(fixture, "BusinessChange"));
        // 首个RED使用已有paste功能：尚未支持cut时应在真实端点失败，而非仅缺少声明。
        var unchanged = await Command(fixture, admin, "workspace.folder.paste", "/api/workspace/content/paste", new { mode = "cut", sourcePosition = a, linkIds = new[] { g.LinkIds[^1] }, targetPosition = a });
        Assert.True(unchanged.GetProperty("noChange").GetBoolean());
        Assert.Equal(before, Snapshot(await Rows(fixture, "WorkspaceFolderContent")));
        Assert.Equal(auditBefore, Snapshot(await Rows(fixture, "BusinessChange")));
        await Command(fixture, admin, "workspace.folder.cut", "/api/workspace/content/clipboard/cut/validate", new { mode = "cut", sourcePosition = a, linkIds = new[] { g.LinkIds[^1] } });
        Assert.Equal(before, Snapshot(await Rows(fixture, "WorkspaceFolderContent")));
        Assert.Equal(auditBefore, Snapshot(await Rows(fixture, "BusinessChange")));
        var moved = await Command(fixture, admin, "workspace.folder.paste", "/api/workspace/content/paste", new { mode = "cut", sourcePosition = a, linkIds = new[] { g.LinkIds[^1] }, targetPosition = c });
        Assert.False(moved.GetProperty("noChange").GetBoolean());
        links = await Rows(fixture, "WorkspaceFolderContent");
        Assert.DoesNotContain(links, row => Equals(row["LID"], g.LinkIds[^1]));
        Assert.Equal(2, links.Count(row => Equals(row["ChildModelKey"], "WorkspaceFolder") && Equals(row["CEID"], gid)));
        Assert.Contains(links, row => Equals(row["PEID"], FolderId(c, links)) && Equals(row["CEID"], gid));
        Assert.Contains(links, row => Equals(row["PEID"], gid) && Equals(row["ChildModelKey"], "Part"));
        Assert.Equal(folders, Snapshot(await Rows(fixture, "WorkspaceFolder")));
        var movedReference = links.Single(row => Equals(row["PEID"], FolderId(c, links)) && Equals(row["CEID"], gid));
        var movedChange = Assert.Single(await Rows(fixture, "BusinessChange"), row => Equals(row["EventType"], "workspace.reference.moved"));
        Assert.Equal(movedReference["LID"], movedChange["TargetId"]);
        Assert.Equal("workspace.content", movedChange["Resource"]); Assert.Equal(fixture.ActorId, movedChange["ActorId"]);
        Assert.Equal(movedReference["LCreatedAt"], movedChange["OccurredAt"]); Assert.False(string.IsNullOrWhiteSpace(movedChange["TraceId"] as string));
        var movedFields = JsonSerializer.Deserialize<ICP.Platform.Modules.Audit.Contracts.BusinessAuditField[]>((string)movedChange["FieldsJson"]!)!;
        Assert.Equal(new[] { "SourceLID", "SourcePEID", "PEID", "ChildModelKey", "CEID" }, movedFields.Select(field => field.Field));
        Assert.Equal(new[] { g.LinkIds[^1], FolderId(a, links), FolderId(c, links), "WorkspaceFolder", gid }, movedFields.Select(field => field.After.Value));
        using var stale = await Send(fixture, admin, "workspace.folder.paste", "paste", new { mode = "cut", sourcePosition = a, linkIds = new[] { g.LinkIds[^1] }, targetPosition = b });
        await Problem(stale, HttpStatusCode.NotFound, "Workspace.ReferenceUnavailable");
        Assert.Single(await Rows(fixture, "BusinessChange"), row => Equals(row["EventType"], "workspace.reference.moved"));
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "cut只接受正式cut预检功能且不借用copy数据动作，提交及同父no-op每次重验源父/目标父/端点及完整路径")]
    public async Task Cut_RechecksModeSpecificFeaturesAndEveryCurrentDataPermission()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync(); var root = await Root(fixture, admin);
        var a = await Folder(fixture, admin, root, "A"); var b = await Folder(fixture, admin, root, "B");
        await Command(fixture, admin, "workspace.folder.createPart", "/api/workspace/content/parts", new { position = a, values = new { ECODE = "CUT-PERM", ENAME = "资格" } });
        var rows = await Rows(fixture, "WorkspaceFolderContent"); var link = Assert.Single(rows, row => Equals(row["ChildModelKey"], "Part"));
        var ids = new[] { (string)link["LID"]! }; var objectId = (string)link["CEID"]!;
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "CutActor", "剪切账号", "cut-actor", "剪切账号",
            Point(fixture, "workspace.spaces.access"), Point(fixture, "workspace.folder.cut"), Point(fixture, "workspace.folder.copy"), Point(fixture, "workspace.folder.paste"));
        using var user = actor.Client;
        await Rule(fixture, admin, "CopyDenied", "part.part", objectId, "partCopy", "deny");
        await Command(fixture, user, "workspace.folder.cut", "/api/workspace/content/clipboard/cut/validate", new { mode = "cut", sourcePosition = a, linkIds = ids });
        using (var borrowed = await Send(fixture, user, "workspace.folder.copy", "clipboard/cut/validate", new { mode = "cut", sourcePosition = a, linkIds = ids })) Assert.Equal(HttpStatusCode.Forbidden, borrowed.StatusCode);
        using (var forged = await Send(fixture, user, "workspace.folder.copy", "clipboard/validate", new { mode = "cut", sourcePosition = a, linkIds = ids })) await Problem(forged, HttpStatusCode.BadRequest, "Workspace.InvalidClipboardInput");
        using (var mismatched = await Send(fixture, user, "workspace.folder.cut", "clipboard/cut/validate", new { mode = "copy", sourcePosition = a, linkIds = ids })) await Problem(mismatched, HttpStatusCode.BadRequest, "Workspace.InvalidClipboardInput");
        var before = Snapshot(rows);
        foreach (var (resource, id, action, code, status) in new[] {
            ("workspace.folder", FolderId(a, rows), "edit", "Workspace.ParentWriteDenied", HttpStatusCode.Forbidden),
            ("part.part", objectId, "edit", "Workspace.ObjectWriteDenied", HttpStatusCode.Forbidden),
            ("part.part", objectId, "read", "Workspace.TargetUnavailable", HttpStatusCode.NotFound) })
        {
            await Rule(fixture, admin, "CutDenied", resource, id, action, "deny");
            foreach (var target in new[] { a, b })
            {
                using var denied = await Send(fixture, user, "workspace.folder.paste", "paste", new { mode = "cut", sourcePosition = a, linkIds = ids, targetPosition = target });
                await Problem(denied, status, code);
            }
            using var qualification = await Send(fixture, user, "workspace.folder.cut", "clipboard/cut/validate", new { mode = "cut", sourcePosition = a, linkIds = ids });
            await Problem(qualification, status, code);
            Assert.Equal(before, Snapshot(await Rows(fixture, "WorkspaceFolderContent")));
            await Rule(fixture, admin, "CutDenied", resource, id, action, "allow");
        }
        await Rule(fixture, admin, "TargetDenied", "workspace.folder", FolderId(b, rows), "edit", "deny");
        using (var denied = await Send(fixture, user, "workspace.folder.paste", "paste", new { mode = "cut", sourcePosition = a, linkIds = ids, targetPosition = b })) await Problem(denied, HttpStatusCode.Forbidden, "Workspace.ParentWriteDenied");
        await Rule(fixture, admin, "AncestorHidden", "workspace.folder", root.RootFolderId, "read", "deny");
        using (var hidden = await Send(fixture, user, "workspace.folder.paste", "paste", new { mode = "cut", sourcePosition = a, linkIds = ids, targetPosition = a })) await Problem(hidden, HttpStatusCode.NotFound, "Workspace.PositionUnavailable");
        Assert.Equal(before, Snapshot(await Rows(fixture, "WorkspaceFolderContent")));
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "历史根Part与Document同父cut零写，真实新落入仍根只接收folder，混合批次整体失败")]
    public async Task Cut_LegacyRootNoOpPrecedesNewIngressAndKeepsAllAudits()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync(); var root = await Root(fixture, admin);
        var a = await Folder(fixture, admin, root, "来源"); var b = await Folder(fixture, admin, root, "目标");
        await Folder(fixture, admin, a, "子文件夹");
        foreach (var kind in new[] { "Part", "Document" }) await Command(fixture, admin, "workspace.folder.create" + kind,
            "/api/workspace/content/" + (kind == "Part" ? "parts" : "documents"), new { position = a, values = new { ECODE = "CUT-LEGACY", ENAME = kind } });
        var rows = await Rows(fixture, "WorkspaceFolderContent"); var sourceId = FolderId(a, rows);
        var ids = rows.Where(row => Equals(row["PEID"], sourceId)).Select(row => (string)row["LID"]!).ToArray();
        var before = Snapshot(rows);
        using (var rejected = await Send(fixture, admin, "workspace.folder.paste", "paste", new { mode = "cut", sourcePosition = a, linkIds = ids, targetPosition = root })) await Problem(rejected, HttpStatusCode.BadRequest, "Workspace.RootOnlyFolders");
        Assert.Equal(before, Snapshot(await Rows(fixture, "WorkspaceFolderContent")));
        var businessIds = rows.Where(row => !Equals(row["ChildModelKey"], "WorkspaceFolder")).Select(row => (string)row["LID"]!).ToArray();
        using (var scope = fixture.Services.CreateScope()) await scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>().Set<Dictionary<string, object?>>("WorkspaceFolderContent")
            .Where(row => businessIds.Contains(EF.Property<string>(row, "LID"))).ExecuteUpdateAsync(set => set.SetProperty(row => EF.Property<string>(row, "PEID"), root.RootFolderId));
        before = Snapshot(await Rows(fixture, "WorkspaceFolderContent"));
        var folderBefore = Snapshot(await Rows(fixture, "WorkspaceFolder")); var partBefore = Snapshot(await Rows(fixture, "Part")); var documentBefore = Snapshot(await Rows(fixture, "Document"));
        var changesBefore = Snapshot(await Rows(fixture, "BusinessChange"));
        var receipt = await Command(fixture, admin, "workspace.folder.paste", "/api/workspace/content/paste", new { mode = "cut", sourcePosition = root, linkIds = businessIds, targetPosition = root });
        Assert.True(receipt.GetProperty("noChange").GetBoolean());
        Assert.Equal(before, Snapshot(await Rows(fixture, "WorkspaceFolderContent"))); Assert.Equal(folderBefore, Snapshot(await Rows(fixture, "WorkspaceFolder")));
        Assert.Equal(partBefore, Snapshot(await Rows(fixture, "Part"))); Assert.Equal(documentBefore, Snapshot(await Rows(fixture, "Document")));
        Assert.Equal(changesBefore, Snapshot(await Rows(fixture, "BusinessChange")));
        await Command(fixture, admin, "workspace.folder.paste", "/api/workspace/content/paste", new { mode = "cut", sourcePosition = root, linkIds = businessIds, targetPosition = b });
        rows = await Rows(fixture, "WorkspaceFolderContent"); Assert.DoesNotContain(rows, row => businessIds.Contains((string)row["LID"]!));
        Assert.Equal(2, rows.Count(row => Equals(row["PEID"], FolderId(b, rows))));
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "整批移除仅父edit和端点read；非空folder、最后引用与共享出现位置保留对象，不调用delete")]
    public async Task RemoveSelected_UsesReadOnlyEndpointEligibilityAndRemovesExactReferencesAtomically()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync(); var root = await Root(fixture, admin);
        var a = await Folder(fixture, admin, root, "A"); var b = await Folder(fixture, admin, root, "B"); var g = await Folder(fixture, admin, a, "非空");
        await Command(fixture, admin, "workspace.folder.createPart", "/api/workspace/content/parts", new { position = g, values = new { ECODE = "REMOVE-CHILD", ENAME = "子内容" } });
        await Command(fixture, admin, "workspace.folder.createPart", "/api/workspace/content/parts", new { position = a, values = new { ECODE = "REMOVE-LAST", ENAME = "最后引用" } });
        var rows = await Rows(fixture, "WorkspaceFolderContent"); var aid = FolderId(a, rows); var gid = FolderId(g, rows);
        await Command(fixture, admin, "workspace.folder.add", "/api/workspace/content/references", new { position = b, target = new { modelKey = "WorkspaceFolder", id = aid } });
        var sharedA = b with { LinkIds = [.. b.LinkIds, (string)(await Rows(fixture, "WorkspaceFolderContent")).Single(row => Equals(row["PEID"], FolderId(b, rows)) && Equals(row["CEID"], aid))["LID"]!] };
        rows = await Rows(fixture, "WorkspaceFolderContent"); var ids = rows.Where(row => Equals(row["PEID"], aid)).Select(row => (string)row["LID"]!).ToArray();
        var removedReferences = rows.Where(row => ids.Contains((string)row["LID"]!)).ToArray();
        var part = rows.Single(row => Equals(row["PEID"], aid) && Equals(row["ChildModelKey"], "Part"));
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "RemoveActor", "移除账号", "remove-actor", "移除账号", Point(fixture, "workspace.spaces.access"), Point(fixture, "workspace.folder.removeSelected"));
        using var user = actor.Client;
        foreach (var action in new[] { "edit", "delete", "partCopy" }) await Rule(fixture, admin, "NoObject" + action, "part.part", (string)part["CEID"]!, action, "deny");
        foreach (var action in new[] { "edit", "delete", "workspaceCopy" }) await Rule(fixture, admin, "NoFolder" + action, "workspace.folder", gid, action, "deny");
        var before = Snapshot(rows); var folders = Snapshot(await Rows(fixture, "WorkspaceFolder")); var parts = Snapshot(await Rows(fixture, "Part"));
        var changesBefore = Snapshot(await Rows(fixture, "BusinessChange"));
        using (var stale = await Send(fixture, user, "workspace.folder.removeSelected", "references/remove", new { position = a, linkIds = ids.Concat(["missing"]).ToArray() })) await Problem(stale, HttpStatusCode.NotFound, "Workspace.ReferenceUnavailable");
        Assert.Equal(before, Snapshot(await Rows(fixture, "WorkspaceFolderContent")));
        Assert.Equal(changesBefore, Snapshot(await Rows(fixture, "BusinessChange")));
        await Rule(fixture, admin, "HiddenEndpoint", "part.part", (string)part["CEID"]!, "read", "deny");
        changesBefore = Snapshot(await Rows(fixture, "BusinessChange"));
        using (var hidden = await Send(fixture, user, "workspace.folder.removeSelected", "references/remove", new { position = a, linkIds = ids })) await Problem(hidden, HttpStatusCode.NotFound, "Workspace.TargetUnavailable");
        Assert.Equal(before, Snapshot(await Rows(fixture, "WorkspaceFolderContent")));
        Assert.Equal(changesBefore, Snapshot(await Rows(fixture, "BusinessChange")));
        await Rule(fixture, admin, "HiddenEndpoint", "part.part", (string)part["CEID"]!, "read", "allow");
        await Command(fixture, user, "workspace.folder.removeSelected", "/api/workspace/content/references/remove", new { position = sharedA, linkIds = ids });
        rows = await Rows(fixture, "WorkspaceFolderContent"); Assert.DoesNotContain(rows, row => Equals(row["PEID"], aid));
        Assert.Contains(rows, row => Equals(row["PEID"], gid)); Assert.Equal(2, rows.Count(row => Equals(row["CEID"], aid)));
        Assert.Equal(folders, Snapshot(await Rows(fixture, "WorkspaceFolder"))); Assert.Equal(parts, Snapshot(await Rows(fixture, "Part")));
        var changes = (await Rows(fixture, "BusinessChange")).Where(row => Equals(row["EventType"], "workspace.reference.removed")).ToArray();
        Assert.Equal(2, changes.Length);
        foreach (var reference in removedReferences)
        {
            var change = Assert.Single(changes, row => Equals(row["TargetId"], reference["LID"]));
            Assert.Equal("workspace.content", change["Resource"]); Assert.Equal(actor.AccountId, change["ActorId"]);
            Assert.False(string.IsNullOrWhiteSpace(change["TraceId"] as string));
            var fields = JsonSerializer.Deserialize<ICP.Platform.Modules.Audit.Contracts.BusinessAuditField[]>((string)change["FieldsJson"]!)!;
            Assert.Equal(new[] { "PEID", "ChildModelKey", "CEID" }, fields.Select(field => field.Field));
            Assert.Equal(new[] { (string)reference["PEID"]!, (string)reference["ChildModelKey"]!, (string)reference["CEID"]! }, fields.Select(field => field.Before!.Value));
            Assert.All(fields, field => Assert.Null(field.After.Value));
            var detail = await FeatureHttpTestRequests.ReadAsync(admin, Assert.Single(fixture.FeatureCatalog, point => point.BehaviorKey == "audit.change.read"), "/api/audit/business-changes/" + change["EID"]);
            Assert.Equal(reference["LID"], detail.GetProperty("fields").GetProperty("TargetId").GetString());
            Assert.Equal(3, detail.GetProperty("summaries").GetProperty("changes").GetProperty("data").GetArrayLength());
        }
        using (var single = await FeatureHttpTestRequests.SendAsync(admin, RowPoint(fixture, "remove"), HttpMethod.Delete, "/api/workspace/content/references/" + sharedA.LinkIds[^1], new { position = b }, ClipboardDeclaration(fixture, RowPoint(fixture, "remove")))) Assert.Equal(HttpStatusCode.OK, single.StatusCode);
        Assert.Single(await Rows(fixture, "BusinessChange"), row => Equals(row["EventType"], "workspace.reference.removed") && Equals(row["TargetId"], sharedA.LinkIds[^1]));
        Assert.Equal(folders, Snapshot(await Rows(fixture, "WorkspaceFolder"))); Assert.Equal(parts, Snapshot(await Rows(fixture, "Part")));
    }
}
