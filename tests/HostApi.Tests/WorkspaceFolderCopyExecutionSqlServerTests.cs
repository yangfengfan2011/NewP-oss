using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Authorization.Contracts;
using Microsoft.Extensions.DependencyInjection;
using ICP.Platform.Testing;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed partial class WorkspaceClipboardSqlServerHttpApiTests
{
    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "浅副本缺或借来源在写前拒绝；真实普通主体候选不要求新对象可读")]
    public async Task ShallowCopy_UsesItsOwnSourceAndReturnsUnreadableCandidateSuccess()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        var root = await Root(fixture, admin);
        var source = await Folder(fixture, admin, root, "320浅副本来源");
        await Folder(fixture, admin, source, "320直接子");
        var target = await Folder(fixture, admin, root, "320浅副本目标");
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "FolderSource320", "浅副本来源", "folder-source320", "浅副本来源账号",
            Point(fixture, "workspace.spaces.access"), Point(fixture, "workspace.folder.pasteFolderCopy"));
        using var ordinary = actor.Client;
        var feature = Point(fixture, "workspace.folder.pasteFolderCopy");
        var declarations = new FeatureHttpTestRequests(fixture);
        var binding = declarations.Declaration("workspace.folder.pasteFolderCopy", feature);
        var borrowed = declarations.Declaration("workspace.folder.create", Point(fixture, "workspace.folder.create"));
        var name = "320创建后不可读浅副本";
        DataScopeDirectSubjectDto[] subjects = [
            new("Creator320", "creator", null, 10, new Dictionary<string, string> { ["read"] = "deny" }, ExpiresAt: "2099-04-01T00:00:00Z"),
            new("Actor320", "user", actor.AccountId, 1, new Dictionary<string, string> { ["read"] = "allow", ["edit"] = "allow", ["authorize"] = "deny" })];
        var authorize = Assert.Single(fixture.FeatureCatalog, point => point.BehaviorKey == "workspace.folder.authorize" && point.ActionScope == "list.rowActions");
        using (var saved = await FeatureHttpTestRequests.SendAsync(admin, authorize, HttpMethod.Put,
            "/api/workspace/content/" + target.LinkIds[^1] + "/permissions?queryScope="
                + Uri.EscapeDataString(JsonSerializer.Serialize(root, new JsonSerializerOptions(JsonSerializerDefaults.Web))),
            new DataScopeObjectConfiguration("workspace.folder", FolderId(target, await Rows(fixture, "WorkspaceFolderContent")), subjects),
            declarations.Declaration("workspace.folder.authorize", authorize)))
            Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);
        var before = Snapshot(await Rows(fixture, "WorkspaceFolder"));
        var input = new { mode = "copy", sourcePosition = root, linkIds = new[] { source.LinkIds[^1] }, targetPosition = target };
        foreach (var wrong in new ActionDataAccessRequirement?[] { null, borrowed })
        {
            using var rejected = await FeatureHttpTestRequests.SendAsync(ordinary, feature, HttpMethod.Post,
                "/api/workspace/content/folders/copy/prepare", input, wrong);
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        }
        // 实际动作身份只送新按钮头；旧entry不能用旧声明补救本次请求。
        using (var request = new HttpRequestMessage(HttpMethod.Post, "/api/workspace/content/folders/copy/prepare") { Content = JsonContent.Create(input) })
        {
            request.Headers.Add("X-NewP-Feature", feature.Position);
            request.Headers.Add("X-NewP-Button", binding.Id);
            using var prepared = await ordinary.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, prepared.StatusCode);
        }
        Assert.Equal(before, Snapshot(await Rows(fixture, "WorkspaceFolder")));
        using (var copied = await FeatureHttpTestRequests.SendAsync(ordinary, feature, HttpMethod.Post,
            "/api/workspace/content/folders/copy", new { input.mode, input.sourcePosition, input.linkIds, input.targetPosition, name, copyParentAuthorization = true }, binding))
        {
            Assert.Equal(HttpStatusCode.OK, copied.StatusCode);
            var receipt = await copied.Content.ReadFromJsonAsync<JsonElement>();
            Assert.True(receipt.GetProperty("succeeded").GetBoolean());
            Assert.False(receipt.TryGetProperty("id", out _));
        }
        var created = Assert.Single(await Rows(fixture, "WorkspaceFolder"), row => Equals(row["ENAME"], name));
        var read = declarations.Declaration("workspace.folder.read", Point(fixture, "workspace.spaces.access"));
        using var hidden = await FeatureHttpTestRequests.SendAsync(ordinary, Point(fixture, "workspace.spaces.access"), HttpMethod.Get,
            "/api/workspace/folders/" + created["EID"], null, read);
        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "两类复制即使角色下限相同，也不能借另一动作的声明准备或提交")]
    public async Task FolderCopy_RejectsTheOtherCopyDeclarationBeforePreparingOrWriting()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        var root = await Root(fixture, admin);
        var source = await Folder(fixture, admin, root, "320交叉来源");
        var beforeFolders = Snapshot(await Rows(fixture, "WorkspaceFolder"));
        var beforeLinks = Snapshot(await Rows(fixture, "WorkspaceFolderContent"));
        foreach (var structure in new[] { false, true })
        {
            var feature = Point(fixture, "workspace.folder." + (structure ? "pasteFolderStructure" : "pasteFolderCopy"));
            var other = Point(fixture, "workspace.folder." + (structure ? "pasteFolderCopy" : "pasteFolderStructure"));
            var declaration = new FeatureHttpTestRequests(fixture).Declaration(structure ? "workspace.folder.pasteFolderCopy" : "workspace.folder.pasteFolderStructure", other);
            var route = "/api/workspace/content/folders/" + (structure ? "copy-structure" : "copy");
            var input = new { mode = "copy", sourcePosition = root, linkIds = new[] { source.LinkIds[^1] }, targetPosition = root, name = "320交叉拒绝" };
            foreach (var path in new[] { route + "/prepare", route })
            {
                using var response = await FeatureHttpTestRequests.SendAsync(admin, feature, HttpMethod.Post, path, input, declaration);
                Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
                Assert.Equal(beforeFolders, Snapshot(await Rows(fixture, "WorkspaceFolder")));
                Assert.Equal(beforeLinks, Snapshot(await Rows(fixture, "WorkspaceFolderContent")));
            }
        }
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "名称准备和复制提交拒绝缺声明，旧普通创建声明不能串用，也不能产生部分副本")]
    public async Task FolderCopy_RequiresItsOwnDeclaredSourceBeforePreparingOrWriting()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        var root = await Root(fixture, admin);
        var source = await Folder(fixture, admin, root, "320来源");
        await Folder(fixture, admin, source, "320直接子");
        var target = await Folder(fixture, admin, root, "320目标");
        var beforeFolders = Snapshot(await Rows(fixture, "WorkspaceFolder"));
        var beforeLinks = Snapshot(await Rows(fixture, "WorkspaceFolderContent"));
        foreach (var (route, behavior) in new[] { ("copy", "workspace.folder.pasteFolderCopy"), ("copy-structure", "workspace.folder.pasteFolderStructure") })
        {
            var feature = Point(fixture, behavior);
            var input = new { mode = "copy", sourcePosition = root, linkIds = new[] { source.LinkIds[^1] }, targetPosition = target };
            using var missing = await FeatureHttpTestRequests.SendAsync(admin, feature, HttpMethod.Post,
                "/api/workspace/content/folders/" + route + "/prepare", input);
            Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
            Assert.Equal(beforeFolders, Snapshot(await Rows(fixture, "WorkspaceFolder")));
            Assert.Equal(beforeLinks, Snapshot(await Rows(fixture, "WorkspaceFolderContent")));
            var submit = new { input.mode, input.sourcePosition, input.linkIds, input.targetPosition, name = "320副本" + route };
            using var missingWrite = await FeatureHttpTestRequests.SendAsync(admin, feature, HttpMethod.Post,
                "/api/workspace/content/folders/" + route, submit);
            Assert.Equal(HttpStatusCode.BadRequest, missingWrite.StatusCode);
            var create = Point(fixture, "workspace.folder.create");
            var borrowed = new FeatureHttpTestRequests(fixture).Declaration("workspace.folder.create", create);
            using var crossed = await FeatureHttpTestRequests.SendAsync(admin, feature, HttpMethod.Post,
                "/api/workspace/content/folders/" + route, submit, borrowed);
            Assert.Equal(HttpStatusCode.BadRequest, crossed.StatusCode);
            Assert.Equal(beforeFolders, Snapshot(await Rows(fixture, "WorkspaceFolder")));
            Assert.Equal(beforeLinks, Snapshot(await Rows(fixture, "WorkspaceFolderContent")));
        }
    }
}
