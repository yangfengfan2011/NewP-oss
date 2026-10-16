using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ICP.Platform.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Data.Common;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed partial class WorkspaceClipboardSqlServerHttpApiTests
{
    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "浅副本经正式动作一次创建新根和完整直接引用，源与业务对象保持原身份")]
    public async Task ShallowCopy_CreatesNamedFolderAndSharesEveryDirectObject()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync(); var root = await Root(fixture, admin);
        var source = await Folder(fixture, admin, root, "来源"); var target = await Folder(fixture, admin, root, "目标");
        using (var edited = await FeatureHttpTestRequests.SendAsync(admin, RowPoint(fixture, "edit"), HttpMethod.Put,
            "/api/workspace/content/" + source.LinkIds[^1], new { position = root, values = new { ENAME = "来源", Description = "只复制说明白名单" } })) Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        var firstChild = await Folder(fixture, admin, source, "Z-first");
        await Folder(fixture, admin, source, "A-second");
        await Folder(fixture, admin, firstChild, "不递归复制的孙文件夹");
        await Command(fixture, admin, "workspace.folder.createPart", "/api/workspace/content/parts", new { position = source, values = new { ECODE = "SHALLOW-PART", ENAME = "共享零组件" } });
        var beforeFolders = await Rows(fixture, "WorkspaceFolder"); var beforeLinks = await Rows(fixture, "WorkspaceFolderContent");
        var parts = Snapshot(await Rows(fixture, "Part"));
        var prepared = await Command(fixture, admin, "workspace.folder.pasteFolderCopy", "/api/workspace/content/folders/copy/prepare", new {
            mode = "copy", sourcePosition = root, linkIds = new[] { source.LinkIds[^1] }, targetPosition = target });
        Assert.Equal("来源", prepared.GetProperty("sourceName").GetString());
        Assert.Equal(new[] { "公共空间", "来源" }, prepared.GetProperty("sourcePath").EnumerateArray().Select(item => item.GetString()));
        Assert.Equal(new[] { "公共空间", "目标" }, prepared.GetProperty("targetPath").EnumerateArray().Select(item => item.GetString()));
        Assert.Equal(3, prepared.EnumerateObject().Count());
        Assert.Equal(Snapshot(beforeFolders), Snapshot(await Rows(fixture, "WorkspaceFolder"))); Assert.Equal(Snapshot(beforeLinks), Snapshot(await Rows(fixture, "WorkspaceFolderContent")));
        var result = await Command(fixture, admin, "workspace.folder.pasteFolderCopy", "/api/workspace/content/folders/copy", new {
            mode = "copy", sourcePosition = root, linkIds = new[] { source.LinkIds[^1] }, targetPosition = target, name = "新副本" });
        Assert.True(result.GetProperty("succeeded").GetBoolean());
        var folders = await Rows(fixture, "WorkspaceFolder"); var links = await Rows(fixture, "WorkspaceFolderContent");
        var created = Assert.Single(folders, row => Equals(row["ENAME"], "新副本"));
        Assert.Equal("只复制说明白名单", created["Description"]); Assert.Equal(fixture.ActorId, created["CreatedBy"]);
        Assert.Equal(beforeFolders.Length + 1, folders.Length); Assert.Equal(root.SpaceId, created["OriginSpaceId"]);
        var sourceId = FolderId(source, beforeLinks); var copyId = (string)created["EID"]!;
        Assert.Equal(beforeLinks.Length + 4, links.Length);
        Assert.Equal(beforeLinks.Where(row => Equals(row["PEID"], sourceId)).Select(row => row["CEID"]).Order(),
            links.Where(row => Equals(row["PEID"], copyId)).Select(row => row["CEID"]).Order());
        Assert.Equal(beforeLinks.Where(row => Equals(row["PEID"], sourceId) && Equals(row["ChildModelKey"], "WorkspaceFolder")).OrderBy(row => row["TreeOrdinal"]).Select(row => row["CEID"]),
            links.Where(row => Equals(row["PEID"], copyId) && Equals(row["ChildModelKey"], "WorkspaceFolder")).OrderBy(row => row["TreeOrdinal"]).Select(row => row["CEID"]));
        foreach (var old in beforeFolders) Assert.Contains(folders, row => Snapshot([row]) == Snapshot([old]));
        foreach (var old in beforeLinks) Assert.Contains(links, row => Snapshot([row]) == Snapshot([old]));
        Assert.Equal(parts, Snapshot(await Rows(fixture, "Part")));
        Assert.Single(await Rows(fixture, "BusinessChange"), row => Equals(row["EventType"], "workspace.folder.shallowCopied"));
    }
}

public sealed partial class WorkspaceClipboardSqlServerHttpApiTests
{
    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "浅副本拒绝模式、多源、伪造、非Folder、失效路径及创建名称，空目录和同父新名可用")]
    public async Task ShallowCopy_RejectsInvalidInputsAndAllowsEmptySameParentCopy()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync(); var root = await Root(fixture, admin);
        var source = await Folder(fixture, admin, root, "原目录"); var target = await Folder(fixture, admin, root, "目标");
        await Command(fixture, admin, "workspace.folder.createPart", "/api/workspace/content/parts", new { position = target, values = new { ECODE = "SHALLOW-INVALID", ENAME = "非文件夹" } });
        var links = await Rows(fixture, "WorkspaceFolderContent"); var sourceLid = source.LinkIds[^1];
        var part = Assert.Single(links, row => Equals(row["ChildModelKey"], "Part"));
        var objects = Snapshot(await Rows(fixture, "WorkspaceFolder")); var before = Snapshot(links);
        object[] invalid = [
            new { mode = "cut", sourcePosition = root, linkIds = new[] { sourceLid }, targetPosition = target, name = "无效" },
            new { mode = "copy", sourcePosition = root, linkIds = Array.Empty<string>(), targetPosition = target, name = "无效" },
            new { mode = "copy", sourcePosition = root, linkIds = new[] { sourceLid, target.LinkIds[^1] }, targetPosition = target, name = "无效" },
            new { mode = "copy", sourcePosition = root, linkIds = new[] { sourceLid }, targetPosition = target, name = "无效", actor = "forged" },
            new { mode = "copy", sourcePosition = target, linkIds = new[] { (string)part["LID"]! }, targetPosition = target, name = "无效" },
            new { mode = "copy", sourcePosition = target, linkIds = new[] { sourceLid }, targetPosition = target, name = "无效" },
            new { mode = "copy", sourcePosition = root, linkIds = new[] { sourceLid }, targetPosition = target, name = " " },
            new { mode = "copy", sourcePosition = root, linkIds = new[] { sourceLid }, targetPosition = target, name = new string('x', 257) } ];
        foreach (var input in invalid)
        {
            using var response = await Send(fixture, admin, "workspace.folder.pasteFolderCopy", "folders/copy", input);
            Assert.InRange((int)response.StatusCode, 400, 499);
            Assert.Equal(objects, Snapshot(await Rows(fixture, "WorkspaceFolder"))); Assert.Equal(before, Snapshot(await Rows(fixture, "WorkspaceFolderContent")));
        }
        await Command(fixture, admin, "workspace.folder.pasteFolderCopy", "/api/workspace/content/folders/copy", new { mode = "copy", sourcePosition = root, linkIds = new[] { sourceLid }, targetPosition = root, name = "同父空副本" });
        Assert.Single(await Rows(fixture, "WorkspaceFolder"), row => Equals(row["ENAME"], "同父空副本"));
        using var conflict = await Send(fixture, admin, "workspace.folder.pasteFolderCopy", "folders/copy", new { mode = "copy", sourcePosition = root, linkIds = new[] { sourceLid }, targetPosition = root, name = "原目录" });
        await Problem(conflict, HttpStatusCode.Conflict, "Workspace.SiblingNameConflict");
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "浅副本源read/copy、直接端点read/copy、目标edit、新候选create交集且失败无残留")]
    public async Task ShallowCopy_RechecksEveryPermissionWithoutRequiringSourceEdit()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync(); var root = await Root(fixture, admin);
        var source = await Folder(fixture, admin, root, "来源"); var target = await Folder(fixture, admin, root, "目标");
        await Command(fixture, admin, "workspace.folder.createPart", "/api/workspace/content/parts", new { position = source, values = new { ECODE = "SHALLOW-SECURITY", ENAME = "隐藏零组件" } });
        var childFolder = await Folder(fixture, admin, source, "受控子文件夹");
        await CreateShallowDocumentAsync(fixture, admin, source, "SHALLOW-SECURITY-DOC");
        var links = await Rows(fixture, "WorkspaceFolderContent"); var sourceId = FolderId(source, links); var targetId = FolderId(target, links);
        var partId = (string)Assert.Single(links, row => Equals(row["ChildModelKey"], "Part"))["CEID"]!;
        var documentId = (string)Assert.Single(links, row => Equals(row["ChildModelKey"], "Document"))["CEID"]!;
        var childFolderId = FolderId(childFolder, links);
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "ShallowActor", "副本账号", "shallow-actor", "副本账号", Point(fixture, "workspace.spaces.access"), Point(fixture, "workspace.folder.copy"), Point(fixture, "workspace.folder.pasteFolderCopy"));
        using var ordinary = actor.Client;
        await Rule(fixture, admin, "ShallowSourceEdit", "workspace.folder", sourceId, "edit", "deny");
        await Rule(fixture, admin, "ShallowParentEdit", "workspace.folder", root.RootFolderId, "edit", "deny");
        var input = new { mode = "copy", sourcePosition = root, linkIds = new[] { source.LinkIds[^1] }, targetPosition = target, name = "权限成功" };
        await Command(fixture, ordinary, "workspace.folder.copy", "/api/workspace/content/clipboard/validate", new { mode = "copy", sourcePosition = root, linkIds = input.linkIds });
        await Command(fixture, ordinary, "workspace.folder.pasteFolderCopy", "/api/workspace/content/folders/copy/prepare", new { input.mode, input.sourcePosition, input.linkIds, input.targetPosition });
        foreach (var (resource, id, action) in new[] { ("workspace.folder", sourceId, "workspaceCopy"), ("workspace.folder", sourceId, "read"),
            ("part.part", partId, "partCopy"), ("part.part", partId, "read"),
            ("workspace.folder", childFolderId, "workspaceCopy"), ("workspace.folder", childFolderId, "read"),
            ("document.document", documentId, "documentCopy"), ("document.document", documentId, "read"), ("workspace.folder", targetId, "edit") })
        {
            var foldersBefore = Snapshot(await Rows(fixture, "WorkspaceFolder")); var linksBefore = Snapshot(await Rows(fixture, "WorkspaceFolderContent"));
            await Rule(fixture, admin, "ShallowDenied", resource, id, action, "deny");
            using var denied = await Send(fixture, ordinary, "workspace.folder.pasteFolderCopy", "folders/copy", input);
            Assert.InRange((int)denied.StatusCode, 400, 499); Assert.DoesNotContain("隐藏零组件", await denied.Content.ReadAsStringAsync());
            Assert.Equal(foldersBefore, Snapshot(await Rows(fixture, "WorkspaceFolder"))); Assert.Equal(linksBefore, Snapshot(await Rows(fixture, "WorkspaceFolderContent")));
            await Rule(fixture, admin, "ShallowDenied", resource, id, action, "allow");
        }
        await Command(fixture, ordinary, "workspace.folder.pasteFolderCopy", "/api/workspace/content/folders/copy", input);
        Assert.Single(await Rows(fixture, "BusinessChange"), row => Equals(row["EventType"], "workspace.folder.shallowCopied") && !Equals(row["ActorId"], fixture.ActorId));
    }

    private static async Task CreateShallowDocumentAsync(BusinessAuditHostFixture fixture, HttpClient client, ICP.Platform.Modules.Workspace.WorkspacePosition position, string code)
    {
        var values = new { ECODE = code, ENAME = "受控图文档" };
        using var multipart = new MultipartFormDataContent();
        multipart.Add(new StringContent(JsonSerializer.Serialize(position, new JsonSerializerOptions(JsonSerializerDefaults.Web))), "position");
        multipart.Add(new StringContent(JsonSerializer.Serialize(values)), "values");
        var file = new ByteArrayContent([1, 2, 3]); file.Headers.ContentType = new("application/pdf"); multipart.Add(file, "file", "permissions.pdf");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/workspace/content/document-uploads") { Content = multipart };
        request.Headers.Add("X-NewP-Feature", Point(fixture, "workspace.folder.createDocument").Position);
        using var response = await client.SendAsync(request); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var uploadId = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("uploadId").GetString();
        await Command(fixture, client, "workspace.folder.createDocument", "/api/workspace/content/documents", new { position, values, uploads = new { mainUploadId = uploadId, attachmentUploadIds = Array.Empty<string>() } });
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "浅副本按最终图允许自身目标、拒绝后代环；并发同名只提交一个完整结果")]
    public async Task ShallowCopy_UsesFinalGraphAndSerializesSameNameCopies()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync(); var root = await Root(fixture, admin);
        var source = await Folder(fixture, admin, root, "来源"); var child = await Folder(fixture, admin, source, "子目录");
        await Command(fixture, admin, "workspace.folder.pasteFolderCopy", "/api/workspace/content/folders/copy", new { mode = "copy", sourcePosition = root, linkIds = new[] { source.LinkIds[^1] }, targetPosition = source, name = "自身内副本" });
        var folders = Snapshot(await Rows(fixture, "WorkspaceFolder")); var links = Snapshot(await Rows(fixture, "WorkspaceFolderContent"));
        using (var cycle = await Send(fixture, admin, "workspace.folder.pasteFolderCopy", "folders/copy", new { mode = "copy", sourcePosition = root, linkIds = new[] { source.LinkIds[^1] }, targetPosition = child, name = "成环" }))
            await Problem(cycle, HttpStatusCode.Conflict, "Workspace.FolderCopyUnavailable");
        Assert.Equal(folders, Snapshot(await Rows(fixture, "WorkspaceFolder"))); Assert.Equal(links, Snapshot(await Rows(fixture, "WorkspaceFolderContent")));
        var input = new { mode = "copy", sourcePosition = root, linkIds = new[] { child.LinkIds[^1] }, targetPosition = root, name = "并发副本" };
        var responses = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => Send(fixture, admin, "workspace.folder.pasteFolderCopy", "folders/copy", new { input.mode, sourcePosition = source, input.linkIds, input.targetPosition, input.name })));
        try { Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK); Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict); }
        finally { foreach (var response in responses) response.Dispose(); }
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "浅副本创建/关系/业务审计及提交前取消失败全部回滚，来源所有事实保持")]
    public async Task ShallowCopy_RollsBackAuditFailuresAndCancellationAfterSqlWrites()
    {
        var control = new PasteCommitControl(); var writeFault = new FolderCopyWriteFault();
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(services => services.AddDbContext<ICP.Platform.Persistence.EfCore.EfPlatformDbContext>((_, options) => options.AddInterceptors(control, writeFault)), includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync(); var root = await Root(fixture, admin);
        var source = await Folder(fixture, admin, root, "来源"); await Folder(fixture, admin, source, "子目录");
        var input = new { mode = "copy", sourcePosition = root, linkIds = new[] { source.LinkIds[^1] }, targetPosition = root, name = "回滚副本" };
        var folders = Snapshot(await Rows(fixture, "WorkspaceFolder")); var links = Snapshot(await Rows(fixture, "WorkspaceFolderContent")); var changes = Snapshot(await Rows(fixture, "BusinessChange"));
        foreach (var model in new[] { "WorkspaceFolder", "WorkspaceFolderContent" })
        {
            writeFault.Model = model; writeFault.Hits = 0;
            try { using var failed = await Send(fixture, admin, "workspace.folder.pasteFolderCopy", "folders/copy", input); Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode); Assert.Equal(1, writeFault.Hits); }
            finally { writeFault.Model = ""; }
            Assert.Equal(folders, Snapshot(await Rows(fixture, "WorkspaceFolder"))); Assert.Equal(links, Snapshot(await Rows(fixture, "WorkspaceFolderContent"))); Assert.Equal(changes, Snapshot(await Rows(fixture, "BusinessChange")));
        }
        foreach (var mode in new[] { "register", "write", "save", "commit" })
        {
            fixture.Fault.Mode = mode == "commit" ? "" : mode; control.Fail = mode == "commit";
            try { using var failed = await Send(fixture, admin, "workspace.folder.pasteFolderCopy", "folders/copy", input); Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode); }
            finally { fixture.Fault.Mode = ""; control.Fail = false; }
            Assert.Equal(folders, Snapshot(await Rows(fixture, "WorkspaceFolder"))); Assert.Equal(links, Snapshot(await Rows(fixture, "WorkspaceFolderContent"))); Assert.Equal(changes, Snapshot(await Rows(fixture, "BusinessChange")));
        }
        control.Pause = true; using var cancellation = new CancellationTokenSource();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/workspace/content/folders/copy") { Content = JsonContent.Create(input) };
        request.Headers.Add("X-NewP-Feature", Point(fixture, "workspace.folder.pasteFolderCopy").Position);
        var pending = admin.SendAsync(request, cancellation.Token);
        try { await control.Reached.Task.WaitAsync(TimeSpan.FromSeconds(20)); cancellation.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await pending); await control.RolledBack.Task.WaitAsync(TimeSpan.FromSeconds(20)); }
        finally { cancellation.Cancel(); control.Resume.TrySetResult(); try { (await pending).Dispose(); } catch (OperationCanceledException) { } }
        Assert.Equal(folders, Snapshot(await Rows(fixture, "WorkspaceFolder"))); Assert.Equal(links, Snapshot(await Rows(fixture, "WorkspaceFolderContent"))); Assert.Equal(changes, Snapshot(await Rows(fixture, "BusinessChange")));
    }

    private sealed class FolderCopyWriteFault : DbCommandInterceptor
    {
        internal string Model = ""; internal int Hits;
        private void Observe(DbCommand command)
        {
            if (Model.Length > 0 && (command.CommandText.Contains("INSERT INTO", StringComparison.Ordinal) || command.CommandText.Contains("MERGE", StringComparison.Ordinal))
                && command.CommandText.Contains("[" + Model + "]", StringComparison.Ordinal)) { Hits++; throw new InvalidOperationException("Injected folder copy SQL write failure."); }
        }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken token = default) { Observe(command); return ValueTask.FromResult(result); }
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken token = default) { Observe(command); return ValueTask.FromResult(result); }
    }
}

public sealed partial class WorkspaceClipboardSqlServerHttpApiTests
{
    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "浅副本跨空间来源/目标、候选create、提交后不可读及剪贴板来源失效均按当前正式规则判定")]
    public async Task ShallowCopy_RechecksCandidatePrivateSpaceAndExactStalePath()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync(); var root = await Root(fixture, admin);
        var source = await Folder(fixture, admin, root, "公共来源"); var target = await Folder(fixture, admin, root, "公共目标");
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "ShallowScope", "范围账号", "shallow-scope", "范围账号", Point(fixture, "workspace.spaces.access"), Point(fixture, "workspace.folder.copy"), Point(fixture, "workspace.folder.pasteFolderCopy"));
        using var ordinary = actor.Client;
        await Command(fixture, ordinary, "workspace.spaces.access", "/api/workspace/spaces/personal/ensure");
        using var spacesRequest = new HttpRequestMessage(HttpMethod.Get, "/api/workspace/spaces"); spacesRequest.Headers.Add("X-NewP-Feature", Point(fixture, "workspace.spaces.access").Position);
        using var spacesResponse = await ordinary.SendAsync(spacesRequest); spacesResponse.EnsureSuccessStatusCode();
        var personal = (await spacesResponse.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray().Single(item => item.GetProperty("spaceKind").GetString() == "Personal");
        var personalPosition = new ICP.Platform.Modules.Workspace.WorkspacePosition(personal.GetProperty("spaceId").GetString()!, personal.GetProperty("rootFolderId").GetString()!, []);
        var input = new { mode = "copy", sourcePosition = root, linkIds = new[] { source.LinkIds[^1] }, targetPosition = personalPosition, name = "私有新副本" };
        var candidateRule = new ICP.Platform.Modules.Authorization.DTOs.DataScopeRuleDto("ShallowCandidate", "ShallowCandidate", null, "新候选", true, "workspace.folder", new("attribute", "ENAME", "equal", JsonSerializer.SerializeToElement(input.name)),
            [new("ShallowCandidateAll", "all", null, 0, new Dictionary<string, string> { ["create"] = "deny" })], []);
        using (var response = await FeatureHttpTestRequests.SendAsync(admin, Assert.Single(fixture.FeatureCatalog, point => point.BehaviorKey == "authorization.dataScope.save"), HttpMethod.Put, "/api/authorization/data-scope/rules/ShallowCandidate", candidateRule)) Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        using (var denied = await Send(fixture, ordinary, "workspace.folder.pasteFolderCopy", "folders/copy", input)) await Problem(denied, HttpStatusCode.Forbidden, "Workspace.FolderCreateDenied");
        var readRule = candidateRule with { Subjects = [new("ShallowCandidateAll", "all", null, 0, new Dictionary<string, string> { ["read"] = "deny" })] };
        using (var response = await FeatureHttpTestRequests.SendAsync(admin, Assert.Single(fixture.FeatureCatalog, point => point.BehaviorKey == "authorization.dataScope.save"), HttpMethod.Put, "/api/authorization/data-scope/rules/ShallowCandidate", readRule)) Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var result = await Command(fixture, ordinary, "workspace.folder.pasteFolderCopy", "/api/workspace/content/folders/copy", input); Assert.True(result.GetProperty("succeeded").GetBoolean()); Assert.False(result.TryGetProperty("id", out _));
        var copy = Assert.Single(await Rows(fixture, "WorkspaceFolder"), row => Equals(row["ENAME"], input.name)); Assert.Equal(personalPosition.SpaceId, copy["OriginSpaceId"]);
        using (var unreadable = await FeatureHttpTestRequests.SendAsync(ordinary, Point(fixture, "workspace.spaces.access"), HttpMethod.Get,
            "/api/workspace/folders/" + (string)copy["EID"]!)) Assert.Equal(HttpStatusCode.NotFound, unreadable.StatusCode);
        var personalLink = Assert.Single(await Rows(fixture, "WorkspaceFolderContent"), row => Equals(row["CEID"], copy["EID"]));
        using (var privateDenied = await Send(fixture, admin, "workspace.folder.pasteFolderCopy", "folders/copy", new { mode = "copy", sourcePosition = personalPosition, linkIds = new[] { (string)personalLink["LID"]! }, targetPosition = target, name = "越权" }))
            await Problem(privateDenied, HttpStatusCode.NotFound, "Workspace.PositionUnavailable");
        // 先在正式预检取样，再删确切来源关系；同一对象的备用公共路径不可替代旧路径。
        await Command(fixture, ordinary, "workspace.folder.copy", "/api/workspace/content/clipboard/validate", new { mode = "copy", sourcePosition = root, input.linkIds });
        var sourceId = FolderId(source, await Rows(fixture, "WorkspaceFolderContent"));
        await Command(fixture, admin, "workspace.folder.add", "/api/workspace/content/references", new { position = target, target = new { modelKey = "WorkspaceFolder", id = sourceId } });
        using (var removed = await FeatureHttpTestRequests.SendAsync(admin, RowPoint(fixture, "remove"), HttpMethod.Delete, "/api/workspace/content/references/" + source.LinkIds[^1], new { position = root })) Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
        using var stale = await Send(fixture, ordinary, "workspace.folder.pasteFolderCopy", "folders/copy", new { input.mode, input.sourcePosition, input.linkIds, targetPosition = target, name = "失效" }); await Problem(stale, HttpStatusCode.NotFound, "Workspace.ReferenceUnavailable");
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "浅副本拒绝损坏的直接端点/次序/同父名称和来源环，保留全部既有事实")]
    public async Task ShallowCopy_RejectsCorruptSourceWithoutTruncation()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync(); var root = await Root(fixture, admin);
        var source = await Folder(fixture, admin, root, "来源"); await Folder(fixture, admin, source, "第一"); await Folder(fixture, admin, source, "第二");
        var sourceId = FolderId(source, await Rows(fixture, "WorkspaceFolderContent"));
        foreach (var kind in new[] { "ordinal", "name", "endpoint", "cycle" })
        {
            using var scope = fixture.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<ICP.Platform.Persistence.EfCore.EfPlatformDbContext>();
            var direct = await db.Set<Dictionary<string, object?>>("WorkspaceFolderContent").Where(row => EF.Property<string>(row, "PEID") == sourceId).OrderBy(row => EF.Property<string>(row, "LID")).ToArrayAsync();
            var firstId = (string)direct[0]["CEID"]!; var secondId = (string)direct[1]["CEID"]!;
            var folder = await db.Set<Dictionary<string, object?>>("WorkspaceFolder").SingleAsync(row => EF.Property<string>(row, "EID") == secondId); var originalName = folder["ENAME"]; var oldOrdinal = direct[0]["TreeOrdinal"];
            switch (kind) { case "ordinal": direct[0]["TreeOrdinal"] = null; break; case "name": folder["ENAME"] = (await db.Set<Dictionary<string, object?>>("WorkspaceFolder").SingleAsync(row => EF.Property<string>(row, "EID") == firstId))["ENAME"]; break; case "endpoint": direct[0]["CEID"] = "WorkspaceFolder_00000000000000000000000000000000"; break; case "cycle": direct[0]["CEID"] = sourceId; break; }
            await db.SaveChangesAsync(); var folders = Snapshot(await Rows(fixture, "WorkspaceFolder")); var links = Snapshot(await Rows(fixture, "WorkspaceFolderContent"));
            using (var response = await Send(fixture, admin, "workspace.folder.pasteFolderCopy", "folders/copy", new { mode = "copy", sourcePosition = root, linkIds = new[] { source.LinkIds[^1] }, targetPosition = root, name = "损坏副本" })) Assert.InRange((int)response.StatusCode, 400, 499);
            Assert.Equal(folders, Snapshot(await Rows(fixture, "WorkspaceFolder"))); Assert.Equal(links, Snapshot(await Rows(fixture, "WorkspaceFolderContent")));
            direct[0]["CEID"] = firstId; direct[0]["TreeOrdinal"] = oldOrdinal; folder["ENAME"] = originalName; await db.SaveChangesAsync();
        }
    }
}



public sealed partial class WorkspaceClipboardSqlServerHttpApiTests
{
    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "浅副本同父隐藏名称冲突按数据库实际collation拒绝，不自动改名或泄露隐藏对象")]
    public async Task ShallowCopy_RejectsHiddenCollationConflict()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync(); var root = await Root(fixture, admin);
        var source = await Folder(fixture, admin, root, "来源"); var hidden = await Folder(fixture, admin, root, "HiddenName");
        string collation; using (var scope = fixture.Services.CreateScope()) collation = await scope.ServiceProvider.GetRequiredService<ICP.Platform.Persistence.EfCore.EfPlatformDbContext>().Database.SqlQueryRaw<string>("SELECT CONVERT(nvarchar(128), DATABASEPROPERTYEX(DB_NAME(),'Collation')) AS Value").SingleAsync();
        var name = collation.Contains("_CI_", StringComparison.OrdinalIgnoreCase) ? "hiddenname" : "HiddenName";
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "ShallowHidden", "冲突账号", "shallow-hidden", "冲突账号", Point(fixture, "workspace.spaces.access"), Point(fixture, "workspace.folder.pasteFolderCopy")); using var ordinary = actor.Client;
        await Rule(fixture, admin, "ShallowHiddenName", "workspace.folder", FolderId(hidden, await Rows(fixture, "WorkspaceFolderContent")), "read", "deny");
        var before = Snapshot(await Rows(fixture, "WorkspaceFolder")); var links = Snapshot(await Rows(fixture, "WorkspaceFolderContent"));
        using var conflict = await Send(fixture, ordinary, "workspace.folder.pasteFolderCopy", "folders/copy", new { mode = "copy", sourcePosition = root, linkIds = new[] { source.LinkIds[^1] }, targetPosition = root, name });
        await Problem(conflict, HttpStatusCode.Conflict, "Workspace.SiblingNameConflict"); var body = await conflict.Content.ReadAsStringAsync(); Assert.DoesNotContain("HiddenName", body);
        Assert.Equal(before, Snapshot(await Rows(fixture, "WorkspaceFolder"))); Assert.Equal(links, Snapshot(await Rows(fixture, "WorkspaceFolderContent")));
        output.WriteLine("COLLATION " + collation + "; equality resolved by the database; hidden conflict returned only the generic field error.");
    }
}
