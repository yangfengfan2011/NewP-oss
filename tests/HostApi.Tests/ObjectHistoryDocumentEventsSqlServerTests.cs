using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ICP.Platform.Modules.Workspace;
using ICP.Platform.Persistence.Abstractions;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static ICP.Platform.HostApi.Tests.EntityOwnershipBackfillSqlServerHttpApiTests;

namespace ICP.Platform.HostApi.Tests;

public sealed class ObjectHistoryDocumentEventsSqlServerTests
{
    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "真实图文档首版regular与主附件创建、内部真副本和释放均记录同scope事实")]
    public async Task DocumentFilesAndInitialRegular_RecordRealCreationCopyAndReleaseFacts()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        var historyRequests = new ObjectHistoryHttpTestRequests(fixture);
        await InitializeAsync(fixture, admin);
        var position = await ContainerAsync(fixture, admin);
        var source = new WorkspacePosition(position.SpaceId, position.RootFolderId, position.LinkIds);
        var targetFolder = await CreateAsync(fixture, admin, position, "WorkspaceFolder", "folders", "create", "文档副本目的296");
        var target = source with { LinkIds = [.. source.LinkIds, targetFolder.LinkId] };
        var values = new { ECODE = "HISTORY-FILES-296", ENAME = "文件履历原对象", Description = "真实主附件" };
        var main = await DocumentFilesSqlServerHttpApiTests.StageAsync(fixture, admin, source, values, [1, 2, 3], "main.pdf");
        var attachment = await DocumentFilesSqlServerHttpApiTests.StageAsync(fixture, admin, source, values, [4, 5, 6], "attachment.pdf");
        using (var created = await historyRequests.SendAsync(admin, Point(fixture, "workspace.folder.createDocument"), HttpMethod.Post,
            "/api/workspace/content/documents", new { position = source, values,
                uploads = new { mainUploadId = main.GetProperty("uploadId").GetString(), attachmentUploadIds = new[] { attachment.GetProperty("uploadId").GetString() } } }))
            await ReadOkAsync(created);
        var master = Assert.Single(await RowsAsync(fixture, "Document"));
        var id = (string)master["EID"]!;
        var link = Assert.Single(await RowsAsync(fixture, "WorkspaceFolderContent"), row => Equals(row["CEID"], id));
        var initialized = Assert.Single(await EventsAsync(fixture, id), row => Equals(row["EventType"], "document.iteration.initialized"));
        Assert.Equal(master["CreatedAt"], initialized["OccurredAt"]);
        Assert.Equal(master["CreatedBy"], initialized["ActorId"]);
        AssertField(initialized, "VersionLabel", "1");
        AssertField(initialized, "IterationKind", "regular");
        AssertField(initialized, "IterationOrdinal", "1");
        await AssertFileEventAsync(fixture, id, "document.files.created", 2);
        Assert.DoesNotContain(await EventsAsync(fixture, id), row => Equals(row["EventType"], "document.files.copied"));

        string operationId;
        using (var prepared = await historyRequests.SendAsync(admin, Point(fixture, "workspace.folder.pasteObjectCopy"), HttpMethod.Post,
            "/api/workspace/content/objects/copy/prepare", new { mode = "copy", sourcePosition = source, linkIds = new[] { (string)link["LID"]! }, targetPosition = target }))
            operationId = (await ReadOkAsync(prepared)).GetProperty("operationId").GetString()!;
        using (var copied = await historyRequests.SendAsync(admin, Point(fixture, "workspace.folder.pasteObjectCopy"), HttpMethod.Post,
            "/api/workspace/content/objects/copy", new { operationId, values = new { ECODE = "HISTORY-FILES-COPY-296", ENAME = "文件履历真副本" } }))
            await ReadOkAsync(copied);
        var copy = Assert.Single(await RowsAsync(fixture, "Document"), row => !Equals(row["EID"], id));
        var copyId = (string)copy["EID"]!;
        var copyRecords = await EventsAsync(fixture, copyId);
        Assert.Single(copyRecords, row => Equals(row["EventType"], "document.iteration.initialized"));
        Assert.Single(copyRecords, row => Equals(row["EventType"], "document.created"));
        Assert.DoesNotContain(copyRecords, row => Equals(row["EventType"], "document.files.created"));
        await AssertFileEventAsync(fixture, copyId, "document.files.copied", 2);
        var copyLink = Assert.Single(await RowsAsync(fixture, "WorkspaceFolderContent"), row => Equals(row["CEID"], copyId));
        using (var deleted = await historyRequests.SendAsync(admin, Point(fixture, "workspace.folder.deleteObject"), HttpMethod.Delete,
            "/api/workspace/content/" + copyLink["LID"], new { position = target })) await ReadOkAsync(deleted);
        var released = Assert.Single(await EventsAsync(fixture, copyId), row => Equals(row["EventType"], "document.files.released"));
        AssertField(released, "FileCount", "2");
        var deletedRecord = Assert.Single(await EventsAsync(fixture, copyId), row => Equals(row["EventType"], "document.deleted"));
        Assert.Equal(deletedRecord["OccurredAt"], released["OccurredAt"]);
        Assert.Equal(deletedRecord["ActorId"], released["ActorId"]);
        Assert.Equal(2, (await RowsAsync(fixture, "DocumentFileLink")).Length);
        Assert.Single(await RowsAsync(fixture, "Document"));
        Assert.Equal(2, (await RowsAsync(fixture, "FileUsage")).Count(row => Equals(row["State"], "active")));
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "初版/文件4事件登记失败与真实请求Flush后取消保持业务及文件使用关联原状")]
    public async Task DocumentFileEvents_AuditFailureAndRequestCancellationRollBackCommittedFacts()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        var historyRequests = new ObjectHistoryHttpTestRequests(fixture);
        await InitializeAsync(fixture, admin);
        var position = await ContainerAsync(fixture, admin);
        var source = new WorkspacePosition(position.SpaceId, position.RootFolderId, position.LinkIds);
        var original = await StageAndCreateAsync("FILE-FAULT-SOURCE-296");
        var originalLink = Assert.Single(await RowsAsync(fixture, "WorkspaceFolderContent"), row => Equals(row["CEID"], original));
        var sequence = 0;
        foreach (var eventType in new[] { "document.iteration.initialized", "document.files.created", "document.files.copied", "document.files.released" })
        foreach (var cancel in new[] { false, true })
        {
            var code = "FILE-FAULT-296-" + ++sequence;
            object body;
            string url;
            string behavior;
            var method = HttpMethod.Post;
            if (eventType == "document.files.released")
            {
                body = new { position = source };
                url = "/api/workspace/content/" + originalLink["LID"];
                behavior = "workspace.folder.deleteObject";
                method = HttpMethod.Delete;
            }
            else if (eventType == "document.files.copied")
            {
                using var preparation = await historyRequests.SendAsync(admin, Point(fixture, "workspace.folder.pasteObjectCopy"), HttpMethod.Post,
                    "/api/workspace/content/objects/copy/prepare", new { mode = "copy", sourcePosition = source,
                        linkIds = new[] { (string)originalLink["LID"]! }, targetPosition = source });
                body = new { operationId = (await ReadOkAsync(preparation)).GetProperty("operationId").GetString(), values = new { ECODE = code, ENAME = code } };
                url = "/api/workspace/content/objects/copy";
                behavior = "workspace.folder.pasteObjectCopy";
            }
            else
            {
                var values = new { ECODE = code, ENAME = code };
                var staged = await DocumentFilesSqlServerHttpApiTests.StageAsync(fixture, admin, source, values, [1, 2, 3]);
                body = new { position = source, values, uploads = new { mainUploadId = staged.GetProperty("uploadId").GetString() } };
                url = "/api/workspace/content/documents";
                behavior = "workspace.folder.createDocument";
            }
            var before = await SnapshotAsync(fixture);
            var reached = false;
            var flushed = false;
            using var cancellation = new CancellationTokenSource();
            fixture.Fault.AfterRegister = async (change, token) =>
            {
                if (change.EventType != eventType) return;
                reached = true;
                if (!cancel) throw new InvalidOperationException("injected document history failure");
                var context = fixture.Services.GetRequiredService<Microsoft.AspNetCore.Http.IHttpContextAccessor>().HttpContext!;
                await context.RequestServices.GetRequiredService<IPlatformTransactionExecutor>().FlushAsync(token);
                flushed = true;
                cancellation.Cancel();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            };
            try
            {
                using var request = new HttpRequestMessage(method, url) { Content = JsonContent.Create(body, options: new JsonSerializerOptions()) };
                request.Headers.Add("X-NewP-Feature", Point(fixture, behavior).Position);
                if (cancel)
                    await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                    {
                        using var response = await admin.SendAsync(request, cancellation.Token);
                        Assert.Fail($"请求未进入取消点，HTTP {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
                    });
                else
                {
                    using var response = await admin.SendAsync(request);
                    Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
                }
            }
            finally { fixture.Fault.AfterRegister = null; }
            Assert.True(reached, eventType);
            if (cancel) Assert.True(flushed, eventType);
            // 副本准备与内容传输有独立持久回执；此处只核业务提交、FileUsage和历史，不把准备回执误当业务行。
            Assert.Equal(before, await SnapshotAsync(fixture));
        }

        async Task<string> StageAndCreateAsync(string code)
        {
            var values = new { ECODE = code, ENAME = code };
            var staged = await DocumentFilesSqlServerHttpApiTests.StageAsync(fixture, admin, source, values, [1, 2, 3]);
            using var created = await historyRequests.SendAsync(admin, Point(fixture, "workspace.folder.createDocument"), HttpMethod.Post,
                "/api/workspace/content/documents", new { position = source, values, uploads = new { mainUploadId = staged.GetProperty("uploadId").GetString() } });
            await ReadOkAsync(created);
            return (string)Assert.Single(await RowsAsync(fixture, "Document"))["EID"]!;
        }
    }

    private static async Task<string> SnapshotAsync(BusinessAuditHostFixture fixture)
    {
        var snapshots = new Dictionary<string, string[]>();
        foreach (var model in new[] { "Document", "DocumentVersion", "DocumentIteration", "DocumentFileLink", "WorkspaceFolderContent", "BusinessChange", "FileUsage", "FileUploadLease" })
        {
            var idField = model is "DocumentFileLink" or "WorkspaceFolderContent" ? "LID" : "EID";
            snapshots[model] = (await RowsAsync(fixture, model)).OrderBy(row => (string)row[idField]!, StringComparer.Ordinal)
                .Select(row => JsonSerializer.Serialize(row.OrderBy(pair => pair.Key, StringComparer.Ordinal))).ToArray();
        }
        return JsonSerializer.Serialize(snapshots);
    }

    private static async Task AssertFileEventAsync(BusinessAuditHostFixture fixture, string documentId, string eventType, int expectedCount)
    {
        var record = Assert.Single(await EventsAsync(fixture, documentId), row => Equals(row["EventType"], eventType));
        AssertField(record, "FileCount", expectedCount.ToString(System.Globalization.CultureInfo.InvariantCulture));
        AssertField(record, "MainFileCount", "1");
        AssertField(record, "AttachmentFileCount", "1");
        var version = Assert.Single(await RowsAsync(fixture, "DocumentVersion"), row => Equals(row["DocumentId"], documentId));
        var links = (await RowsAsync(fixture, "DocumentFileLink")).Where(row => Equals(row["PEID"], version["CurrentIterationId"])).ToArray();
        Assert.Equal(expectedCount, links.Length);
        Assert.All(links, row => { Assert.Equal(row["LCreatedAt"], record["OccurredAt"]); Assert.Equal(row["LCreatedBy"], record["ActorId"]); });
    }

    private static void AssertField(Dictionary<string, object?> record, string field, string expected)
    {
        var value = Assert.Single(JsonSerializer.Deserialize<JsonElement>((string)record["FieldsJson"]!).EnumerateArray(), row => row.GetProperty("Field").GetString() == field);
        Assert.Equal(expected, value.GetProperty("After").GetProperty("Value").GetString());
    }

    private static async Task<Dictionary<string, object?>[]> EventsAsync(BusinessAuditHostFixture fixture, string documentId) =>
        (await RowsAsync(fixture, "BusinessChange")).Where(row => Equals(row["Resource"], "document.document") && Equals(row["TargetId"], documentId)).ToArray();

    private static async Task<Dictionary<string, object?>[]> RowsAsync(BusinessAuditHostFixture fixture, string model)
    {
        using var scope = fixture.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>().Set<Dictionary<string, object?>>(model).AsNoTracking().ToArrayAsync();
    }
}
