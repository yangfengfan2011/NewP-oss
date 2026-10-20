using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed partial class WorkspaceClipboardSqlServerHttpApiTests
{
    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "真实引用按钮在后项不可读时整批无写，允许后只移除选定LID而不消费对象delete")]
    public async Task ReferenceButtons_RejectTheLaterEndpointAtomicallyAndRemoveOnlySelectedLinks()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        var root = await Root(fixture, admin);
        var parent = await Folder(fixture, admin, root, "引用按钮来源");
        var other = await Folder(fixture, admin, root, "引用按钮另一出现");
        var folder = await Folder(fixture, admin, parent, "引用按钮文件夹");
        await Command(fixture, admin, "workspace.folder.createPart", "/api/workspace/content/parts",
            new { position = parent, values = new { ECODE = "REFERENCE-BUTTON", ENAME = "引用按钮零组件" } });
        var partLink = Assert.Single(await Rows(fixture, "WorkspaceFolderContent"), row => Equals(row["ChildModelKey"], "Part"));
        var partId = (string)partLink["CEID"]!;
        // 原add是独立纯API；不把它补成按钮，也不改变目标read及父edit规则。
        await Command(fixture, admin, "workspace.folder.add", "/api/workspace/content/references",
            new { position = other, target = new { modelKey = "Part", id = partId } });
        var allLinks = await Rows(fixture, "WorkspaceFolderContent");
        var otherPartLink = Assert.Single(allLinks, row => Equals(row["ChildModelKey"], "Part") && !Equals(row["LID"], partLink["LID"]));
        var selection = new[] { folder.LinkIds[^1], (string)partLink["LID"]! };
        var batch = Point(fixture, "workspace.folder.removeSelected");
        var single = RowPoint(fixture, "remove");
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "ReferenceButton", "引用按钮", "reference-button", "引用按钮账号",
            Point(fixture, "workspace.spaces.access"), batch, single);
        using var ordinary = actor.Client;
        var plan = fixture.Services.GetRequiredService<CanonicalModulePlan>();
        var batchButton = Assert.Single(plan.ActionDataAccessCatalog, item => item.ViewKey == "workspace.folder.list"
            && item.Surface == "pageActions" && item.ActionKey == "removeSelected" && item.FeaturePositions.Contains(batch.Position));
        var rowButton = Assert.Single(plan.ActionDataAccessCatalog, item => item.ViewKey == "workspace.folder.list"
            && item.Surface == "rowActions" && item.ActionKey == "remove" && item.FeaturePositions.Contains(single.Position));
        await Rule(fixture, admin, "ReferenceLateHidden", "part.part", partId, "read", "deny");
        var linksBefore = Snapshot(await Rows(fixture, "WorkspaceFolderContent"));
        var auditsBefore = Snapshot(await Rows(fixture, "BusinessChange"));
        using (var denied = await SendButton(batch, batchButton.Id, HttpMethod.Post, "/api/workspace/content/references/remove",
            new { position = parent, linkIds = selection }))
            await Problem(denied, HttpStatusCode.NotFound, "Workspace.TargetUnavailable");
        Assert.Equal(linksBefore, Snapshot(await Rows(fixture, "WorkspaceFolderContent")));
        Assert.Equal(auditsBefore, Snapshot(await Rows(fixture, "BusinessChange")));
        foreach (var button in new[] { (string?)null, rowButton.Id })
        {
            using var rejected = await SendButton(batch, button, HttpMethod.Post, "/api/workspace/content/references/remove",
                new { position = parent, linkIds = selection });
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        }
        Assert.Equal(linksBefore, Snapshot(await Rows(fixture, "WorkspaceFolderContent")));
        Assert.Equal(auditsBefore, Snapshot(await Rows(fixture, "BusinessChange")));
        await Rule(fixture, admin, "ReferenceLateHidden", "part.part", partId, "read", "allow");
        await Rule(fixture, admin, "ReferenceDoesNotDelete", "part.part", partId, "delete", "deny");
        var partsBefore = Snapshot(await Rows(fixture, "Part"));
        var foldersBefore = Snapshot(await Rows(fixture, "WorkspaceFolder"));
        using (var removed = await SendButton(batch, batchButton.Id, HttpMethod.Post, "/api/workspace/content/references/remove",
            new { position = parent, linkIds = selection })) Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
        Assert.DoesNotContain(await Rows(fixture, "WorkspaceFolderContent"), row => selection.Contains((string)row["LID"]!));
        Assert.Contains(await Rows(fixture, "WorkspaceFolderContent"), row => Equals(row["LID"], otherPartLink["LID"]));
        using (var removed = await SendButton(single, rowButton.Id, HttpMethod.Delete,
            "/api/workspace/content/references/" + (string)otherPartLink["LID"]!, new { position = other }))
            Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
        Assert.Equal(partsBefore, Snapshot(await Rows(fixture, "Part")));
        Assert.Equal(foldersBefore, Snapshot(await Rows(fixture, "WorkspaceFolder")));
        Assert.Equal(3, (await Rows(fixture, "BusinessChange")).Count(row => Equals(row["EventType"], "workspace.reference.removed")));

        // 只从真实模块投影取得按钮ID；不按URL推测声明，不改raw helper给缺失身份兜底。
        async Task<HttpResponseMessage> SendButton(FeaturePoint feature, string? button, HttpMethod method, string path, object body)
        {
            using var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(body, options: new JsonSerializerOptions()) };
            request.Headers.Add("X-NewP-Feature", feature.Position);
            if (button is not null) request.Headers.Add("X-NewP-Button", button);
            return await ordinary.SendAsync(request);
        }
    }
}
