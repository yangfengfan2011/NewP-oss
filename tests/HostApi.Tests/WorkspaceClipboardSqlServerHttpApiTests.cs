using System.Net;
using System.Net.Http.Json;
using System.Data.Common;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Claims;
using System.Text.Json;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Modules.Authorization.DTOs;
using ICP.Platform.Modules.Workspace;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Http;
using Xunit;
using Xunit.Abstractions;

namespace ICP.Platform.HostApi.Tests;

public sealed partial class WorkspaceClipboardSqlServerHttpApiTests(ITestOutputHelper output)
{
    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "只读预检失败或取消归还自己打开的连接，已有调用方连接及关系保持不变")]
    public async Task Qualification_ReturnsItsConnectionOnFailureAndCancellationWithoutClosingTheCallerConnection()
    {
        var control = new QualificationReadControl();
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(services => services.AddDbContext<EfPlatformDbContext>((_, options) => options.AddInterceptors(control)), includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync(); var root = await Root(fixture, admin);
        var source = await Folder(fixture, admin, root, "连接来源");
        await Command(fixture, admin, "workspace.folder.createPart", "/api/workspace/content/parts", new { position = source, values = new { ECODE = "COPY-CONNECTION", ENAME = "连接资格" } });
        var before = Snapshot(await Rows(fixture, "WorkspaceFolderContent"));
        var linkId = (await Rows(fixture, "WorkspaceFolderContent")).Single(row => Equals(row["ChildModelKey"], "Part"))["LID"] as string;
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
        var contents = scope.ServiceProvider.GetRequiredService<WorkspaceContents>();
        var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>(); var previous = accessor.HttpContext;
        accessor.HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, fixture.ActorId)], "qualification-test")) };
        try
        {
            var connection = db.Database.GetDbConnection();
            await Assert.ThrowsAsync<ICP.Platform.Crud.LinkQueries.LinkQueryException>(() => contents.ValidateClipboardAsync(fixture.ActorId, "copy", source, ["missing-link"], CancellationToken.None));
            Assert.Equal(System.Data.ConnectionState.Closed, connection.State);
            control.Pause = true;
            using var cancellation = new CancellationTokenSource();
            var pending = contents.ValidateClipboardAsync(fixture.ActorId, "copy", source, [linkId!], cancellation.Token);
            try
            {
                await control.Reached.Task.WaitAsync(TimeSpan.FromSeconds(20)); cancellation.Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await pending);
            }
            finally { cancellation.Cancel(); try { await pending; } catch (OperationCanceledException) { } }
            Assert.Equal(System.Data.ConnectionState.Closed, connection.State); control.Pause = false;
            await db.Database.OpenConnectionAsync();
            try
            {
                var snapshot = await contents.ValidateClipboardAsync(fixture.ActorId, "copy", source, [linkId!], CancellationToken.None);
                Assert.Equal(linkId, Assert.Single(snapshot.Items).LinkId); Assert.Equal(System.Data.ConnectionState.Open, connection.State);
                await Assert.ThrowsAsync<ICP.Platform.Crud.LinkQueries.LinkQueryException>(() => contents.ValidateClipboardAsync(fixture.ActorId, "copy", source, ["missing-link"], CancellationToken.None));
                Assert.Equal(System.Data.ConnectionState.Open, connection.State); Assert.Null(db.Database.CurrentTransaction);
            }
            finally { await db.Database.CloseConnectionAsync(); }
        }
        finally { accessor.HttpContext = previous; }
        Assert.Equal(before, Snapshot(await Rows(fixture, "WorkspaceFolderContent")));
    }

    private sealed class QualificationReadControl : DbCommandInterceptor
    {
        public bool Pause { get; set; }
        public TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData data,
            InterceptionResult<DbDataReader> result, CancellationToken token = default)
        {
            if (Pause && command.CommandText.Contains("WorkspaceFolderContent", StringComparison.Ordinal))
            { Reached.TrySetResult(); await Task.Delay(Timeout.Infinite, token); }
            return result;
        }
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "100条真实Part引用预检与粘贴记录SQL往返、窄选择规模、业务实体物化及原子新增规模")]
    public async Task MaximumSelection_RecordsQualificationAndPasteDatabaseAndMaterializationCosts()
    {
        var cost = new ClipboardCost();
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(services => services.AddDbContext<EfPlatformDbContext>((_, options) => options.AddInterceptors(cost)), includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync(); var root = await Root(fixture, admin);
        var a = await Folder(fixture, admin, root, "容量来源"); var b = await Folder(fixture, admin, root, "容量目标"); var c = await Folder(fixture, admin, root, "移动目标");
        await Command(fixture, admin, "workspace.folder.createPart", "/api/workspace/content/parts", new { position = a, values = new { ECODE = "COPY-MAX-000", ENAME = "真实Part种子" } });
        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
            var ids = scope.ServiceProvider.GetRequiredService<ICP.Platform.Crud.ObjectIds.IPlatformObjectIdGenerator>();
            var part = await db.Set<Dictionary<string, object?>>("Part").AsNoTracking().SingleAsync();
            var link = await db.Set<Dictionary<string, object?>>("WorkspaceFolderContent").AsNoTracking().SingleAsync(row => EF.Property<string>(row, "ChildModelKey") == "Part");
            // 容量夹具复用正式Part实体/审计/Link形状，测试行为仍走真实资格目录与HTTP。
            for (var index = 1; index < 100; index++)
            {
                var id = ids.NewId("Part");
                db.Set<Dictionary<string, object?>>("Part").Add(new(part) { ["EID"] = id, ["ECODE"] = "COPY-MAX-" + index.ToString("D3") });
                db.Set<Dictionary<string, object?>>("WorkspaceFolderContent").Add(new(link) { ["LID"] = ids.NewId("WorkspaceFolderContent"), ["CEID"] = id });
            }
            await db.SaveChangesAsync();
        }
        var links = await Rows(fixture, "WorkspaceFolderContent"); var parentId = FolderId(a, links);
        var selection = links.Where(row => Equals(row["PEID"], parentId)).Select(row => (string)row["LID"]!).Reverse().ToArray();
        Assert.Equal(100, selection.Length);
        string database;
        using (var scope = fixture.Services.CreateScope()) database = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>().Database.GetDbConnection().Database;
        using var sql = new WorkspaceReferenceSqlServerHttpApiTests.SqlObservation(database);
        using var timing = new ClipboardSqlTiming(database);
        sql.Begin(); timing.Begin();
        cost.Begin(); var qualificationClock = System.Diagnostics.Stopwatch.StartNew();
        var snapshot = await Command(fixture, admin, "workspace.folder.copy", "/api/workspace/content/clipboard/validate", new { mode = "copy", sourcePosition = a, linkIds = selection });
        qualificationClock.Stop(); var qualification = cost.End();
        var qualificationSql = sql.End(); var qualificationTiming = timing.End();
        Assert.Equal(100, snapshot.GetProperty("items").GetArrayLength()); Assert.Equal(0, qualification.BusinessEntities); Assert.Equal(0, qualification.Writes);
        sql.Begin(); timing.Begin(); cost.Begin(); var warmClock = Stopwatch.StartNew();
        var warmSnapshot = await Command(fixture, admin, "workspace.folder.copy", "/api/workspace/content/clipboard/validate", new { mode = "copy", sourcePosition = a, linkIds = selection });
        warmClock.Stop(); var warmQualification = cost.End(); var warmSql = sql.End(); var warmTiming = timing.End();
        Assert.Equal(snapshot.GetRawText(), warmSnapshot.GetRawText()); Assert.Equal(0, warmQualification.BusinessEntities); Assert.Equal(0, warmQualification.Writes);
        sql.Begin(); timing.Begin();
        cost.Begin(); var pasteClock = System.Diagnostics.Stopwatch.StartNew();
        await Command(fixture, admin, "workspace.folder.paste", "/api/workspace/content/paste", new { mode = "copy", sourcePosition = a, linkIds = selection, targetPosition = b });
        pasteClock.Stop(); var paste = cost.End();
        var pasteSql = sql.End(); var pasteTiming = timing.End();
        links = await Rows(fixture, "WorkspaceFolderContent");
        Assert.Equal(100, links.Count(row => Equals(row["PEID"], parentId)));
        Assert.Equal(100, links.Count(row => Equals(row["PEID"], FolderId(b, links))));
        Assert.Equal(100, (await Rows(fixture, "Part")).Length); Assert.Equal(1, paste.GraphLocks); Assert.True(paste.Writes > 0); Assert.Equal(0, paste.BusinessEntities);
        output.WriteLine(JsonSerializer.Serialize(new { selection = 100, sourceLinkProjectionRows = 100, qualifiedSnapshotItems = 100, insertedReferences = 100,
            qualification, qualificationSql, qualificationTiming, qualificationWallSeconds = qualificationClock.Elapsed.TotalSeconds,
            warmQualification, warmSql, warmTiming, warmQualificationWallSeconds = warmClock.Elapsed.TotalSeconds,
            paste, pasteSql, pasteTiming, pasteWallSeconds = pasteClock.Elapsed.TotalSeconds,
            boundary = "One page <=100; per-object endpoint/read/copy decisions remain visible in command totals; no new capacity or SLA." }));
        timing.Begin(); cost.Begin(); var cutQualificationClock = Stopwatch.StartNew();
        await Command(fixture, admin, "workspace.folder.cut", "/api/workspace/content/clipboard/cut/validate", new { mode = "cut", sourcePosition = a, linkIds = selection });
        cutQualificationClock.Stop(); var cutQualification = cost.End(); var cutQualificationSql = timing.End();
        timing.Begin(); cost.Begin(); var cutClock = Stopwatch.StartNew();
        await Command(fixture, admin, "workspace.folder.paste", "/api/workspace/content/paste", new { mode = "cut", sourcePosition = a, linkIds = selection, targetPosition = c });
        cutClock.Stop(); var cut = cost.End(); var cutSql = timing.End();
        links = await Rows(fixture, "WorkspaceFolderContent"); var moved = links.Where(row => Equals(row["PEID"], FolderId(c, links))).Select(row => (string)row["LID"]!).ToArray();
        Assert.Equal(100, moved.Length); Assert.DoesNotContain(links, row => Equals(row["PEID"], parentId));
        timing.Begin(); cost.Begin(); var removeClock = Stopwatch.StartNew();
        await Command(fixture, admin, "workspace.folder.removeSelected", "/api/workspace/content/references/remove", new { position = c, linkIds = moved });
        removeClock.Stop(); var remove = cost.End(); var removeSql = timing.End();
        Assert.Equal(100, (await Rows(fixture, "Part")).Length);
        Assert.Equal(0, cutQualification.BusinessEntities); Assert.Equal(0, cut.BusinessEntities); Assert.Equal(0, remove.BusinessEntities);
        output.WriteLine(JsonSerializer.Serialize(new { selection = 100, cutQualification, cutQualificationSql, cutQualificationWallSeconds = cutQualificationClock.Elapsed.TotalSeconds,
            cut, cutSql, cutWallSeconds = cutClock.Elapsed.TotalSeconds, remove, removeSql, removeWallSeconds = removeClock.Elapsed.TotalSeconds,
            boundary = "SqlClient diagnostic totals cover EF and raw commands; <=100 ordered narrow source projections; no business entities materialized; per-object decisions retained, no new SLA." }));
    }

    // SqlClient事件同时覆盖EF及Link reader直接执行的命令；仅记录次数和耗时，不保留SQL或参数。
    private sealed class ClipboardSqlTiming : IObserver<DiagnosticListener>, IObserver<KeyValuePair<string, object?>>, IDisposable
    {
        private readonly IDisposable _all;
        private readonly string _database;
        private readonly List<IDisposable> _subscriptions = [];
        private readonly ConcurrentDictionary<object, long> _starts = new();
        private readonly ConcurrentQueue<double> _opens = new();
        private readonly ConcurrentQueue<double> _commands = new();
        private bool _active;
        public ClipboardSqlTiming(string database) { _database = database; _all = DiagnosticListener.AllListeners.Subscribe(this); }
        public void Begin() { _starts.Clear(); _opens.Clear(); _commands.Clear(); _active = true; }
        public object End() { _active = false; return new { connectionOpens = _opens.Count, connectionOpenMs = _opens.Sum(), databaseCommands = _commands.Count, commandMs = _commands.Sum(), maximumCommandMs = _commands.DefaultIfEmpty().Max() }; }
        public void OnNext(DiagnosticListener value) { if (value.Name == "SqlClientDiagnosticListener") _subscriptions.Add(value.Subscribe(this)); }
        public void OnNext(KeyValuePair<string, object?> value)
        {
            if (!_active || value.Value is null) return;
            var command = value.Value.GetType().GetProperty("Command")?.GetValue(value.Value) as DbCommand;
            var connection = command?.Connection ?? value.Value.GetType().GetProperty("Connection")?.GetValue(value.Value) as DbConnection;
            if (!string.Equals(connection?.Database, _database, StringComparison.Ordinal)) return;
            var opening = value.Key.Contains("ConnectionOpen", StringComparison.Ordinal);
            var key = opening ? (object?)connection : command;
            if (key is null) return;
            if (value.Key.EndsWith("Before", StringComparison.Ordinal)) _starts[key] = Stopwatch.GetTimestamp();
            else if (value.Key.EndsWith("After", StringComparison.Ordinal) && _starts.TryRemove(key, out var started))
                (opening ? _opens : _commands).Enqueue(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }
        public void OnCompleted() { }
        public void OnError(Exception error) { }
        public void Dispose() { _active = false; foreach (var subscription in _subscriptions) subscription.Dispose(); _all.Dispose(); }
    }

    private sealed record ClipboardCostSnapshot(int Commands, int Reads, int Writes, int GraphLocks, int BusinessEntities);
    private sealed class ClipboardCost : DbCommandInterceptor, IMaterializationInterceptor
    {
        private bool _active; private int _commands; private int _reads; private int _writes; private int _locks; private int _entities;
        public void Begin() { _commands = _reads = _writes = _locks = _entities = 0; _active = true; }
        public ClipboardCostSnapshot End() { _active = false; return new(_commands, _reads, _writes, _locks, _entities); }
        public object InitializedInstance(MaterializationInterceptionData data, object entity)
        { if (_active && data.EntityType.Name is "Part" or "WorkspaceFolder" or "WorkspaceFolderContent") _entities++; return entity; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData data, InterceptionResult<DbDataReader> result, CancellationToken token = default)
        { Observe(command); return ValueTask.FromResult(result); }
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData data, InterceptionResult<int> result, CancellationToken token = default)
        { Observe(command); return ValueTask.FromResult(result); }
        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command, CommandEventData data, InterceptionResult<object> result, CancellationToken token = default)
        { Observe(command); return ValueTask.FromResult(result); }
        private void Observe(DbCommand command)
        {
            if (!_active) return; _commands++;
            if (command.CommandText.Contains("Workspace.FolderGraph", StringComparison.Ordinal)) _locks++;
            if (command.CommandText.Contains("INSERT INTO [dbo].[WorkspaceFolderContent]", StringComparison.Ordinal)
                || command.CommandText.Contains("MERGE [dbo].[WorkspaceFolderContent]", StringComparison.Ordinal)
                || command.CommandText.Contains("DELETE FROM", StringComparison.Ordinal) && command.CommandText.Contains("WorkspaceFolderContent", StringComparison.Ordinal)) _writes++;
            else if (command.CommandText.Contains("SELECT", StringComparison.Ordinal)) _reads++;
        }
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "复制后源LID失效不能换同对象另一引用，竞争重复和互成环由唯一图锁串行化")]
    public async Task Paste_RechecksSourceRelationshipsAndSerializesConcurrentConflicts()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync(); var root = await Root(fixture, admin);
        var a = await Folder(fixture, admin, root, "A"); var b = await Folder(fixture, admin, root, "B");
        var x = await Folder(fixture, admin, a, "X"); var y = await Folder(fixture, admin, a, "Y");
        var oldIds = new[] { x.LinkIds[^1], y.LinkIds[^1] };
        await Command(fixture, admin, "workspace.folder.copy", "/api/workspace/content/clipboard/validate", new { mode = "copy", sourcePosition = a, linkIds = oldIds });
        using (var removed = await FeatureHttpTestRequests.SendAsync(admin, RowPoint(fixture, "remove"), HttpMethod.Delete,
            "/api/workspace/content/references/" + y.LinkIds[^1], new { position = a })) Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
        var all = await Rows(fixture, "WorkspaceFolderContent"); var yId = (await Rows(fixture, "WorkspaceFolder")).Single(row => Equals(row["ENAME"], "Y"))["EID"];
        await Command(fixture, admin, "workspace.folder.add", "/api/workspace/content/references", new { position = a, target = new { modelKey = "WorkspaceFolder", id = yId } });
        var before = Snapshot(await Rows(fixture, "WorkspaceFolderContent"));
        using (var stale = await Send(fixture, admin, "workspace.folder.paste", "paste", new { mode = "copy", sourcePosition = a, linkIds = oldIds, targetPosition = b }))
            await Problem(stale, HttpStatusCode.NotFound, "Workspace.ReferenceUnavailable");
        Assert.Equal(before, Snapshot(await Rows(fixture, "WorkspaceFolderContent")));
        var ids = (await Rows(fixture, "WorkspaceFolderContent")).Where(row => Equals(row["PEID"], FolderId(a, all))).Select(row => (string)row["LID"]!).ToArray();
        var copies = await Task.WhenAll(Send(fixture, admin, "workspace.folder.paste", "paste", new { mode = "copy", sourcePosition = a, linkIds = ids, targetPosition = b }),
            Send(fixture, admin, "workspace.folder.paste", "paste", new { mode = "copy", sourcePosition = a, linkIds = ids, targetPosition = b }));
        try { Assert.Single(copies, response => response.StatusCode == HttpStatusCode.OK); await Problem(Assert.Single(copies, response => response.StatusCode == HttpStatusCode.Conflict), HttpStatusCode.Conflict, "Workspace.DuplicateReference"); }
        finally { foreach (var response in copies) response.Dispose(); }
        var cycles = await Task.WhenAll(Send(fixture, admin, "workspace.folder.paste", "paste", new { mode = "copy", sourcePosition = root, linkIds = new[] { a.LinkIds[0] }, targetPosition = b }),
            Send(fixture, admin, "workspace.folder.paste", "paste", new { mode = "copy", sourcePosition = root, linkIds = new[] { b.LinkIds[0] }, targetPosition = a }));
        try { Assert.Single(cycles, response => response.StatusCode == HttpStatusCode.OK); await Problem(Assert.Single(cycles, response => response.StatusCode == HttpStatusCode.Conflict), HttpStatusCode.Conflict, "Workspace.CycleForbidden"); }
        finally { foreach (var response in cycles) response.Dispose(); }
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "整批已写SQL但提交前故障或请求取消时回滚全部关系，原对象与源审计不变")]
    public async Task Paste_RollsBackEveryReferenceAfterDatabaseWriteOnFailureOrCancellation()
    {
        var control = new PasteCommitControl();
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(services => services.AddDbContext<EfPlatformDbContext>((_, options) => options.AddInterceptors(control)), includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync(); var root = await Root(fixture, admin);
        var a = await Folder(fixture, admin, root, "A"); var b = await Folder(fixture, admin, root, "B");
        var x = await Folder(fixture, admin, a, "X"); var y = await Folder(fixture, admin, a, "Y");
        var body = new { mode = "copy", sourcePosition = a, linkIds = new[] { x.LinkIds[^1], y.LinkIds[^1] }, targetPosition = b };
        var before = Snapshot(await Rows(fixture, "WorkspaceFolderContent")); var objects = Snapshot(await Rows(fixture, "WorkspaceFolder"));
        control.Fail = true;
        using (var failed = await Send(fixture, admin, "workspace.folder.paste", "paste", body)) Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
        Assert.True(control.WrittenReferences >= 2); control.Fail = false;
        Assert.Equal(before, Snapshot(await Rows(fixture, "WorkspaceFolderContent"))); Assert.Equal(objects, Snapshot(await Rows(fixture, "WorkspaceFolder")));
        control.Pause = true;
        using var cancellation = new CancellationTokenSource();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/workspace/content/paste") { Content = JsonContent.Create(body) };
        request.Headers.Add("X-NewP-Feature", Point(fixture, "workspace.folder.paste").Position);
        var pending = admin.SendAsync(request, cancellation.Token);
        try
        {
            await control.Reached.Task.WaitAsync(TimeSpan.FromSeconds(20)); cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await pending);
            await control.RolledBack.Task.WaitAsync(TimeSpan.FromSeconds(20));
        }
        finally { cancellation.Cancel(); control.Resume.TrySetResult(); try { (await pending).Dispose(); } catch (OperationCanceledException) { } }
        control.Pause = false;
        Assert.Equal(before, Snapshot(await Rows(fixture, "WorkspaceFolderContent"))); Assert.Equal(objects, Snapshot(await Rows(fixture, "WorkspaceFolder")));
        await Command(fixture, admin, "workspace.folder.paste", "/api/workspace/content/paste", body);
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "当前版次legacy根Part/Document保持可读可编辑可复制移出可移除，来源个人路径不能被其他主体借用")]
    public async Task LegacyRootReferences_KeepCurrentDocumentIdentityAndRemainManageableWithoutNewRootWrites()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync(); var root = await Root(fixture, admin);
        var source = await Folder(fixture, admin, root, "合法准备目录"); var target = await Folder(fixture, admin, root, "目标");
        foreach (var action in new[] { "createPart", "createDocument" })
            await Command(fixture, admin, "workspace.folder." + action, "/api/workspace/content/" + (action == "createPart" ? "parts" : "documents"), new { position = source, values = new { ECODE = "LEGACY", ENAME = "历史根对象" } });
        var rows = await Rows(fixture, "WorkspaceFolderContent"); var business = rows.Where(row => !Equals(row["ChildModelKey"], "WorkspaceFolder")).ToArray();
        // 仅将正式创建的当前对象关系改到根，重建历史位置；不改Document主键、版本/版次外键或身份形状。
        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
            var lids = business.Select(row => (string)row["LID"]!).ToArray();
            await db.Set<Dictionary<string, object?>>("WorkspaceFolderContent").Where(row => lids.Contains(EF.Property<string>(row, "LID")))
                .ExecuteUpdateAsync(set => set.SetProperty(row => EF.Property<string>(row, "PEID"), root.RootFolderId));
        }
        var before = Snapshot(await Rows(fixture, "DocumentVersion")); var iteration = Snapshot(await Rows(fixture, "DocumentIteration"));
        var url = "/api/links/workspace.content?direction=forward&anchorModelKey=WorkspaceFolder&anchorId=" + root.RootFolderId + "&queryScope=" + Uri.EscapeDataString(JsonSerializer.Serialize(new { spaceId = root.SpaceId, rootFolderId = root.RootFolderId, linkIds = "[]" }));
        var visible = await FeatureHttpTestRequests.ReadAsync(admin, Point(fixture, "workspace.spaces.access"), url);
        Assert.Equal(4, visible.GetProperty("total").GetInt32());
        var ids = business.Select(row => (string)row["LID"]!).ToArray();
        await Command(fixture, admin, "workspace.folder.copy", "/api/workspace/content/clipboard/validate", new { mode = "copy", sourcePosition = root, linkIds = ids });
        await Command(fixture, admin, "workspace.folder.paste", "/api/workspace/content/paste", new { mode = "copy", sourcePosition = root, linkIds = ids, targetPosition = target });
        Assert.Equal(before, Snapshot(await Rows(fixture, "DocumentVersion"))); Assert.Equal(iteration, Snapshot(await Rows(fixture, "DocumentIteration")));
        foreach (var row in business)
        {
            using var edited = await FeatureHttpTestRequests.SendAsync(admin, RowPoint(fixture, "edit"), HttpMethod.Put, "/api/workspace/content/" + row["LID"], new { position = root, values = new { ENAME = "历史根仍可编辑" } });
            Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
            using var removed = await FeatureHttpTestRequests.SendAsync(admin, RowPoint(fixture, "remove"), HttpMethod.Delete, "/api/workspace/content/references/" + row["LID"], new { position = root });
            Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
        }
        Assert.Single(await Rows(fixture, "Document")); Assert.Single(await Rows(fixture, "DocumentVersion")); Assert.Single(await Rows(fixture, "DocumentIteration")); Assert.Single(await Rows(fixture, "Part"));
        var personal = await Command(fixture, admin, "workspace.spaces.access", "/api/workspace/spaces/personal/ensure");
        var personalPosition = new WorkspacePosition(personal.GetProperty("spaceId").GetString()!, personal.GetProperty("rootFolderId").GetString()!, []);
        var personalFolder = await Folder(fixture, admin, personalPosition, "私人来源");
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "PrivateCopy", "私人隔离", "private-copy", "私人隔离", Point(fixture, "workspace.spaces.access"), Point(fixture, "workspace.folder.copy"), Point(fixture, "workspace.folder.paste"));
        using var ordinary = actor.Client;
        using var privateSource = await Send(fixture, ordinary, "workspace.folder.copy", "clipboard/validate", new { mode = "copy", sourcePosition = personalPosition, linkIds = new[] { personalFolder.LinkIds[0] } });
        await Problem(privateSource, HttpStatusCode.NotFound, "Workspace.PositionUnavailable");
    }

    private sealed class PasteCommitControl : DbTransactionInterceptor
    {
        public bool Fail; public bool Pause; public int WrittenReferences;
        public TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Resume { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource RolledBack { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction, TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        {
            if (!Fail && !Pause) return result;
            WrittenReferences = eventData.Context!.ChangeTracker.Entries<Dictionary<string, object?>>().Count(entry => entry.Metadata.Name == "WorkspaceFolderContent" && entry.State == EntityState.Unchanged);
            if (Fail) throw new InvalidOperationException("Injected Workspace paste commit failure.");
            Reached.TrySetResult(); await Resume.Task.WaitAsync(cancellationToken); return result;
        }
        public override Task TransactionRolledBackAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        { if (Pause) RolledBack.TrySetResult(); return Task.CompletedTask; }
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "复制输入拒绝空、重复、超限、错父、伪造端点和根对象，不写持久事实")]
    public async Task Qualification_RejectsInvalidSelectionsAndNeverTreatsRootAsAnObject()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync(); var root = await Root(fixture, admin);
        var a = await Folder(fixture, admin, root, "A"); var b = await Folder(fixture, admin, root, "B");
        var linkId = a.LinkIds[0];
        var before = Snapshot(await Rows(fixture, "WorkspaceFolderContent"));
        foreach (var ids in new[] { Array.Empty<string>(), new[] { linkId, linkId }, Enumerable.Range(0, 101).Select(index => "link-" + index).ToArray(), new[] { "missing" } })
        {
            using var response = await Send(fixture, admin, "workspace.folder.copy", "clipboard/validate", new { mode = "copy", sourcePosition = root, linkIds = ids });
            await Problem(response, ids is { Length: 1 } ? HttpStatusCode.NotFound : HttpStatusCode.BadRequest, ids is { Length: 1 } ? "Workspace.ReferenceUnavailable" : "Workspace.InvalidSelection");
        }
        using (var wrongParent = await Send(fixture, admin, "workspace.folder.copy", "clipboard/validate", new { mode = "copy", sourcePosition = b, linkIds = new[] { linkId } }))
            await Problem(wrongParent, HttpStatusCode.NotFound, "Workspace.ReferenceUnavailable");
        foreach (var body in new object[] { new { mode = "cut", sourcePosition = root, linkIds = new[] { linkId } },
            new { mode = "copy", sourcePosition = root, linkIds = new[] { linkId }, modelKey = "Part" },
            new { mode = "copy", sourcePosition = new { root.SpaceId, root.RootFolderId, root.LinkIds, actorId = "forged" }, linkIds = new[] { linkId } } })
        {
            using var response = await Send(fixture, admin, "workspace.folder.copy", "clipboard/validate", body);
            await Problem(response, HttpStatusCode.BadRequest, "Workspace.InvalidClipboardInput");
        }
        Assert.Equal(before, Snapshot(await Rows(fixture, "WorkspaceFolderContent")));
        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
            var link = await db.Set<Dictionary<string, object?>>("WorkspaceFolderContent").SingleAsync(row => EF.Property<string>(row, "LID") == linkId);
            link["ChildModelKey"] = "Unknown/Type"; link["TreeOrdinal"] = null; await db.SaveChangesAsync();
        }
        using (var unknown = await Send(fixture, admin, "workspace.folder.copy", "clipboard/validate", new { mode = "copy", sourcePosition = root, linkIds = new[] { linkId } }))
            await Problem(unknown, HttpStatusCode.BadRequest, "Workspace.InvalidEndpoint");
        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
            var link = await db.Set<Dictionary<string, object?>>("WorkspaceFolderContent").SingleAsync(row => EF.Property<string>(row, "LID") == linkId);
            link["ChildModelKey"] = "WorkspaceFolder"; link["CEID"] = root.RootFolderId; link["TreeOrdinal"] = 1L; await db.SaveChangesAsync();
        }
        using var rootObject = await Send(fixture, admin, "workspace.folder.copy", "clipboard/validate", new { mode = "copy", sourcePosition = root, linkIds = new[] { linkId } });
        await Problem(rootObject, HttpStatusCode.Conflict, "Workspace.RootReferenceForbidden");
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "普通主体copy无需源父edit，独立copy/read、完整链及目标edit在粘贴时重验")]
    public async Task Copy_RechecksIndependentDataActionsSourcePathAndTargetWriteWithoutSourceEdit()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync(); var root = await Root(fixture, admin);
        var a = await Folder(fixture, admin, root, "A"); var b = await Folder(fixture, admin, root, "B"); var c = await Folder(fixture, admin, root, "C");
        await Command(fixture, admin, "workspace.folder.createPart", "/api/workspace/content/parts", new { position = a, values = new { ECODE = "COPY-PERMISSION", ENAME = "资格" } });
        var link = Assert.Single(await Rows(fixture, "WorkspaceFolderContent"), row => Equals(row["ChildModelKey"], "Part"));
        var sourceIds = new[] { (string)link["LID"]! }; var objectId = (string)link["CEID"]!;
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "CopyActor", "复制账号", "copy-actor", "复制账号", Point(fixture, "workspace.spaces.access"), Point(fixture, "workspace.folder.copy"), Point(fixture, "workspace.folder.paste"));
        using var ordinary = actor.Client;
        await Rule(fixture, admin, "SourceReadOnly", "workspace.folder", FolderId(a, await Rows(fixture, "WorkspaceFolderContent")), "edit", "deny");
        await Command(fixture, ordinary, "workspace.folder.copy", "/api/workspace/content/clipboard/validate", new { mode = "copy", sourcePosition = a, linkIds = sourceIds });
        await Command(fixture, ordinary, "workspace.folder.paste", "/api/workspace/content/paste", new { mode = "copy", sourcePosition = a, linkIds = sourceIds, targetPosition = b });
        foreach (var action in new[] { "partCopy", "read" })
        {
            await Rule(fixture, admin, "ObjectDenied", "part.part", objectId, action, "deny");
            using var denied = await Send(fixture, ordinary, "workspace.folder.paste", "paste", new { mode = "copy", sourcePosition = a, linkIds = sourceIds, targetPosition = c });
            await Problem(denied, action == "read" ? HttpStatusCode.NotFound : HttpStatusCode.Forbidden, action == "read" ? "Workspace.TargetUnavailable" : "Workspace.ObjectCopyDenied");
            await Rule(fixture, admin, "ObjectDenied", "part.part", objectId, action, "allow");
        }
        await Rule(fixture, admin, "TargetReadOnly", "workspace.folder", FolderId(c, await Rows(fixture, "WorkspaceFolderContent")), "edit", "deny");
        using (var targetDenied = await Send(fixture, ordinary, "workspace.folder.paste", "paste", new { mode = "copy", sourcePosition = a, linkIds = sourceIds, targetPosition = c }))
            await Problem(targetDenied, HttpStatusCode.Forbidden, "Workspace.ParentWriteDenied");
        await Rule(fixture, admin, "AncestorHidden", "workspace.folder", root.RootFolderId, "read", "deny");
        using (var hidden = await Send(fixture, ordinary, "workspace.folder.copy", "clipboard/validate", new { mode = "copy", sourcePosition = a, linkIds = sourceIds }))
            await Problem(hidden, HttpStatusCode.NotFound, "Workspace.PositionUnavailable");
        using var missingFeature = await FeatureHttpTestRequests.SendAsync(ordinary, Point(fixture, "workspace.spaces.access"), HttpMethod.Post,
            "/api/workspace/content/clipboard/validate", new { mode = "copy", sourcePosition = a, linkIds = sourceIds });
        Assert.Equal(HttpStatusCode.Forbidden, missingFeature.StatusCode);
        Assert.Equal(2, (await Rows(fixture, "WorkspaceFolderContent")).Count(row => Equals(row["ChildModelKey"], "Part")));
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "批量按snapshot文件夹相对次序追加，重复、同名、防环及内部冲突整批拒绝")]
    public async Task Paste_OrdersFoldersAndRejectsEveryBatchConflictAtomically()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync(); var root = await Root(fixture, admin);
        var a = await Folder(fixture, admin, root, "A"); var b = await Folder(fixture, admin, root, "B");
        var x = await Folder(fixture, admin, a, "X"); var y = await Folder(fixture, admin, a, "Y");
        await Command(fixture, admin, "workspace.folder.createPart", "/api/workspace/content/parts", new { position = a, values = new { ECODE = "BATCH", ENAME = "列表对象" } });
        var part = Assert.Single(await Rows(fixture, "WorkspaceFolderContent"), row => Equals(row["ChildModelKey"], "Part"));
        var ids = new[] { y.LinkIds[^1], (string)part["LID"]!, x.LinkIds[^1] };
        await Command(fixture, admin, "workspace.folder.paste", "/api/workspace/content/paste", new { mode = "copy", sourcePosition = a, linkIds = ids, targetPosition = b });
        var all = await Rows(fixture, "WorkspaceFolderContent"); var bId = FolderId(b, all);
        Assert.Equal(new[] { FolderId(y, all), FolderId(x, all) }, all.Where(row => Equals(row["PEID"], bId) && Equals(row["ChildModelKey"], "WorkspaceFolder")).OrderBy(row => row["TreeOrdinal"]).Select(row => (string)row["CEID"]!));
        Assert.Null(Assert.Single(all, row => Equals(row["PEID"], bId) && Equals(row["ChildModelKey"], "Part"))["TreeOrdinal"]);
        var before = Snapshot(all);
        foreach (var target in new[] { a, b })
        {
            using var duplicate = await Send(fixture, admin, "workspace.folder.paste", "paste", new { mode = "copy", sourcePosition = a, linkIds = ids, targetPosition = target });
            await Problem(duplicate, HttpStatusCode.Conflict, "Workspace.DuplicateReference"); Assert.Equal(before, Snapshot(await Rows(fixture, "WorkspaceFolderContent")));
        }
        using (var rootMixed = await Send(fixture, admin, "workspace.folder.paste", "paste", new { mode = "copy", sourcePosition = a, linkIds = ids, targetPosition = root }))
            await Problem(rootMixed, HttpStatusCode.BadRequest, "Workspace.RootOnlyFolders");
        using (var cycle = await Send(fixture, admin, "workspace.folder.paste", "paste", new { mode = "copy", sourcePosition = root, linkIds = new[] { a.LinkIds[0] }, targetPosition = x }))
            await Problem(cycle, HttpStatusCode.Conflict, "Workspace.CycleForbidden");
        var targetC = await Folder(fixture, admin, root, "C"); await Folder(fixture, admin, targetC, "X");
        before = Snapshot(await Rows(fixture, "WorkspaceFolderContent"));
        using (var sameName = await Send(fixture, admin, "workspace.folder.paste", "paste", new { mode = "copy", sourcePosition = a, linkIds = ids, targetPosition = targetC }))
            await Problem(sameName, HttpStatusCode.Conflict, "Workspace.SiblingNameConflict");
        Assert.Equal(before, Snapshot(await Rows(fixture, "WorkspaceFolderContent")));
        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
            await db.Set<Dictionary<string, object?>>("WorkspaceFolder").Where(row => EF.Property<string>(row, "EID") == FolderId(y, all))
                .ExecuteUpdateAsync(set => set.SetProperty(row => EF.Property<string>(row, "ENAME"), "x"));
        }
        using var internalConflict = await Send(fixture, admin, "workspace.folder.paste", "paste", new { mode = "copy", sourcePosition = a, linkIds = ids, targetPosition = root });
        // 业务项根准入先于名称判断；普通目标独立证明批内名称比较沿SQL collation。
        await Problem(internalConflict, HttpStatusCode.BadRequest, "Workspace.RootOnlyFolders");
        using var internalNames = await Send(fixture, admin, "workspace.folder.paste", "paste", new { mode = "copy", sourcePosition = a, linkIds = new[] { x.LinkIds[^1], y.LinkIds[^1] }, targetPosition = targetC });
        await Problem(internalNames, HttpStatusCode.Conflict, "Workspace.SiblingNameConflict");
        Assert.Equal(before, Snapshot(await Rows(fixture, "WorkspaceFolderContent")));
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "公共和个人根的尾空格/大小写别名在共同精确位置入口拒绝，不得绕过根落入或上传预检")]
    public async Task RootIdentity_AliasesCannotBypassRootIngressThroughExactPositions()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        var root = await Root(fixture, admin);
        var mine = await Command(fixture, admin, "workspace.spaces.access", "/api/workspace/spaces/personal/ensure");
        var personal = new WorkspacePosition(mine.GetProperty("spaceId").GetString()!, mine.GetProperty("rootFolderId").GetString()!, []);
        var source = await Folder(fixture, admin, root, "根身份来源");
        await Command(fixture, admin, "workspace.folder.createPart", "/api/workspace/content/parts",
            new { position = source, values = new { ECODE = "ROOT-IDENTITY-SOURCE", ENAME = "普通目录仍可创建" } });
        var links = await Rows(fixture, "WorkspaceFolderContent");
        var part = Assert.Single(links, row => Equals(row["ChildModelKey"], "Part"));
        var facts = new[] { "WorkspaceFolder", "WorkspaceFolderContent", "Part", "Document" };
        var before = new Dictionary<string, string>(); foreach (var model in facts) before[model] = Snapshot(await Rows(fixture, model));
        var canonicalRoots = await FeatureHttpTestRequests.ReadAsync(admin, Point(fixture, "workspace.spaces.access"), "/api/trees/workspace.structure/roots?scope=currentUser");
        Assert.Equal(2, canonicalRoots.GetArrayLength());
        Assert.All(canonicalRoots.EnumerateArray(), node =>
        {
            var position = ICP.Platform.Crud.Runtime.LinkTreeNodeKey.Decode(node.GetProperty("nodeKey").GetString()!);
            Assert.Equal(position.RootObjectId, node.GetProperty("context").GetProperty("rootFolderId").GetString());
        });
        var search = await FeatureHttpTestRequests.ReadAsync(admin, Point(fixture, "workspace.spaces.access"), "/api/trees/workspace.structure/search?scope=currentUser&keyword=" + Uri.EscapeDataString("根身份来源"));
        var found = ICP.Platform.Crud.Runtime.LinkTreeNodeKey.Decode(Assert.Single(search.GetProperty("items").EnumerateArray()).GetProperty("node").GetProperty("nodeKey").GetString()!);
        Assert.Equal(root.SpaceId, found.RootKey); Assert.Equal(root.RootFolderId, found.RootObjectId);
        foreach (var position in new[] { root, personal })
        {
            foreach (var (url, _) in ReadRequests(position))
            {
                using var canonical = await FeatureHttpTestRequests.SendAsync(admin, Point(fixture, "workspace.spaces.access"), HttpMethod.Get, url);
                Assert.Equal(HttpStatusCode.OK, canonical.StatusCode);
            }
            Assert.NotEqual(position.RootFolderId, position.RootFolderId.ToUpperInvariant());
            Assert.NotEqual(position.SpaceId, position.SpaceId.ToUpperInvariant());
            foreach (var alias in new[] { position with { RootFolderId = position.RootFolderId + " " }, position with { RootFolderId = position.RootFolderId.ToUpperInvariant() },
                position with { SpaceId = position.SpaceId + " " }, position with { SpaceId = position.SpaceId.ToUpperInvariant() } })
            {
                foreach (var (url, code) in ReadRequests(alias))
                {
                    using var read = await FeatureHttpTestRequests.SendAsync(admin, Point(fixture, "workspace.spaces.access"), HttpMethod.Get, url);
                    await Problem(read, HttpStatusCode.NotFound, code);
                }
                foreach (var (kind, action) in new[] { ("parts", "createPart"), ("documents", "createDocument") })
                {
                    using var created = await FeatureHttpTestRequests.SendAsync(admin, Point(fixture, "workspace.folder." + action), HttpMethod.Post,
                        "/api/workspace/content/" + kind, new { position = alias, values = new { ECODE = "ROOT-ALIAS", ENAME = "根身份别名禁止落入" } });
                    await Problem(created, HttpStatusCode.NotFound, "Workspace.PositionUnavailable");
                }
                using (var added = await FeatureHttpTestRequests.SendAsync(admin, Point(fixture, "workspace.folder.add"), HttpMethod.Post,
                    "/api/workspace/content/references", new { position = alias, target = new { modelKey = "Part", id = part["CEID"] } }))
                    await Problem(added, HttpStatusCode.NotFound, "Workspace.PositionUnavailable");
                foreach (var mode in new[] { "copy", "cut" })
                {
                    using var paste = await Send(fixture, admin, "workspace.folder.paste", "paste", new { mode, sourcePosition = source, linkIds = new[] { (string)part["LID"]! }, targetPosition = alias });
                    await Problem(paste, HttpStatusCode.NotFound, "Workspace.PositionUnavailable");
                }
                using var multipart = new MultipartFormDataContent();
                multipart.Add(new StringContent(JsonSerializer.Serialize(alias, new JsonSerializerOptions(JsonSerializerDefaults.Web))), "position");
                multipart.Add(new StringContent("{\"ECODE\":\"ROOT-ALIAS-UPLOAD\",\"ENAME\":\"根别名上传\"}"), "values");
                multipart.Add(new ByteArrayContent([1, 2, 3]), "file", "alias.pdf");
                using var request = new HttpRequestMessage(HttpMethod.Post, "/api/workspace/content/document-uploads") { Content = multipart };
                request.Headers.Add("X-NewP-Feature", Point(fixture, "workspace.folder.createDocument").Position);
                using var upload = await admin.SendAsync(request);
                await Problem(upload, HttpStatusCode.NotFound, "Workspace.PositionUnavailable");
            }
        }
        foreach (var model in facts) Assert.Equal(before[model], Snapshot(await Rows(fixture, model)));
        Assert.Empty(await Rows(fixture, "FileRecord")); Assert.Empty(await Rows(fixture, "FileUploadLease")); Assert.Empty(Directory.GetFiles(fixture.StorageRoot));

        static (string Url, string Code)[] ReadRequests(WorkspacePosition position)
        {
            var key = Uri.EscapeDataString(ICP.Platform.Crud.Runtime.LinkTreeNodeKey.Encode(new(position.SpaceId, position.RootFolderId, position.LinkIds)));
            var query = "?spaceId=" + Uri.EscapeDataString(position.SpaceId) + "&rootFolderId=" + Uri.EscapeDataString(position.RootFolderId) + "&linkIds=%5B%5D";
            return [("/api/trees/workspace.structure/path?scope=currentUser&nodeKey=" + key, "Platform.TreePositionUnavailable"),
                ("/api/trees/workspace.structure/children?scope=currentUser&nodeKey=" + key, "Platform.TreePositionUnavailable"),
                ("/api/workspace/content/path" + query, "Workspace.PositionUnavailable"), ("/api/workspace/content/children" + query, "Workspace.PositionUnavailable")];
        }
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "空间根在新建、直接add及上传I/O前拒绝业务对象，独立对象创建仍可用")]
    public async Task Root_RejectsBusinessCreationReferenceAndUploadBeforeFileIo()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        var root = await Command(fixture, admin, "workspace.spaces.access", "/api/workspace/spaces/public/initialize");
        var position = new WorkspacePosition(root.GetProperty("spaceId").GetString()!, root.GetProperty("rootFolderId").GetString()!, []);
        foreach (var (kind, action) in new[] { ("parts", "createPart"), ("documents", "createDocument") })
        {
            using var response = await FeatureHttpTestRequests.SendAsync(admin, Point(fixture, "workspace.folder." + action), HttpMethod.Post,
                "/api/workspace/content/" + kind, new { position, values = new { ECODE = "ROOT-" + kind, ENAME = "根禁止落入" } });
            await Problem(response, HttpStatusCode.BadRequest, "Workspace.RootOnlyFolders");
        }
        using var independent = await FeatureHttpTestRequests.SendAsync(admin, Assert.Single(fixture.FeatureCatalog, point => point.BehaviorKey == "part.part.create"),
            HttpMethod.Post, "/api/part/parts", new { ECODE = "DIRECT", ENAME = "独立对象" });
        Assert.Equal(HttpStatusCode.Created, independent.StatusCode);
        var id = (await independent.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("fields").GetProperty("EID").GetString();
        using var added = await FeatureHttpTestRequests.SendAsync(admin, Point(fixture, "workspace.folder.add"), HttpMethod.Post,
            "/api/workspace/content/references", new { position, target = new { modelKey = "Part", id } });
        await Problem(added, HttpStatusCode.BadRequest, "Workspace.RootOnlyFolders");
        using var multipart = new MultipartFormDataContent();
        multipart.Add(new StringContent(JsonSerializer.Serialize(position, new JsonSerializerOptions(JsonSerializerDefaults.Web))), "position");
        multipart.Add(new StringContent("{\"ECODE\":\"ROOT-UPLOAD\",\"ENAME\":\"根上传\"}"), "values");
        multipart.Add(new ByteArrayContent([1, 2, 3]), "file", "root.pdf");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/workspace/content/document-uploads") { Content = multipart };
        request.Headers.Add("X-NewP-Feature", Point(fixture, "workspace.folder.createDocument").Position);
        using var upload = await admin.SendAsync(request);
        await Problem(upload, HttpStatusCode.BadRequest, "Workspace.RootOnlyFolders");
        Assert.Empty(await Rows(fixture, "FileRecord")); Assert.Empty(await Rows(fixture, "FileUploadLease"));
        Assert.Empty(Directory.GetFiles(fixture.StorageRoot)); Assert.Empty(await Rows(fixture, "WorkspaceFolderContent"));
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "真实主文件及附件图文档A→B→C只新增引用，保留版本、关联、Files使用和字节")]
    public async Task CopyCutAndRemoveDocumentReferences_PreserveAllObjectAndFileFacts()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        var root = await Command(fixture, admin, "workspace.spaces.access", "/api/workspace/spaces/public/initialize");
        var rootPosition = new WorkspacePosition(root.GetProperty("spaceId").GetString()!, root.GetProperty("rootFolderId").GetString()!, []);
        var a = await Folder(fixture, admin, rootPosition, "A");
        var b = await Folder(fixture, admin, rootPosition, "B");
        var c = await Folder(fixture, admin, rootPosition, "C");
        var values = new { ECODE = "COPY-FILES", ENAME = "主附件引用" };
        var uploadIds = new List<string>();
        for (var index = 0; index < 3; index++)
        {
            using var multipart = new MultipartFormDataContent();
            multipart.Add(new StringContent(JsonSerializer.Serialize(a, new JsonSerializerOptions(JsonSerializerDefaults.Web))), "position");
            multipart.Add(new StringContent(JsonSerializer.Serialize(values)), "values");
            var file = new ByteArrayContent([(byte)index, (byte)(index + 1)]);
            file.Headers.ContentType = new("application/pdf"); multipart.Add(file, "file", $"copy-{index}.pdf");
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/workspace/content/document-uploads") { Content = multipart };
            request.Headers.Add("X-NewP-Feature", Point(fixture, "workspace.folder.createDocument").Position);
            using var response = await admin.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            uploadIds.Add((await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("uploadId").GetString()!);
        }
        await Command(fixture, admin, "workspace.folder.createDocument", "/api/workspace/content/documents",
            new { position = a, values, uploads = new { mainUploadId = uploadIds[0], attachmentUploadIds = uploadIds.Skip(1).ToArray() } });
        var links = await Rows(fixture, "WorkspaceFolderContent");
        var source = Assert.Single(links, row => Equals(row["PEID"], FolderId(a, links)));
        var sourceId = (string)source["LID"]!;
        var models = new[] { "Document", "DocumentVersion", "DocumentIteration", "DocumentFileLink", "FileUsage", "FileUploadLease", "FileRecord" };
        var before = new Dictionary<string, string>();
        foreach (var model in models) before[model] = Snapshot(await Rows(fixture, model));
        var bytesBefore = Directory.GetFiles(fixture.StorageRoot).ToDictionary(path => Path.GetFileName(path)!, File.ReadAllBytes);
        var validated = await Command(fixture, admin, "workspace.folder.copy", "/api/workspace/content/clipboard/validate",
            new { mode = "copy", sourcePosition = a, linkIds = new[] { sourceId } });
        Assert.Equal(sourceId, Assert.Single(validated.GetProperty("items").EnumerateArray()).GetProperty("linkId").GetString());
        foreach (var target in new[] { b, c })
            await Command(fixture, admin, "workspace.folder.paste", "/api/workspace/content/paste",
                new { mode = "copy", sourcePosition = a, linkIds = new[] { sourceId }, targetPosition = target });
        var references = (await Rows(fixture, "WorkspaceFolderContent")).Where(row => Equals(row["ChildModelKey"], "Document")).ToArray();
        Assert.Equal(3, references.Length); Assert.Equal(3, references.Select(row => row["LID"]).Distinct().Count());
        Assert.Contains(references, row => Equals(row["LID"], sourceId)); Assert.All(references, row => Assert.Equal(source["CEID"], row["CEID"]));
        Assert.All(references, row => Assert.Null(row["TreeOrdinal"]));
        foreach (var model in models) Assert.Equal(before[model], Snapshot(await Rows(fixture, model)));
        Assert.Equal(bytesBefore.Keys.Order(), Directory.GetFiles(fixture.StorageRoot).Select(Path.GetFileName).Order());
        foreach (var (name, bytes) in bytesBefore) Assert.Equal(bytes, await File.ReadAllBytesAsync(Path.Combine(fixture.StorageRoot, name!)));
        // 当前主附件Document的legacy根位置仍可读取/下载；只改准备关系，不改主键或版本/使用形状。
        using (var scope = fixture.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>().Set<Dictionary<string, object?>>("WorkspaceFolderContent")
                .Where(row => EF.Property<string>(row, "LID") == sourceId).ExecuteUpdateAsync(set => set.SetProperty(row => EF.Property<string>(row, "PEID"), rootPosition.RootFolderId));
        var documentId = (string)source["CEID"]!;
        var fileList = await FeatureHttpTestRequests.ReadAsync(admin, DocumentFilesSqlServerHttpApiTests.Point(fixture, "document.document.read"), "/api/document/documents/" + documentId + "/files");
        foreach (var file in fileList.GetProperty("files").EnumerateArray())
        {
            using var downloaded = await FeatureHttpTestRequests.SendAsync(admin, DocumentFilesSqlServerHttpApiTests.Point(fixture, "document.document.download"), HttpMethod.Get,
                "/api/document/documents/" + documentId + "/files/" + file.GetProperty("fileLinkId").GetString() + "/content");
            Assert.Equal(HttpStatusCode.OK, downloaded.StatusCode);
            var ordinal = file.GetProperty("ordinal").GetInt32(); Assert.Equal(new byte[] { (byte)ordinal, (byte)(ordinal + 1) }, await downloaded.Content.ReadAsByteArrayAsync());
        }
        foreach (var model in models) Assert.Equal(before[model], Snapshot(await Rows(fixture, model)));
        // 同父legacy根Document剪切也必须零写；随后移动及移除最后引用保留全部文件链。
        var legacyBefore = Snapshot(await Rows(fixture, "WorkspaceFolderContent"));
        var noOp = await Command(fixture, admin, "workspace.folder.paste", "/api/workspace/content/paste", new { mode = "cut", sourcePosition = rootPosition, linkIds = new[] { sourceId }, targetPosition = rootPosition });
        Assert.True(noOp.GetProperty("noChange").GetBoolean()); Assert.Equal(legacyBefore, Snapshot(await Rows(fixture, "WorkspaceFolderContent")));
        foreach (var position in new[] { b, c })
        {
            links = await Rows(fixture, "WorkspaceFolderContent");
            var id = (string)links.Single(row => Equals(row["PEID"], FolderId(position, links)) && Equals(row["ChildModelKey"], "Document"))["LID"]!;
            await Command(fixture, admin, "workspace.folder.removeSelected", "/api/workspace/content/references/remove", new { position, linkIds = new[] { id } });
        }
        await Command(fixture, admin, "workspace.folder.cut", "/api/workspace/content/clipboard/cut/validate", new { mode = "cut", sourcePosition = rootPosition, linkIds = new[] { sourceId } });
        await Command(fixture, admin, "workspace.folder.paste", "/api/workspace/content/paste", new { mode = "cut", sourcePosition = rootPosition, linkIds = new[] { sourceId }, targetPosition = b });
        links = await Rows(fixture, "WorkspaceFolderContent");
        var movedDocument = Assert.Single(links, row => Equals(row["ChildModelKey"], "Document"));
        Assert.NotEqual(sourceId, movedDocument["LID"]); Assert.Equal(documentId, movedDocument["CEID"]);
        Assert.Equal(FolderId(b, links), movedDocument["PEID"]);
        await Command(fixture, admin, "workspace.folder.removeSelected", "/api/workspace/content/references/remove", new { position = b, linkIds = new[] { (string)movedDocument["LID"]! } });
        Assert.DoesNotContain(await Rows(fixture, "WorkspaceFolderContent"), row => Equals(row["ChildModelKey"], "Document"));
        foreach (var model in models) Assert.Equal(before[model], Snapshot(await Rows(fixture, model)));
        foreach (var (name, bytes) in bytesBefore) Assert.Equal(bytes, await File.ReadAllBytesAsync(Path.Combine(fixture.StorageRoot, name!)));
    }

    private static string Snapshot(Dictionary<string, object?>[] rows) => JsonSerializer.Serialize(rows.Select(row => row.OrderBy(pair => pair.Key).ToDictionary()).OrderBy(row => JsonSerializer.Serialize(row)));
    private static string FolderId(WorkspacePosition position, Dictionary<string, object?>[] links) => position.LinkIds.Count == 0 ? position.RootFolderId : (string)links.Single(row => Equals(row["LID"], position.LinkIds[^1]))["CEID"]!;
    private static async Task<WorkspacePosition> Folder(BusinessAuditHostFixture fixture, HttpClient client, WorkspacePosition parent, string name)
    {
        var result = await Command(fixture, client, "workspace.folder.create", "/api/workspace/content/folders", new { position = parent, values = new { ENAME = name } });
        var id = result.GetProperty("id").GetString();
        var link = Assert.Single(await Rows(fixture, "WorkspaceFolderContent"), row => Equals(row["CEID"], id));
        return parent with { LinkIds = [.. parent.LinkIds, (string)link["LID"]!] };
    }
    private static FeaturePoint Point(BusinessAuditHostFixture fixture, string behavior) => Assert.Single(fixture.FeatureCatalog,
        point => point.ModuleId == (behavior.StartsWith("document.", StringComparison.Ordinal) ? "Document" : "Workspace")
            && point.BehaviorKey == behavior && (behavior != "workspace.folder.copy" || point.Key == "copySelected")
            && (behavior != "workspace.folder.cut" || point.Key == "cutSelected") && (behavior == "workspace.spaces.access" || point.ActionScope == "list.pageActions" || behavior == "workspace.folder.add" && point.ActionScope == "list.custom"));
    private static FeaturePoint RowPoint(BusinessAuditHostFixture fixture, string key) => Assert.Single(fixture.FeatureCatalog,
        point => point.ModuleId == "Workspace" && point.StaticScope == "folder" && point.ActionScope == "list.rowActions" && point.Key == key);
    private static async Task<JsonElement> Command(BusinessAuditHostFixture fixture, HttpClient client, string behavior, string path, object? body = null)
    {
        using var response = await FeatureHttpTestRequests.SendAsync(client, Point(fixture, behavior), HttpMethod.Post, path, body);
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
    private static async Task<WorkspacePosition> Root(BusinessAuditHostFixture fixture, HttpClient admin)
    {
        var root = await Command(fixture, admin, "workspace.spaces.access", "/api/workspace/spaces/public/initialize");
        return new(root.GetProperty("spaceId").GetString()!, root.GetProperty("rootFolderId").GetString()!, []);
    }
    private static Task<HttpResponseMessage> Send(BusinessAuditHostFixture fixture, HttpClient client, string behavior, string path, object body) =>
        FeatureHttpTestRequests.SendAsync(client, Point(fixture, behavior), HttpMethod.Post, "/api/workspace/content/" + path, body);
    private static async Task Rule(BusinessAuditHostFixture fixture, HttpClient admin, string key, string resource, string id, string action, string opinion)
    {
        var rule = new DataScopeRuleDto(key, key, null, key, true, resource, new("attribute", "EID", "equal", JsonSerializer.SerializeToElement(id)),
            [new(key + "Subject", "all", null, 0, new Dictionary<string, string> { [action] = opinion })], []);
        using var response = await FeatureHttpTestRequests.SendAsync(admin, Assert.Single(fixture.FeatureCatalog, point => point.BehaviorKey == "authorization.dataScope.save"),
            HttpMethod.Put, "/api/authorization/data-scope/rules/" + key, rule);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }
    private static async Task<Dictionary<string, object?>[]> Rows(BusinessAuditHostFixture fixture, string model)
    {
        using var scope = fixture.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>().Set<Dictionary<string, object?>>(model).AsNoTracking().ToArrayAsync();
    }
    private static async Task Problem(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal(code, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }
}
