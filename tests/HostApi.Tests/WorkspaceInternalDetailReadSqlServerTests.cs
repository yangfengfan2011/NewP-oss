using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ICP.Platform.Testing;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed partial class WorkspaceClipboardSqlServerHttpApiTests
{
    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "单对象复制准备与编辑预取使用各自内部详情来源，保留原业务动作拒绝")]
    public async Task SingleObjectPrepareAndEditValues_UseTheirRegisteredInternalDetailSources()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        var root = await Root(fixture, admin);
        var source = await Folder(fixture, admin, root, "共享详情来源309");
        var target = await Folder(fixture, admin, root, "共享详情目标309");
        await Command(fixture, admin, "workspace.folder.createPart", "/api/workspace/content/parts",
            new { position = source, values = new { ECODE = "INTERNAL-309", ENAME = "共享详情309" } });
        var link = Assert.Single(await Rows(fixture, "WorkspaceFolderContent"), row => Equals(row["ChildModelKey"], "Part"));
        var id = (string)link["CEID"]!; var lid = (string)link["LID"]!;
        var edit = RowPoint(fixture, "edit"); var copy = Point(fixture, "workspace.folder.pasteObjectCopy");
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "InternalDetail309", "内部详情",
            "internal-detail309", "内部详情主体", Point(fixture, "workspace.spaces.access"), edit, copy);
        using var ordinary = actor.Client;
        using (var values = await FeatureHttpTestRequests.SendAsync(ordinary, edit, HttpMethod.Post,
            "/api/workspace/content/" + lid + "/edit-values", new { position = source }))
        {
            Assert.Equal(HttpStatusCode.OK, values.StatusCode);
            Assert.Equal("INTERNAL-309", (await values.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("fields").GetProperty("ECODE").GetString());
        }
        var input = new { mode = "copy", sourcePosition = source, linkIds = new[] { lid }, targetPosition = target };
        using (var prepared = await Send(fixture, ordinary, "workspace.folder.pasteObjectCopy", "objects/copy/prepare", input))
        {
            Assert.Equal(HttpStatusCode.OK, prepared.StatusCode);
            Assert.False(string.IsNullOrWhiteSpace((await prepared.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("operationId").GetString()));
        }
        var count = (await Rows(fixture, "WorkspaceObjectCopy")).Length;
        await Rule(fixture, admin, "InternalDetailCopy309", "part.part", id, "partObjectCopy", "deny");
        using var denied = await Send(fixture, ordinary, "workspace.folder.pasteObjectCopy", "objects/copy/prepare", input);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Equal(count, (await Rows(fixture, "WorkspaceObjectCopy")).Length);
    }
}
