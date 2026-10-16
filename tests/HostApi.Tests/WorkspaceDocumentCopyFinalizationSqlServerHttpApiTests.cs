using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using DocFiles = ICP.Platform.HostApi.Tests.DocumentFilesSqlServerHttpApiTests;

namespace ICP.Platform.HostApi.Tests;

public sealed partial class WorkspaceClipboardSqlServerHttpApiTests
{
    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "Document普通创建赢得编码竞争时复制定位ECODE并回滚业务图，改编码复用准备后可确认")]
    public async Task ObjectCopy_DocumentConcurrentCreateRetainsCodeFeedbackAndCorrectablePreparation()
    {
        var barrier = new ObjectCopyWriteFault { PauseTable = "Document" };
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(services => services.AddDbContext<EfPlatformDbContext>((_, options) => options.AddInterceptors(barrier)), includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync(); var root = await Root(fixture, admin);
        var source = await Folder(fixture, admin, root, "文档编码来源"); var target = await Folder(fixture, admin, root, "文档编码目标");
        var row = await CreateCopyDocument(fixture, admin, source, "DOC-CODE-SOURCE");
        var operation = await PrepareDocumentCopy(fixture, admin, source, target, row.LinkId);
        barrier.Pause = true;
        var pending = Send(fixture, admin, "workspace.folder.pasteObjectCopy", "objects/copy", new { operationId = operation, values = new { ECODE = "DOC-CODE-RACE", ENAME = "主动名称" } });
        try
        {
            await barrier.Reached.Task.WaitAsync(TimeSpan.FromSeconds(20)); barrier.Pause = false;
            var point = Assert.Single(fixture.FeatureCatalog, item => item.ModuleId == "Document" && item.BehaviorKey == "document.document.create" && item.ActionScope == "list.pageActions");
            using var winner = await FeatureHttpTestRequests.SendAsync(admin, point, HttpMethod.Post, "/api/document/documents", new { ECODE = "DOC-CODE-RACE", ENAME = "正常创建" });
            Assert.Equal(HttpStatusCode.Created, winner.StatusCode); barrier.Release.TrySetResult();
            using var loser = await pending; Assert.Equal(HttpStatusCode.Conflict, loser.StatusCode);
            var problem = await loser.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Contains(problem.GetProperty("fieldErrors").EnumerateArray(), item => item.GetProperty("field").GetString() == "ECODE");
        }
        finally { barrier.Pause = false; barrier.Release.TrySetResult(); (await pending).Dispose(); }
        Assert.Equal(2, (await Rows(fixture, "Document")).Length); Assert.Equal(3, (await Rows(fixture, "FileUsage")).Length);
        Assert.DoesNotContain(await Rows(fixture, "BusinessChange"), item => Equals(item["EventType"], "workspace.object.copied"));
        using var corrected = await Send(fixture, admin, "workspace.folder.pasteObjectCopy", "objects/copy", new { operationId = operation, values = new { ECODE = "DOC-CODE-CORRECTED", ENAME = "主动名称" } });
        Assert.Equal(HttpStatusCode.OK, corrected.StatusCode); Assert.Equal(3, (await Rows(fixture, "Document")).Length);
        Assert.Equal("主动名称", Assert.Single(await Rows(fixture, "Document"), item => Equals(item["ECODE"], "DOC-CODE-CORRECTED"))["ENAME"]);
        Assert.Equal(6, (await Rows(fixture, "FileRecord")).Length); Assert.Equal(6, (await Rows(fixture, "FileUsage")).Length);
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "Document owner已确认文件后源撤权仍整图拒绝，取消与最终提交竞争只报告持久赢家")]
    public async Task ObjectCopy_DocumentFinalRevocationRollsBackAndCommitWinsLateCancellation()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync(); var root = await Root(fixture, admin);
        var source = await Folder(fixture, admin, root, "文档最后资格源"); var target = await Folder(fixture, admin, root, "文档最后资格目标");
        var row = await CreateCopyDocument(fixture, admin, source, "DOC-LATE-RIGHTS");
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "DocLateActor", "文档最终资格", "doc-late-actor", "最终资格主体",
            Point(fixture, "workspace.spaces.access"), Point(fixture, "workspace.folder.pasteObjectCopy"), DocFiles.Point(fixture, "document.document.read"));
        using var ordinary = actor.Client;
        foreach (var revoke in new[] { true, false })
        {
            var operation = await PrepareDocumentCopy(fixture, ordinary, source, target, row.LinkId);
            var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            fixture.Fault.AfterRegister = async (change, token) => { if (change.EventType == "workspace.object.copied") { reached.TrySetResult(); await release.Task.WaitAsync(token); } };
            var pending = Send(fixture, ordinary, "workspace.folder.pasteObjectCopy", "objects/copy", new { operationId = operation, values = new { ECODE = "DOC-LATE-COPY", ENAME = "最终副本" } });
            Task<JsonElement>? cancellation = null;
            try
            {
                await reached.Task.WaitAsync(TimeSpan.FromSeconds(20));
                if (revoke) await Rule(fixture, admin, "DocumentLateRevoke", "document.document", row.Id, "documentObjectCopy", "deny");
                else cancellation = Command(fixture, ordinary, "workspace.folder.pasteObjectCopy", "/api/workspace/content/objects/copy/cancel", new { operationId = operation });
                release.TrySetResult(); using var response = await pending;
                if (revoke) await Problem(response, HttpStatusCode.Forbidden, "Workspace.ObjectCopyDenied");
                else { Assert.Equal(HttpStatusCode.OK, response.StatusCode); Assert.Equal("committed", (await cancellation!.WaitAsync(TimeSpan.FromSeconds(10))).GetProperty("state").GetString()); }
            }
            finally { release.TrySetResult(); (await pending).Dispose(); fixture.Fault.AfterRegister = null; }
            await CleanDocumentCopies(fixture);
            Assert.Equal(revoke ? 1 : 2, (await Rows(fixture, "Document")).Length);
            Assert.Equal(revoke ? 3 : 6, Directory.EnumerateFiles(fixture.StorageRoot).Count());
            if (revoke) await Rule(fixture, admin, "DocumentLateRevoke", "document.document", row.Id, "documentObjectCopy", "allow");
        }
        Assert.Single(await Rows(fixture, "BusinessChange"), item => Equals(item["EventType"], "workspace.object.copied"));
    }
}
