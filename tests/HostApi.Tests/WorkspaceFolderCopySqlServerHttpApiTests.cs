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
    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "两种新副本使目标非空；仅移除新根引用后才能删除目标，来源与副本对象不删除")]
    public async Task FolderCopies_RemoveReferencesBeforeDeletingTheirParent()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync(); var root = await Root(fixture, admin);
        var source = await Folder(fixture, admin, root, "清理来源");
        await Folder(fixture, admin, source, "保留来源子文件夹");
        var target = await Folder(fixture, admin, root, "待删除父目录");
        var targetId = FolderId(target, await Rows(fixture, "WorkspaceFolderContent"));
        await Command(fixture, admin, "workspace.folder.pasteFolderCopy", "/api/workspace/content/folders/copy", new {
            mode = "copy", sourcePosition = root, linkIds = new[] { source.LinkIds[^1] }, targetPosition = target, name = "清理浅副本" });
        await Command(fixture, admin, "workspace.folder.pasteFolderStructure", "/api/workspace/content/folders/copy-structure", new {
            mode = "copy", sourcePosition = root, linkIds = new[] { source.LinkIds[^1] }, targetPosition = target, name = "清理结构副本" });
        var folders = await Rows(fixture, "WorkspaceFolder"); var links = await Rows(fixture, "WorkspaceFolderContent");
        var copyIds = folders.Where(row => Equals(row["ENAME"], "清理浅副本") || Equals(row["ENAME"], "清理结构副本")).Select(row => row["EID"]).ToHashSet();
        Assert.Equal(2, copyIds.Count);
        var incoming = links.Where(row => Equals(row["PEID"], targetId)).ToArray();
        Assert.Equal(2, incoming.Length); Assert.All(incoming, row => Assert.Contains(row["CEID"], copyIds));
        var delete = new { position = root, linkIds = new[] { target.LinkIds[^1] } };
        using (var rejected = await Send(fixture, admin, "workspace.folder.deleteObjects", "delete", delete))
            await Problem(rejected, HttpStatusCode.Conflict, "Workspace.FolderNotEmpty");
        Assert.Equal(Snapshot(folders), Snapshot(await Rows(fixture, "WorkspaceFolder")));
        Assert.Equal(Snapshot(links), Snapshot(await Rows(fixture, "WorkspaceFolderContent")));
        await Command(fixture, admin, "workspace.folder.removeSelected", "/api/workspace/content/references/remove", new {
            position = target, linkIds = incoming.Select(row => (string)row["LID"]!).ToArray() });
        Assert.Equal(Snapshot(folders), Snapshot(await Rows(fixture, "WorkspaceFolder")));
        Assert.Equal(Snapshot(links.Where(row => !incoming.Any(edge => Equals(edge["LID"], row["LID"]))).ToArray()), Snapshot(await Rows(fixture, "WorkspaceFolderContent")));
        await Command(fixture, admin, "workspace.folder.deleteObjects", "/api/workspace/content/delete", delete);
        Assert.Equal(Snapshot(folders.Where(row => !Equals(row["EID"], targetId)).ToArray()), Snapshot(await Rows(fixture, "WorkspaceFolder")));
        Assert.Equal(Snapshot(links.Where(row => !incoming.Any(edge => Equals(edge["LID"], row["LID"])) && !Equals(row["LID"], target.LinkIds[^1])).ToArray()),
            Snapshot(await Rows(fixture, "WorkspaceFolderContent")));
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "cut重建LID原样保留历史NULL、普通false和中性true；结构复制仍遍历中性边并生成普通新边")]
    public async Task ScopeNeutral_CutPreservesAllStatesAndStructureTraversesNeutralEdges()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync(); var root = await Root(fixture, admin);
        var source = await Folder(fixture, admin, root, "迁移来源"); var target = await Folder(fixture, admin, root, "迁移目标");
        foreach (var neutral in new bool?[] { null, false, true })
        {
            var child = await Folder(fixture, admin, source, "移动-" + (neutral?.ToString() ?? "历史NULL"));
            var sourceLid = child.LinkIds[^1];
            using (var scope = fixture.Services.CreateScope())
                await scope.ServiceProvider.GetRequiredService<ICP.Platform.Persistence.EfCore.EfPlatformDbContext>().Set<Dictionary<string, object?>>("WorkspaceFolderContent")
                    .Where(row => EF.Property<string>(row, "LID") == sourceLid).ExecuteUpdateAsync(set => set.SetProperty(row => EF.Property<bool?>(row, "IsScopeNeutral"), neutral));
            await Command(fixture, admin, "workspace.folder.paste", "/api/workspace/content/paste", new { mode = "cut", sourcePosition = source, linkIds = new[] { child.LinkIds[^1] }, targetPosition = target });
            var links = await Rows(fixture, "WorkspaceFolderContent");
            var moved = Assert.Single(links, row => Equals(row["PEID"], FolderId(target, links)) && Equals(row["IsScopeNeutral"], neutral));
            Assert.DoesNotContain(links, row => Equals(row["LID"], child.LinkIds[^1])); Assert.NotEqual(child.LinkIds[^1], moved["LID"]);
        }
        await Command(fixture, admin, "workspace.folder.pasteFolderCopy", "/api/workspace/content/folders/copy", new { mode = "copy", sourcePosition = root, linkIds = new[] { target.LinkIds[^1] }, targetPosition = root, name = "中性来源" });
        var facts = await Rows(fixture, "WorkspaceFolderContent"); var folders = await Rows(fixture, "WorkspaceFolder");
        var neutralId = (string)Assert.Single(folders, row => Equals(row["ENAME"], "中性来源"))["EID"]!;
        var neutralRootLink = Assert.Single(facts, row => Equals(row["CEID"], neutralId));
        Assert.Equal(3, facts.Count(row => Equals(row["PEID"], neutralId) && Equals(row["IsScopeNeutral"], true)));
        var beforeIds = folders.Select(row => row["EID"]).ToHashSet();
        await Command(fixture, admin, "workspace.folder.pasteFolderStructure", "/api/workspace/content/folders/copy-structure", new { mode = "copy", sourcePosition = root, linkIds = new[] { neutralRootLink["LID"] }, targetPosition = root, name = "新结构" });
        var created = (await Rows(fixture, "WorkspaceFolder")).Where(row => !beforeIds.Contains(row["EID"])).Select(row => row["EID"]).ToHashSet(); Assert.Equal(4, created.Count);
        Assert.All((await Rows(fixture, "WorkspaceFolderContent")).Where(row => created.Contains(row["CEID"])), row => Assert.Equal(false, row["IsScopeNeutral"]));
        var childId = facts.First(row => Equals(row["PEID"], neutralId))["CEID"];
        var childPosition = root with { LinkIds = [target.LinkIds[^1], (string)facts.Single(row => Equals(row["PEID"], FolderId(target, facts)) && Equals(row["CEID"], childId))["LID"]!] };
        using var cycle = await Send(fixture, admin, "workspace.folder.pasteFolderCopy", "folders/copy", new { mode = "copy", sourcePosition = root, linkIds = new[] { neutralRootLink["LID"] }, targetPosition = childPosition, name = "不忽略中性边判环" });
        Assert.InRange((int)cycle.StatusCode, 400, 499);
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "FC12浅副本不能使公共主体首次读取私人来源子对象，原私人路径仍不可借用")]
    public async Task ShallowCopy_DoesNotExpandAnotherActorsPrivateChildReadScope()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync(); var publicRoot = await Root(fixture, admin);
        var target = await Folder(fixture, admin, publicRoot, "公共目标");
        var alice = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "Fc12AliceRole", "私人拥有者", "fc12-alice", "Alice",
            Point(fixture, "workspace.spaces.access"), Point(fixture, "workspace.folder.create"), Point(fixture, "workspace.folder.copy"), Point(fixture, "workspace.folder.pasteFolderCopy"),
            Point(fixture, "workspace.folder.paste"), Point(fixture, "workspace.folder.cut"));
        using var aliceClient = alice.Client;
        var bob = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "Fc12BobRole", "公共读取者", "fc12-bob", "Bob", Point(fixture, "workspace.spaces.access"));
        using var bobClient = bob.Client;
        var personal = await Command(fixture, aliceClient, "workspace.spaces.access", "/api/workspace/spaces/personal/ensure");
        var personalRoot = new ICP.Platform.Modules.Workspace.WorkspacePosition(personal.GetProperty("spaceId").GetString()!, personal.GetProperty("rootFolderId").GetString()!, []);
        var source = await Folder(fixture, aliceClient, personalRoot, "私人S");
        var child = await Folder(fixture, aliceClient, source, "私人C");
        var childId = FolderId(child, await Rows(fixture, "WorkspaceFolderContent"));
        await Rule(fixture, admin, "Fc12ChildRead", "workspace.folder", childId, "read", "allow");
        var privatePath = "/api/workspace/content/path?spaceId=" + personalRoot.SpaceId + "&rootFolderId=" + personalRoot.RootFolderId
            + "&linkIds=" + Uri.EscapeDataString(JsonSerializer.Serialize(child.LinkIds));
        using var before = await FeatureHttpTestRequests.SendAsync(bobClient, Point(fixture, "workspace.spaces.access"), HttpMethod.Get, "/api/workspace/folders/" + childId);
        using var privateBefore = await FeatureHttpTestRequests.SendAsync(bobClient, Point(fixture, "workspace.spaces.access"), HttpMethod.Get, privatePath);
        Assert.Equal(HttpStatusCode.NotFound, before.StatusCode); Assert.Equal(HttpStatusCode.NotFound, privateBefore.StatusCode);
        var copied = await Command(fixture, aliceClient, "workspace.folder.pasteFolderCopy", "/api/workspace/content/folders/copy", new {
            mode = "copy", sourcePosition = personalRoot, linkIds = new[] { source.LinkIds[^1] }, targetPosition = target, name = "公共浅副本N" });
        Assert.True(copied.GetProperty("succeeded").GetBoolean());
        using var after = await FeatureHttpTestRequests.SendAsync(bobClient, Point(fixture, "workspace.spaces.access"), HttpMethod.Get, "/api/workspace/folders/" + childId);
        using var privateAfter = await FeatureHttpTestRequests.SendAsync(bobClient, Point(fixture, "workspace.spaces.access"), HttpMethod.Get, privatePath);
        output.WriteLine(JsonSerializer.Serialize(new { scenario = "FC12-private-child-shallow-copy", before = (int)before.StatusCode,
            after = (int)after.StatusCode, privatePathBefore = (int)privateBefore.StatusCode, privatePathAfter = (int)privateAfter.StatusCode,
            copySucceeded = true, childOriginUnchanged = Equals(Assert.Single(await Rows(fixture, "WorkspaceFolder"), row => Equals(row["EID"], childId))["OriginSpaceId"], personalRoot.SpaceId) }));
        Assert.Equal(HttpStatusCode.NotFound, privateAfter.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, after.StatusCode);
        using (var ownerRead = await FeatureHttpTestRequests.SendAsync(aliceClient, Point(fixture, "workspace.spaces.access"), HttpMethod.Get, "/api/workspace/folders/" + childId))
            Assert.Equal(HttpStatusCode.OK, ownerRead.StatusCode);
        var links = await Rows(fixture, "WorkspaceFolderContent");
        var newRootId = (string)Assert.Single(await Rows(fixture, "WorkspaceFolder"), row => Equals(row["ENAME"], "公共浅副本N"))["EID"]!;
        var newRootLink = Assert.Single(links, row => Equals(row["CEID"], newRootId)); Assert.Equal(false, newRootLink["IsScopeNeutral"]);
        var neutral = Assert.Single(links, row => Equals(row["PEID"], newRootId)); Assert.Equal(true, neutral["IsScopeNeutral"]);
        var newPosition = target with { LinkIds = [.. target.LinkIds, (string)newRootLink["LID"]!] };
        var query = "&queryScope=" + Uri.EscapeDataString(JsonSerializer.Serialize(new { spaceId = newPosition.SpaceId, rootFolderId = newPosition.RootFolderId, linkIds = JsonSerializer.Serialize(newPosition.LinkIds) }));
        var visible = await FeatureHttpTestRequests.ReadAsync(bobClient, Point(fixture, "workspace.spaces.access"), "/api/links/workspace.content?direction=forward&anchorModelKey=WorkspaceFolder&anchorId=" + newRootId + query);
        Assert.Empty(visible.GetProperty("items").EnumerateArray());
        var children = await FeatureHttpTestRequests.ReadAsync(bobClient, Point(fixture, "workspace.spaces.access"), "/api/workspace/content/children?spaceId=" + newPosition.SpaceId + "&rootFolderId=" + newPosition.RootFolderId + "&linkIds=" + Uri.EscapeDataString(JsonSerializer.Serialize(newPosition.LinkIds)));
        Assert.Empty(children.GetProperty("items").EnumerateArray());
        var candidates = await FeatureHttpTestRequests.ReadAsync(bobClient, Point(fixture, "workspace.spaces.access"), "/api/workspace/folders/candidates?keyword=" + Uri.EscapeDataString("私人C"));
        Assert.Empty(candidates.GetProperty("items").EnumerateArray());
        // 移动不是再次分享，原true必须随新LID保留；同父no-op连审计也不写。
        var noOpLinks = Snapshot(links); var noOpAudit = Snapshot(await Rows(fixture, "BusinessChange"));
        var noOp = await Command(fixture, aliceClient, "workspace.folder.paste", "/api/workspace/content/paste", new { mode = "cut", sourcePosition = newPosition, linkIds = new[] { neutral["LID"] }, targetPosition = newPosition });
        Assert.True(noOp.GetProperty("noChange").GetBoolean()); Assert.Equal(noOpLinks, Snapshot(await Rows(fixture, "WorkspaceFolderContent"))); Assert.Equal(noOpAudit, Snapshot(await Rows(fixture, "BusinessChange")));
        var movedTo = await Folder(fixture, admin, publicRoot, "中性移动目标");
        await Command(fixture, aliceClient, "workspace.folder.paste", "/api/workspace/content/paste", new { mode = "cut", sourcePosition = newPosition, linkIds = new[] { neutral["LID"] }, targetPosition = movedTo });
        links = await Rows(fixture, "WorkspaceFolderContent");
        var moved = Assert.Single(links, row => Equals(row["PEID"], FolderId(movedTo, links)) && Equals(row["CEID"], childId));
        Assert.Equal(true, moved["IsScopeNeutral"]); Assert.NotEqual(neutral["LID"], moved["LID"]);
        using (var movedRead = await FeatureHttpTestRequests.SendAsync(bobClient, Point(fixture, "workspace.spaces.access"), HttpMethod.Get, "/api/workspace/folders/" + childId)) Assert.Equal(HttpStatusCode.NotFound, movedRead.StatusCode);
        // 显式引用复制仍是分享；随后撤销read或移除最后普通共享边，不得靠中性边维持范围。
        var sharedAt = await Folder(fixture, admin, publicRoot, "显式共享目标");
        await Command(fixture, aliceClient, "workspace.folder.paste", "/api/workspace/content/paste", new { mode = "copy", sourcePosition = movedTo, linkIds = new[] { moved["LID"] }, targetPosition = sharedAt });
        links = await Rows(fixture, "WorkspaceFolderContent"); var shared = Assert.Single(links, row => Equals(row["PEID"], FolderId(sharedAt, links)) && Equals(row["CEID"], childId)); Assert.Equal(false, shared["IsScopeNeutral"]);
        using (var sharedRead = await FeatureHttpTestRequests.SendAsync(bobClient, Point(fixture, "workspace.spaces.access"), HttpMethod.Get, "/api/workspace/folders/" + childId)) Assert.Equal(HttpStatusCode.OK, sharedRead.StatusCode);
        // 历史NULL仍是普通共享边；对象本来可读时，再建浅副本也不遮断原资格。
        var sharedLid = (string)shared["LID"]!;
        using (var scope = fixture.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<ICP.Platform.Persistence.EfCore.EfPlatformDbContext>().Set<Dictionary<string, object?>>("WorkspaceFolderContent")
                .Where(row => EF.Property<string>(row, "LID") == sharedLid).ExecuteUpdateAsync(set => set.SetProperty(row => EF.Property<bool?>(row, "IsScopeNeutral"), (bool?)null));
        await Command(fixture, aliceClient, "workspace.folder.pasteFolderCopy", "/api/workspace/content/folders/copy", new { mode = "copy", sourcePosition = personalRoot, linkIds = new[] { source.LinkIds[^1] }, targetPosition = target, name = "原已共享的浅副本" });
        using (var legacySharedRead = await FeatureHttpTestRequests.SendAsync(bobClient, Point(fixture, "workspace.spaces.access"), HttpMethod.Get, "/api/workspace/folders/" + childId)) Assert.Equal(HttpStatusCode.OK, legacySharedRead.StatusCode);
        await Rule(fixture, admin, "Fc12ChildRead", "workspace.folder", childId, "read", "deny");
        using (var revoked = await FeatureHttpTestRequests.SendAsync(bobClient, Point(fixture, "workspace.spaces.access"), HttpMethod.Get, "/api/workspace/folders/" + childId)) Assert.Equal(HttpStatusCode.NotFound, revoked.StatusCode);
        await Rule(fixture, admin, "Fc12ChildRead", "workspace.folder", childId, "read", "allow");
        using (var removed = await FeatureHttpTestRequests.SendAsync(admin, RowPoint(fixture, "remove"), HttpMethod.Delete, "/api/workspace/content/references/" + shared["LID"], new { position = sharedAt })) Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
        using (var unshared = await FeatureHttpTestRequests.SendAsync(bobClient, Point(fixture, "workspace.spaces.access"), HttpMethod.Get, "/api/workspace/folders/" + childId)) Assert.Equal(HttpStatusCode.NotFound, unshared.StatusCode);
    }

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
            new { mode = "copy", sourcePosition = root, linkIds = new[] { sourceLid }, targetPosition = target, name = "无效", isScopeNeutral = false },
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
