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
    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "首文档已确认后撤销另一源资格回滚整批，最终提交与取消竞争只报告同一个持久赢家")]
    public async Task ObjectCopy_DocumentBatchFinalRightsAndCancellationUseTheWholeBatchOutcome()
    {
        var graph = new ObjectCopyWriteFault();
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(services => services.AddDbContext<EfPlatformDbContext>((_, options) => options.AddInterceptors(graph)), includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync(); var root = await Root(fixture, admin);
        var source = await Folder(fixture, admin, root, "批次最终资格源"); var target = await Folder(fixture, admin, root, "批次最终资格目标");
        string[] links = [await CreateBatchDocument(fixture, admin, source, 1), await CreateBatchDocument(fixture, admin, source, 2)];
        var second = Assert.Single(await Rows(fixture, "WorkspaceFolderContent"), row => Equals(row["LID"], links[1]));
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "BatchFinalActor", "批次最终主体", "batch-final-actor", "批次最终账号",
            Point(fixture, "workspace.spaces.access"), Point(fixture, "workspace.folder.pasteObjectCopy"), Point(fixture, "workspace.folder.resolveObjectCopy"), DocFiles.Point(fixture, "document.document.read"));
        using var ordinary = actor.Client;
        foreach (var revoke in new[] { true, false })
        {
            var prepared = await PrepareDocumentBatch(fixture, ordinary, source, target, links); var id = prepared.GetProperty("operationId").GetString();
            var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            fixture.Fault.AfterRegister = async (change, token) => { if (change.EventType == "workspace.object.copied") { reached.TrySetResult(); await release.Task.WaitAsync(token); } };
            var pending = Send(fixture, ordinary, "workspace.folder.pasteObjectCopy", "objects/copy", new { operationId = id, values = BatchValues(links, "BATCH-FINAL") });
            Task<JsonElement>? cancelled = null;
            try
            {
                await reached.Task.WaitAsync(TimeSpan.FromSeconds(30));
                if (revoke) await Rule(fixture, admin, "BatchLaterSourceRevoke", "document.document", (string)second["CEID"]!, "documentObjectCopy", "deny");
                else
                {
                    graph.WatchEditGraph = true;
                    cancelled = Command(fixture, ordinary, "workspace.folder.pasteObjectCopy", "/api/workspace/content/objects/copy/cancel", new { operationId = id });
                    await graph.EditGraphReached.Task.WaitAsync(TimeSpan.FromSeconds(20));
                }
                release.TrySetResult(); using var response = await pending;
                if (revoke) await Problem(response, HttpStatusCode.Forbidden, "Document.CopyDenied");
                else
                {
                    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                    var result = await cancelled!.WaitAsync(TimeSpan.FromSeconds(20));
                    Assert.Equal("committed", result.GetProperty("state").GetString()); Assert.All(result.GetProperty("items").EnumerateArray(), row => Assert.True(row.GetProperty("succeeded").GetBoolean()));
                }
            }
            finally { release.TrySetResult(); (await pending).Dispose(); fixture.Fault.AfterRegister = null; graph.WatchEditGraph = false; }
            await CleanDocumentCopies(fixture); Assert.Equal(revoke ? 2 : 4, (await Rows(fixture, "Document")).Length);
            Assert.Equal(revoke ? 4 : 8, Directory.EnumerateFiles(fixture.StorageRoot).Count());
            if (revoke) await Rule(fixture, admin, "BatchLaterSourceRevoke", "document.document", (string)second["CEID"]!, "documentObjectCopy", "allow");
        }
        Assert.Equal(2, (await Rows(fixture, "BusinessChange")).Count(row => Equals(row["EventType"], "workspace.object.copied")));
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "整批业务提交前失败与提交后未知分别按持久结果收敛；后续不同文档交叉删除实际回收不串用")]
    public async Task ObjectCopy_DocumentBatchUnknownCommitAndCrossDocumentReclamationPreserveTheSurvivors()
    {
        var fault = new CopyCommitFault();
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(services => services.AddDbContext<EfPlatformDbContext>((_, options) => options.AddInterceptors(fault)), includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync(); var root = await Root(fixture, admin);
        var source = await Folder(fixture, admin, root, "未知批次源"); var target = await Folder(fixture, admin, root, "未知批次目标");
        string[] links = [await CreateBatchDocument(fixture, admin, source, 1), await CreateBatchDocument(fixture, admin, source, 2)];
        string? committed = null;
        foreach (var after in new[] { false, true })
        {
            var prepared = await PrepareDocumentBatch(fixture, admin, source, target, links); var id = prepared.GetProperty("operationId").GetString();
            fault.Arm("business", after);
            using var failed = await Send(fixture, admin, "workspace.folder.pasteObjectCopy", "objects/copy", new { operationId = id, values = BatchValues(links, "BATCH-UNKNOWN") });
            Assert.False(failed.IsSuccessStatusCode); Assert.Equal(1, fault.Injected);
            var result = await Command(fixture, admin, "workspace.folder.resolveObjectCopy", "/api/workspace/content/objects/copy/result", new { operationId = id });
            Assert.Equal(after, result.GetProperty("succeeded").GetBoolean()); Assert.Equal(2, result.GetProperty("items").GetArrayLength());
            var cancelled = await Command(fixture, admin, "workspace.folder.pasteObjectCopy", "/api/workspace/content/objects/copy/cancel", new { operationId = id });
            Assert.Equal(after ? "committed" : "cancelled", cancelled.GetProperty("state").GetString());
            await CleanDocumentCopies(fixture); Assert.Equal(after ? 4 : 2, (await Rows(fixture, "Document")).Length);
            Assert.Equal(after ? 8 : 4, Directory.EnumerateFiles(fixture.StorageRoot).Count());
            if (after) committed = id;
        }
        await fixture.RestartWithActionsAsync(new Dictionary<string, bool>());
        using var resumed = fixture.CreateClient(); await BusinessAuditHostFixture.RefreshAntiforgeryAsync(resumed);
        using (var login = await resumed.PostAsJsonAsync("/api/auth/login", new { ECODE = "admin", Password = "Audit-test-secure-938!" })) Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        await BusinessAuditHostFixture.RefreshAntiforgeryAsync(resumed);
        var recovered = await Command(fixture, resumed, "workspace.folder.resolveObjectCopy", "/api/workspace/content/objects/copy/result", new { operationId = committed });
        Assert.Equal("committed", recovered.GetProperty("state").GetString());
        var copies = await Rows(fixture, "Document"); var first = Assert.Single(copies, row => Equals(row["ECODE"], "BATCH-UNKNOWN-0"));
        var copyLink = Assert.Single(await Rows(fixture, "WorkspaceFolderContent"), row => Equals(row["CEID"], first["EID"]));
        await Command(fixture, resumed, "workspace.folder.deleteObjects", "/api/workspace/content/delete", new { position = target, linkIds = new[] { copyLink["LID"] } });
        await Command(fixture, resumed, "workspace.folder.deleteObjects", "/api/workspace/content/delete", new { position = source, linkIds = new[] { links[1] } });
        await CleanDocumentCopies(fixture);
        Assert.Equal(2, (await Rows(fixture, "Document")).Length); Assert.Equal(4, Directory.EnumerateFiles(fixture.StorageRoot).Count());
        foreach (var (code, ordinal) in new[] { ("BATCH-SOURCE-1", 1), ("BATCH-UNKNOWN-1", 2) })
        {
            var survivor = Assert.Single(await Rows(fixture, "Document"), row => Equals(row["ECODE"], code));
            var list = await FeatureHttpTestRequests.ReadAsync(resumed, DocFiles.Point(fixture, "document.document.read"), $"/api/document/documents/{survivor["EID"]}/files");
            var files = list.GetProperty("files").EnumerateArray().ToArray(); Assert.Equal(2, files.Length);
            for (var index = 0; index < files.Length; index++)
            {
                using var content = await FeatureHttpTestRequests.SendAsync(resumed, DocFiles.Point(fixture, "document.document.download"), HttpMethod.Get,
                    $"/api/document/documents/{survivor["EID"]}/files/{files[index].GetProperty("fileLinkId").GetString()}/content");
                Assert.Equal(HttpStatusCode.OK, content.StatusCode); Assert.Equal(new byte[] { (byte)ordinal, (byte)index, 97, 98 }, await content.Content.ReadAsByteArrayAsync());
            }
        }
    }
}