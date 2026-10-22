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
    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "真实copy/cut按钮保留父与端点独立动作、复制用途OR、提交重判及关系移动原子性")]
    public async Task ClipboardButtons_PreserveParentAndEndpointRulesWithoutPartialReferenceWrites()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        var root = await Root(fixture, admin);
        var source = await Folder(fixture, admin, root, "按钮剪贴板来源");
        var target = await Folder(fixture, admin, root, "按钮剪贴板复制目标");
        var moveTarget = await Folder(fixture, admin, root, "按钮剪贴板移动目标");
        await Command(fixture, admin, "workspace.folder.createPart", "/api/workspace/content/parts",
            new { position = source, values = new { ECODE = "CLIPBOARD-BUTTON", ENAME = "按钮剪贴板零组件" } });
        var links = await Rows(fixture, "WorkspaceFolderContent");
        var part = Assert.Single(links, row => Equals(row["ChildModelKey"], "Part"));
        var partId = (string)part["CEID"]!;
        var sourceId = FolderId(source, links);
        var selected = new[] { (string)part["LID"]! };
        var copy = RowPoint(fixture, "copy");
        var cut = RowPoint(fixture, "cut");
        var paste = Point(fixture, "workspace.folder.paste");
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "ClipboardButton", "剪贴板按钮", "clipboard-button", "剪贴板按钮账号",
            Point(fixture, "workspace.spaces.access"), copy, cut, paste);
        using var ordinary = actor.Client;
        var plan = fixture.Services.GetRequiredService<CanonicalModulePlan>();
        var copyId = Button(copy, "rowActions", "copy");
        var cutId = Button(cut, "rowActions", "cut");
        var pasteId = Button(paste, "pageActions", "paste");
        var beforeLinks = Snapshot(links);
        var beforeAudit = Snapshot((await Rows(fixture, "BusinessChange")).Where(row => ((string)row["EventType"]!).StartsWith("workspace.reference.", StringComparison.Ordinal)).ToArray());
        var beforeParts = Snapshot(await Rows(fixture, "Part"));
        await Rule(fixture, admin, "ClipboardSourceParent", "workspace.folder", sourceId, "edit", "deny");
        using (var allowed = await Send(copy, copyId, "clipboard/validate", new { mode = "copy", sourcePosition = source, linkIds = selected }))
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        using (var denied = await Send(cut, cutId, "clipboard/cut/validate", new { mode = "cut", sourcePosition = source, linkIds = selected }))
            await Problem(denied, HttpStatusCode.Forbidden, "Workspace.ParentWriteDenied");
        await Rule(fixture, admin, "ClipboardSourceParent", "workspace.folder", sourceId, "edit", "allow");
        await Rule(fixture, admin, "ClipboardSelectedEdit", "part.part", partId, "edit", "deny");
        using (var denied = await Send(cut, cutId, "clipboard/cut/validate", new { mode = "cut", sourcePosition = source, linkIds = selected }))
            await Problem(denied, HttpStatusCode.Forbidden, "Workspace.ObjectWriteDenied");
        await Rule(fixture, admin, "ClipboardSelectedEdit", "part.part", partId, "edit", "allow");
        foreach (var id in new[] { (string?)null, cutId })
        {
            using var missing = await Send(copy, id, "clipboard/validate", new { mode = "copy", sourcePosition = source, linkIds = selected });
            Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        }
        using (var empty = await Send(copy, copyId, "clipboard/validate", new { mode = "copy", sourcePosition = source, linkIds = Array.Empty<string>() }))
            Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        using (var wrongSource = await Send(copy, copyId, "clipboard/validate", new { mode = "copy", sourcePosition = target, linkIds = selected }))
            await Problem(wrongSource, HttpStatusCode.NotFound, "Workspace.ReferenceUnavailable");
        await Rule(fixture, admin, "ClipboardReferenceCopy", "part.part", partId, "partCopy", "deny");
        await Rule(fixture, admin, "ClipboardObjectCopy", "part.part", partId, "partObjectCopy", "allow");
        using (var allowed = await Send(copy, copyId, "clipboard/validate", new { mode = "copy", sourcePosition = source, linkIds = selected }))
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        using (var denied = await Send(paste, pasteId, "paste", new { mode = "copy", sourcePosition = source, linkIds = selected, targetPosition = target }))
            await Problem(denied, HttpStatusCode.Forbidden, "Workspace.ObjectCopyDenied");
        await Rule(fixture, admin, "ClipboardReferenceCopy", "part.part", partId, "partCopy", "allow");
        await Rule(fixture, admin, "ClipboardEndpointRead", "part.part", partId, "read", "deny");
        using (var denied = await Send(paste, pasteId, "paste", new { mode = "copy", sourcePosition = source, linkIds = selected, targetPosition = target }))
            await Problem(denied, HttpStatusCode.NotFound, "Workspace.TargetUnavailable");
        Assert.Equal(beforeLinks, Snapshot(await Rows(fixture, "WorkspaceFolderContent")));
        Assert.Equal(beforeAudit, Snapshot((await Rows(fixture, "BusinessChange")).Where(row => ((string)row["EventType"]!).StartsWith("workspace.reference.", StringComparison.Ordinal)).ToArray()));
        await Rule(fixture, admin, "ClipboardEndpointRead", "part.part", partId, "read", "allow");
        using (var allowed = await Send(paste, pasteId, "paste", new { mode = "copy", sourcePosition = source, linkIds = selected, targetPosition = target }))
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        var afterCopy = await Rows(fixture, "WorkspaceFolderContent");
        Assert.Contains(afterCopy, row => Equals(row["LID"], selected[0]));
        Assert.Single(afterCopy, row => Equals(row["PEID"], FolderId(target, afterCopy)) && Equals(row["CEID"], partId));
        using (var allowed = await Send(cut, cutId, "clipboard/cut/validate", new { mode = "cut", sourcePosition = source, linkIds = selected }))
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        using (var same = await Send(paste, pasteId, "paste", new { mode = "cut", sourcePosition = source, linkIds = selected, targetPosition = source }))
        {
            Assert.Equal(HttpStatusCode.OK, same.StatusCode);
            Assert.True((await same.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("noChange").GetBoolean());
        }
        using (var moved = await Send(paste, pasteId, "paste", new { mode = "cut", sourcePosition = source, linkIds = selected, targetPosition = moveTarget }))
            Assert.Equal(HttpStatusCode.OK, moved.StatusCode);
        var afterMove = await Rows(fixture, "WorkspaceFolderContent");
        Assert.DoesNotContain(afterMove, row => Equals(row["LID"], selected[0]));
        Assert.Single(afterMove, row => Equals(row["PEID"], FolderId(moveTarget, afterMove)) && Equals(row["CEID"], partId));
        Assert.Equal(beforeParts, Snapshot(await Rows(fixture, "Part")));
        Assert.Single(await Rows(fixture, "BusinessChange"), row => Equals(row["EventType"], "workspace.reference.copied"));
        Assert.Single(await Rows(fixture, "BusinessChange"), row => Equals(row["EventType"], "workspace.reference.moved"));

        string Button(FeaturePoint point, string surface, string key) => Assert.Single(plan.ActionDataAccessCatalog, item => item.ViewKey == "workspace.folder.list"
            && item.Surface == surface && item.ActionKey == key && item.FeaturePositions.Contains(point.Position)).Id;

        async Task<HttpResponseMessage> Send(FeaturePoint point, string? button, string suffix, object body)
        {
            var linksBeforeRequest = Snapshot(await Rows(fixture, "WorkspaceFolderContent"));
            var auditBeforeRequest = Snapshot(await Rows(fixture, "BusinessChange"));
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/workspace/content/" + suffix)
                { Content = JsonContent.Create(body, options: new JsonSerializerOptions()) };
            request.Headers.Add("X-NewP-Feature", point.Position);
            if (button is not null) request.Headers.Add("X-NewP-Button", button);
            var response = await ordinary.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                Assert.Equal(linksBeforeRequest, Snapshot(await Rows(fixture, "WorkspaceFolderContent")));
                Assert.Equal(auditBeforeRequest, Snapshot(await Rows(fixture, "BusinessChange")));
            }
            return response;
        }
    }
}
