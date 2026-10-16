using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Modules.Workspace;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ICP.Platform.Testing;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed partial class WorkspaceClipboardSqlServerHttpApiTests
{
    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "深层Folder及每个候选独立裁决，排除业务对象拒绝不影响结构，源无须edit")]
    public async Task StructureCopy_RechecksDeepPermissionsAndEveryCandidateWithoutReadingExcludedObjects()
    {
        var excludedReads = new StructureExcludedReadGuard();
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(services => services.AddDbContext<EfPlatformDbContext>((_, options) => options.AddInterceptors(excludedReads)), includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync(); var root = await Root(fixture, admin);
        var source = await Folder(fixture, admin, root, "范围来源"); var target = await Folder(fixture, admin, root, "目标");
        var middle = await Folder(fixture, admin, source, "中间"); var leaf = await Folder(fixture, admin, middle, "深层保密名称");
        await Command(fixture, admin, "workspace.folder.createPart", "/api/workspace/content/parts", new { position = source, values = new { ECODE = "STRUCTURE-DENIED-PART", ENAME = "排除的零组件" } });
        await CreateShallowDocumentAsync(fixture, admin, leaf, "STRUCTURE-DENIED-DOCUMENT");
        var allLinks = await Rows(fixture, "WorkspaceFolderContent"); var sourceId = FolderId(source, allLinks); var leafId = FolderId(leaf, allLinks);
        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
            foreach (var id in new[] { sourceId, leafId }) (await db.Set<Dictionary<string, object?>>("WorkspaceFolder").SingleAsync(row => EF.Property<string>(row, "EID") == id))["Description"] = "白名单说明";
            await db.SaveChangesAsync();
        }
        var parts = Snapshot(await Rows(fixture, "Part")); var documents = Snapshot(await Rows(fixture, "Document"));
        var files = new Dictionary<string, string>(); foreach (var model in new[] { "DocumentVersion", "DocumentIteration", "DocumentFileLink", "FileUsage", "FileRecord" }) files[model] = Snapshot(await Rows(fixture, model));
        var bytes = Directory.GetFiles(fixture.StorageRoot).ToDictionary(path => path, File.ReadAllBytes);
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "StructureScope", "结构账号", "structure-scope", "结构账号", Point(fixture, "workspace.spaces.access"), Point(fixture, "workspace.folder.copy"), Point(fixture, "workspace.folder.pasteFolderStructure"));
        using var ordinary = actor.Client;
        foreach (var id in new[] { root.RootFolderId, sourceId, FolderId(middle, allLinks), leafId }) await Rule(fixture, admin, "StructureEdit" + id, "workspace.folder", id, "edit", "deny");
        foreach (var (model, resource, copy) in new[] { ("Part", "part.part", "partCopy"), ("Document", "document.document", "documentCopy") })
            foreach (var action in new[] { "read", copy }) await Rule(fixture, admin, model + action, resource, (string)Assert.Single(await Rows(fixture, model))["EID"]!, action, "deny");
        var input = new { mode = "copy", sourcePosition = root, linkIds = new[] { source.LinkIds[^1] }, targetPosition = target, name = "结构权限成功" };
        var preparation = await Command(fixture, ordinary, "workspace.folder.pasteFolderStructure", "/api/workspace/content/folders/copy-structure/prepare", new { input.mode, input.sourcePosition, input.linkIds, input.targetPosition });
        Assert.Equal("范围来源", preparation.GetProperty("sourceName").GetString());
        var foldersBefore = Snapshot(await Rows(fixture, "WorkspaceFolder")); var linksBefore = Snapshot(await Rows(fixture, "WorkspaceFolderContent")); var changesBefore = Snapshot(await Rows(fixture, "BusinessChange"));
        foreach (var (id, action) in new[] { (sourceId, "workspaceCopy"), (sourceId, "read"), (leafId, "workspaceCopy"), (leafId, "read"), (FolderId(target, allLinks), "edit") })
        {
            await Rule(fixture, admin, "StructureDenied", "workspace.folder", id, action, "deny");
            var auditBeforeAttempt = Snapshot(await Rows(fixture, "BusinessChange"));
            using (var denied = await Send(fixture, ordinary, "workspace.folder.pasteFolderStructure", "folders/copy-structure", input))
            { Assert.InRange((int)denied.StatusCode, 400, 499); Assert.DoesNotContain("深层保密名称", await denied.Content.ReadAsStringAsync()); }
            Assert.Equal(foldersBefore, Snapshot(await Rows(fixture, "WorkspaceFolder"))); Assert.Equal(linksBefore, Snapshot(await Rows(fixture, "WorkspaceFolderContent"))); Assert.Equal(auditBeforeAttempt, Snapshot(await Rows(fixture, "BusinessChange")));
            await Rule(fixture, admin, "StructureDenied", "workspace.folder", id, action, "allow");
        }
        var candidateRule = new ICP.Platform.Modules.Authorization.DTOs.DataScopeRuleDto("StructureCandidate", "StructureCandidate", null, "逐候选", true, "workspace.folder", new("attribute", "ENAME", "equal", JsonSerializer.SerializeToElement("深层保密名称")),
            [new("StructureCandidateAll", "all", null, 0, new Dictionary<string, string> { ["create"] = "deny" })], []);
        var save = Assert.Single(fixture.FeatureCatalog, point => point.BehaviorKey == "authorization.dataScope.save");
        using (var response = await FeatureHttpTestRequests.SendAsync(admin, save, HttpMethod.Put, "/api/authorization/data-scope/rules/StructureCandidate", candidateRule)) Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        using (var denied = await Send(fixture, ordinary, "workspace.folder.pasteFolderStructure", "folders/copy-structure", input)) await Problem(denied, HttpStatusCode.Forbidden, "Workspace.FolderCreateDenied");
        Assert.Equal(foldersBefore, Snapshot(await Rows(fixture, "WorkspaceFolder"))); Assert.Equal(linksBefore, Snapshot(await Rows(fixture, "WorkspaceFolderContent")));
        // 新根read拒绝；不能因结果不可读而否定已完成的create。
        var readRule = candidateRule with { Condition = new("attribute", "ENAME", "equal", JsonSerializer.SerializeToElement(input.name)), Subjects = [new("StructureCandidateAll", "all", null, 0, new Dictionary<string, string> { ["read"] = "deny" })] };
        using (var response = await FeatureHttpTestRequests.SendAsync(admin, save, HttpMethod.Put, "/api/authorization/data-scope/rules/StructureCandidate", readRule)) Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        excludedReads.Active = true;
        try { await Command(fixture, ordinary, "workspace.folder.pasteFolderStructure", "/api/workspace/content/folders/copy-structure", input); }
        finally { excludedReads.Active = false; }
        Assert.Equal(0, excludedReads.Hits);
        var created = (await Rows(fixture, "WorkspaceFolder")).Where(row => row["CreatedBy"] as string == actor.AccountId).ToArray();
        Assert.Equal(3, created.Length); Assert.All(created, row => Assert.Equal(root.SpaceId, row["OriginSpaceId"]));
        Assert.Equal("白名单说明", Assert.Single(created, row => Equals(row["ENAME"], input.name))["Description"]); Assert.Equal("白名单说明", Assert.Single(created, row => Equals(row["ENAME"], "深层保密名称"))["Description"]);
        Assert.Equal(parts, Snapshot(await Rows(fixture, "Part"))); Assert.Equal(documents, Snapshot(await Rows(fixture, "Document")));
        foreach (var (model, snapshot) in files) Assert.Equal(snapshot, Snapshot(await Rows(fixture, model))); foreach (var (path, original) in bytes) Assert.Equal(original, await File.ReadAllBytesAsync(path));
        var records = (await Rows(fixture, "BusinessChange")).Where(row => Equals(row["EventType"], "workspace.folder.structureCopied")).ToArray();
        Assert.Equal(3, records.Length); Assert.All(records, row => Assert.Equal(actor.AccountId, row["ActorId"]));
        Assert.DoesNotContain("深层保密名称", JsonSerializer.Serialize(records));
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "结构复制保留菱形的全部内部边及唯一共享对象，不带外部入边或业务对象")]
    public async Task StructureCopy_RebuildsDiamondWithOneIdentityPerFolder()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync(); var root = await Root(fixture, admin);
        var source = await Folder(fixture, admin, root, "S"); var outside = await Folder(fixture, admin, root, "外部");
        var a = await Folder(fixture, admin, source, "Z-first"); var b = await Folder(fixture, admin, source, "A-second");
        var c = await Folder(fixture, admin, a, "共享后代");
        var originalLinks = await Rows(fixture, "WorkspaceFolderContent"); var cId = FolderId(c, originalLinks);
        foreach (var parent in new[] { b, outside })
            await Command(fixture, admin, "workspace.folder.add", "/api/workspace/content/references", new { position = parent, target = new { modelKey = "WorkspaceFolder", id = cId } });
        await Command(fixture, admin, "workspace.folder.createPart", "/api/workspace/content/parts", new { position = source, values = new { ECODE = "STRUCTURE-PART", ENAME = "排除" } });
        var foldersBefore = await Rows(fixture, "WorkspaceFolder"); var linksBefore = await Rows(fixture, "WorkspaceFolderContent");
        var result = await Command(fixture, admin, "workspace.folder.pasteFolderStructure", "/api/workspace/content/folders/copy-structure", new {
            mode = "copy", sourcePosition = root, linkIds = new[] { source.LinkIds[^1] }, targetPosition = c, name = "独立结构" });
        Assert.True(result.GetProperty("succeeded").GetBoolean());
        var folders = await Rows(fixture, "WorkspaceFolder"); var links = await Rows(fixture, "WorkspaceFolderContent");
        var created = folders.Where(row => !foldersBefore.Any(old => Equals(old["EID"], row["EID"]))).ToArray();
        Assert.Equal(4, created.Length); var newRoot = Assert.Single(created, row => Equals(row["ENAME"], "独立结构"));
        var newA = Assert.Single(created, row => Equals(row["ENAME"], "Z-first")); var newB = Assert.Single(created, row => Equals(row["ENAME"], "A-second"));
        var newC = Assert.Single(created, row => Equals(row["ENAME"], "共享后代"));
        var added = links.Where(row => !linksBefore.Any(old => Equals(old["LID"], row["LID"]))).ToArray();
        Assert.Equal(5, added.Length); Assert.All(added, row => { Assert.Equal("WorkspaceFolder", row["ChildModelKey"]); Assert.Contains(created, child => Equals(child["EID"], row["CEID"])); });
        Assert.Contains(added, row => Equals(row["PEID"], cId) && Equals(row["CEID"], newRoot["EID"]));
        Assert.Equal(new[] { newA["EID"], newB["EID"] }, added.Where(row => Equals(row["PEID"], newRoot["EID"])).OrderBy(row => row["TreeOrdinal"]).Select(row => row["CEID"]));
        Assert.Equal(2, added.Count(row => Equals(row["CEID"], newC["EID"])));
        foreach (var old in foldersBefore) Assert.Contains(folders, row => Snapshot([row]) == Snapshot([old]));
        foreach (var old in linksBefore) Assert.Contains(links, row => Snapshot([row]) == Snapshot([old]));
        Assert.Equal(4, (await Rows(fixture, "BusinessChange")).Count(row => Equals(row["EventType"], "workspace.folder.structureCopied")));
    }
}

public sealed partial class WorkspaceClipboardSqlServerHttpApiTests
{
    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "结构复制拒绝模式、多源、伪造、非Folder、失效路径及创建名称，空目录和同父新名可用")]
    public async Task StructureCopy_RejectsInvalidInputsAndAllowsEmptySameParentCopy()
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
            using var response = await Send(fixture, admin, "workspace.folder.pasteFolderStructure", "folders/copy-structure", input);
            Assert.InRange((int)response.StatusCode, 400, 499);
            Assert.Equal(objects, Snapshot(await Rows(fixture, "WorkspaceFolder"))); Assert.Equal(before, Snapshot(await Rows(fixture, "WorkspaceFolderContent")));
        }
        await Command(fixture, admin, "workspace.folder.pasteFolderStructure", "/api/workspace/content/folders/copy-structure", new { mode = "copy", sourcePosition = root, linkIds = new[] { sourceLid }, targetPosition = root, name = "同父空副本" });
        Assert.Single(await Rows(fixture, "WorkspaceFolder"), row => Equals(row["ENAME"], "同父空副本"));
        using var conflict = await Send(fixture, admin, "workspace.folder.pasteFolderStructure", "folders/copy-structure", new { mode = "copy", sourcePosition = root, linkIds = new[] { sourceLid }, targetPosition = root, name = "原目录" });
        await Problem(conflict, HttpStatusCode.Conflict, "Workspace.SiblingNameConflict");
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "结构复制创建/关系/业务审计及提交前取消失败全部回滚，来源所有事实保持")]
    public async Task StructureCopy_RollsBackAuditFailuresAndCancellationAfterSqlWrites()
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
            try { using var failed = await Send(fixture, admin, "workspace.folder.pasteFolderStructure", "folders/copy-structure", input); Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode); Assert.Equal(1, writeFault.Hits); }
            finally { writeFault.Model = ""; }
            Assert.Equal(folders, Snapshot(await Rows(fixture, "WorkspaceFolder"))); Assert.Equal(links, Snapshot(await Rows(fixture, "WorkspaceFolderContent"))); Assert.Equal(changes, Snapshot(await Rows(fixture, "BusinessChange")));
        }
        foreach (var mode in new[] { "register", "write", "save", "commit" })
        {
            fixture.Fault.Mode = mode == "commit" ? "" : mode; control.Fail = mode == "commit";
            try { using var failed = await Send(fixture, admin, "workspace.folder.pasteFolderStructure", "folders/copy-structure", input); Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode); }
            finally { fixture.Fault.Mode = ""; control.Fail = false; }
            Assert.Equal(folders, Snapshot(await Rows(fixture, "WorkspaceFolder"))); Assert.Equal(links, Snapshot(await Rows(fixture, "WorkspaceFolderContent"))); Assert.Equal(changes, Snapshot(await Rows(fixture, "BusinessChange")));
        }
        // 根入边、内部边及最后一个新对象的业务登记分别失败，证明不会只覆盖第一条事件。
        foreach (var stage in new[] { "root-reference", "internal-reference", "last-folder" })
        {
            var referenceCount = 0; var folderCount = 0; var hits = 0;
            fixture.Fault.AfterRegister = (change, _) => {
                if (change.EventType == "workspace.reference.copied") referenceCount++;
                if (change.EventType == "workspace.folder.structureCopied") folderCount++;
                if (stage == "root-reference" && referenceCount == 1 || stage == "internal-reference" && referenceCount == 2 || stage == "last-folder" && folderCount == 2)
                { hits++; throw new InvalidOperationException("Injected structure copy registration stage failure."); }
                return Task.CompletedTask;
            };
            try { using var failed = await Send(fixture, admin, "workspace.folder.pasteFolderStructure", "folders/copy-structure", input); Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode); Assert.Equal(1, hits); }
            finally { fixture.Fault.AfterRegister = null; }
            Assert.Equal(folders, Snapshot(await Rows(fixture, "WorkspaceFolder"))); Assert.Equal(links, Snapshot(await Rows(fixture, "WorkspaceFolderContent"))); Assert.Equal(changes, Snapshot(await Rows(fixture, "BusinessChange")));
        }
        control.Pause = true; using var cancellation = new CancellationTokenSource();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/workspace/content/folders/copy-structure") { Content = JsonContent.Create(input) };
        request.Headers.Add("X-NewP-Feature", Point(fixture, "workspace.folder.pasteFolderStructure").Position);
        var pending = admin.SendAsync(request, cancellation.Token);
        try { await control.Reached.Task.WaitAsync(TimeSpan.FromSeconds(20)); cancellation.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await pending); await control.RolledBack.Task.WaitAsync(TimeSpan.FromSeconds(20)); }
        finally { cancellation.Cancel(); control.Resume.TrySetResult(); try { (await pending).Dispose(); } catch (OperationCanceledException) { } }
        Assert.Equal(folders, Snapshot(await Rows(fixture, "WorkspaceFolder"))); Assert.Equal(links, Snapshot(await Rows(fixture, "WorkspaceFolderContent"))); Assert.Equal(changes, Snapshot(await Rows(fixture, "BusinessChange")));
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "结构复制跨空间来源/目标、候选create、提交后不可读及剪贴板来源失效均按当前正式规则判定")]
    public async Task StructureCopy_RechecksCandidatePrivateSpaceAndExactStalePath()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync(); var root = await Root(fixture, admin);
        var source = await Folder(fixture, admin, root, "公共来源"); var target = await Folder(fixture, admin, root, "公共目标");
        var crossSpaceChild = await Folder(fixture, admin, source, "跨空间后代");
        var sourceChildId = FolderId(crossSpaceChild, await Rows(fixture, "WorkspaceFolderContent"));
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "ShallowScope", "范围账号", "shallow-scope", "范围账号", Point(fixture, "workspace.spaces.access"), Point(fixture, "workspace.folder.copy"), Point(fixture, "workspace.folder.pasteFolderStructure"));
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
        using (var denied = await Send(fixture, ordinary, "workspace.folder.pasteFolderStructure", "folders/copy-structure", input)) await Problem(denied, HttpStatusCode.Forbidden, "Workspace.FolderCreateDenied");
        var readRule = candidateRule with { Subjects = [new("ShallowCandidateAll", "all", null, 0, new Dictionary<string, string> { ["read"] = "deny" })] };
        using (var response = await FeatureHttpTestRequests.SendAsync(admin, Assert.Single(fixture.FeatureCatalog, point => point.BehaviorKey == "authorization.dataScope.save"), HttpMethod.Put, "/api/authorization/data-scope/rules/ShallowCandidate", readRule)) Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var result = await Command(fixture, ordinary, "workspace.folder.pasteFolderStructure", "/api/workspace/content/folders/copy-structure", input); Assert.True(result.GetProperty("succeeded").GetBoolean()); Assert.False(result.TryGetProperty("id", out _));
        var copy = Assert.Single(await Rows(fixture, "WorkspaceFolder"), row => Equals(row["ENAME"], input.name)); Assert.Equal(personalPosition.SpaceId, copy["OriginSpaceId"]);
        var newChild = Assert.Single(await Rows(fixture, "WorkspaceFolder"), row => Equals(row["ENAME"], "跨空间后代") && !Equals(row["EID"], sourceChildId));
        Assert.Equal(personalPosition.SpaceId, newChild["OriginSpaceId"]); Assert.Equal(actor.AccountId, newChild["CreatedBy"]);
        using (var unreadable = await FeatureHttpTestRequests.SendAsync(ordinary, Point(fixture, "workspace.spaces.access"), HttpMethod.Get,
            "/api/workspace/folders/" + (string)copy["EID"]!)) Assert.Equal(HttpStatusCode.NotFound, unreadable.StatusCode);
        var personalLink = Assert.Single(await Rows(fixture, "WorkspaceFolderContent"), row => Equals(row["CEID"], copy["EID"]));
        using (var privateDenied = await Send(fixture, admin, "workspace.folder.pasteFolderStructure", "folders/copy-structure", new { mode = "copy", sourcePosition = personalPosition, linkIds = new[] { (string)personalLink["LID"]! }, targetPosition = target, name = "越权" }))
            await Problem(privateDenied, HttpStatusCode.NotFound, "Workspace.PositionUnavailable");
        // 先在正式预检取样，再删确切来源关系；同一对象的备用公共路径不可替代旧路径。
        await Command(fixture, ordinary, "workspace.folder.copy", "/api/workspace/content/clipboard/validate", new { mode = "copy", sourcePosition = root, input.linkIds });
        var sourceId = FolderId(source, await Rows(fixture, "WorkspaceFolderContent"));
        await Command(fixture, admin, "workspace.folder.add", "/api/workspace/content/references", new { position = target, target = new { modelKey = "WorkspaceFolder", id = sourceId } });
        using (var removed = await FeatureHttpTestRequests.SendAsync(admin, RowPoint(fixture, "remove"), HttpMethod.Delete, "/api/workspace/content/references/" + source.LinkIds[^1], new { position = root })) Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
        using var stale = await Send(fixture, ordinary, "workspace.folder.pasteFolderStructure", "folders/copy-structure", new { input.mode, input.sourcePosition, input.linkIds, targetPosition = target, name = "失效" }); await Problem(stale, HttpStatusCode.NotFound, "Workspace.ReferenceUnavailable");
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "结构复制拒绝损坏的直接端点/次序/同父名称和来源环，保留全部既有事实")]
    public async Task StructureCopy_RejectsCorruptSourceWithoutTruncation()
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
            using (var response = await Send(fixture, admin, "workspace.folder.pasteFolderStructure", "folders/copy-structure", new { mode = "copy", sourcePosition = root, linkIds = new[] { source.LinkIds[^1] }, targetPosition = root, name = "损坏副本" })) Assert.InRange((int)response.StatusCode, 400, 499);
            Assert.Equal(folders, Snapshot(await Rows(fixture, "WorkspaceFolder"))); Assert.Equal(links, Snapshot(await Rows(fixture, "WorkspaceFolderContent")));
            direct[0]["CEID"] = firstId; direct[0]["TreeOrdinal"] = oldOrdinal; folder["ENAME"] = originalName; await db.SaveChangesAsync();
        }
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "结构复制同父隐藏名称冲突按数据库实际collation拒绝，不自动改名或泄露隐藏对象")]
    public async Task StructureCopy_RejectsHiddenCollationConflict()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync(); var root = await Root(fixture, admin);
        var source = await Folder(fixture, admin, root, "来源"); var hidden = await Folder(fixture, admin, root, "HiddenName");
        string collation; using (var scope = fixture.Services.CreateScope()) collation = await scope.ServiceProvider.GetRequiredService<ICP.Platform.Persistence.EfCore.EfPlatformDbContext>().Database.SqlQueryRaw<string>("SELECT CONVERT(nvarchar(128), DATABASEPROPERTYEX(DB_NAME(),'Collation')) AS Value").SingleAsync();
        var name = collation.Contains("_CI_", StringComparison.OrdinalIgnoreCase) ? "hiddenname" : "HiddenName";
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "ShallowHidden", "冲突账号", "shallow-hidden", "冲突账号", Point(fixture, "workspace.spaces.access"), Point(fixture, "workspace.folder.pasteFolderStructure")); using var ordinary = actor.Client;
        await Rule(fixture, admin, "ShallowHiddenName", "workspace.folder", FolderId(hidden, await Rows(fixture, "WorkspaceFolderContent")), "read", "deny");
        var before = Snapshot(await Rows(fixture, "WorkspaceFolder")); var links = Snapshot(await Rows(fixture, "WorkspaceFolderContent"));
        using var conflict = await Send(fixture, ordinary, "workspace.folder.pasteFolderStructure", "folders/copy-structure", new { mode = "copy", sourcePosition = root, linkIds = new[] { source.LinkIds[^1] }, targetPosition = root, name });
        await Problem(conflict, HttpStatusCode.Conflict, "Workspace.SiblingNameConflict"); var body = await conflict.Content.ReadAsStringAsync(); Assert.DoesNotContain("HiddenName", body);
        Assert.Equal(before, Snapshot(await Rows(fixture, "WorkspaceFolder"))); Assert.Equal(links, Snapshot(await Rows(fixture, "WorkspaceFolderContent")));
        output.WriteLine("COLLATION " + collation + "; equality resolved by the database; hidden conflict returned only the generic field error.");
    }
}

public sealed partial class WorkspaceClipboardSqlServerHttpApiTests
{
    private sealed class StructureExcludedReadGuard : DbCommandInterceptor
    {
        internal bool Active; internal int Hits;
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken token = default)
        {
            if (Active && new[] { "Part", "Document", "DocumentVersion", "DocumentIteration", "DocumentFileLink", "FileRecord", "FileUsage", "FileUploadLease" }.Any(model => command.CommandText.Contains("[" + model + "]", StringComparison.Ordinal)))
            { Hits++; throw new InvalidOperationException("Structure copy must not read excluded business objects or file tables."); }
            return ValueTask.FromResult(result);
        }
    }
}
