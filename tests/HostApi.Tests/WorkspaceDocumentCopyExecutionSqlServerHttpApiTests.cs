using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ICP.Platform.Testing;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Modules.Authorization.DTOs;
using ICP.Platform.Modules.Document;
using ICP.Platform.Modules.Files.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed partial class WorkspaceClipboardSqlServerHttpApiTests
{
    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "普通主体在真实Document副本准备入口缺正式来源时写前拒绝，不借Part动作声明或默选旧分支")]
    public async Task DocumentObjectCopy_RejectsMissingDeclarationAtItsRealPrepareEntry()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        var root = await Root(fixture, admin);
        var source = await Folder(fixture, admin, root, "Document正式来源");
        var target = await Folder(fixture, admin, root, "Document正式目标");
        var lid = await CreateBatchDocument(fixture, admin, source, 1);
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "DocCopyDeclarationActor", "Document复制主体",
            "doc-copy-declaration", "Document复制账号", Point(fixture, "workspace.spaces.access"), Point(fixture, "workspace.folder.pasteObjectCopy"));
        using var ordinary = actor.Client;
        using var response = await FeatureHttpTestRequests.SendAsync(ordinary, Point(fixture, "workspace.folder.pasteObjectCopy"), HttpMethod.Post,
            "/api/workspace/content/objects/copy/prepare", new { mode = "copy", sourcePosition = source, linkIds = new[] { lid }, targetPosition = target });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(await Rows(fixture, "WorkspaceObjectCopy"));
        Assert.Single(await Rows(fixture, "Document"));
        Assert.Equal(2, (await Rows(fixture, "FileUsage")).Length);

        var point = Point(fixture, "workspace.folder.pasteObjectCopy");
        var declaration = new FeatureHttpTestRequests(fixture).Declaration("workspace.objectCopy", point);
        var body = new { mode = "copy", sourcePosition = source, linkIds = new[] { lid }, targetPosition = target };
        var wrong = new FeatureHttpTestRequests(fixture).Declaration("workspace.folder.copy", Point(fixture, "workspace.folder.copy"));
        using (var swapped = await FeatureHttpTestRequests.SendAsync(ordinary, point, HttpMethod.Post,
            "/api/workspace/content/objects/copy/prepare", body, wrong))
            await Problem(swapped, HttpStatusCode.BadRequest, "ActionDataAccess.DeclarationInvalid");
        var sourceId = (string)Assert.Single(await Rows(fixture, "Document"))["EID"]!;
        await Rule(fixture, admin, "DocumentPrepareCopy", "document.document", sourceId, "documentObjectCopy", "deny");
        using (var denied = await SendPrepare()) await Problem(denied, HttpStatusCode.Forbidden, "Workspace.ObjectCopyDenied");
        await Rule(fixture, admin, "DocumentPrepareCopy", "document.document", sourceId, "documentObjectCopy", "allow");
        await Rule(fixture, admin, "DocumentPrepareRead", "document.document", sourceId, "read", "deny");
        using (var denied = await SendPrepare()) await Problem(denied, HttpStatusCode.NotFound, "Workspace.TargetUnavailable");
        await Rule(fixture, admin, "DocumentPrepareRead", "document.document", sourceId, "read", "allow");
        var targetId = FolderId(target, await Rows(fixture, "WorkspaceFolderContent"));
        await Rule(fixture, admin, "DocumentPrepareParent", "workspace.folder", targetId, "edit", "deny");
        using (var denied = await SendPrepare()) await Problem(denied, HttpStatusCode.Forbidden, "Workspace.ParentWriteDenied");
        await Rule(fixture, admin, "DocumentPrepareParent", "workspace.folder", targetId, "edit", "allow");
        using var allowed = await SendPrepare();
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        var prepared = await allowed.Content.ReadFromJsonAsync<JsonElement>();
        using var copied = await FeatureHttpTestRequests.SendAsync(ordinary, point, HttpMethod.Post,
            "/api/workspace/content/objects/copy", new { operationId = prepared.GetProperty("operationId").GetString(),
                values = new { ECODE = "DOCUMENT-DECLARED-COPY", ENAME = "正式Document副本" } }, declaration);
        Assert.Equal(HttpStatusCode.OK, copied.StatusCode);
        Assert.True((await copied.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("succeeded").GetBoolean());
        Assert.Equal(2, (await Rows(fixture, "Document")).Length);
        Assert.Equal(4, (await Rows(fixture, "FileUsage")).Length);

        Task<HttpResponseMessage> SendPrepare() => FeatureHttpTestRequests.SendAsync(ordinary, point, HttpMethod.Post,
            "/api/workspace/content/objects/copy/prepare", body, declaration);

    }
    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "普通主体的整批Document逐后项核read/copy/候选与目标父级，拒绝不准备字节，成功回执不读取不可见新对象")]
    public async Task DocumentObjectCopy_BatchChecksEveryActualSourceParentAndCandidateWithoutReadingTheNewObjects()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        var root = await Root(fixture, admin);
        var source = await Folder(fixture, admin, root, "正式整批来源");
        var target = await Folder(fixture, admin, root, "正式整批目标");
        string[] links = [await CreateBatchDocument(fixture, admin, source, 1), await CreateBatchDocument(fixture, admin, source, 2),
            await CreateBatchDocument(fixture, admin, source, 3)];
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "DocBatchFormalActor", "整批复制主体",
            "doc-batch-formal", "整批复制账号", Point(fixture, "workspace.spaces.access"), Point(fixture, "workspace.folder.pasteObjectCopy"),
            Point(fixture, "workspace.folder.resolveObjectCopy"));
        using var ordinary = actor.Client;
        var documents = await Rows(fixture, "Document");
        var laterId = (string)Assert.Single(documents, row => Equals(row["ECODE"], "BATCH-SOURCE-3"))["EID"]!;
        await Rule(fixture, admin, "DocBatchLateRead", "document.document", laterId, "read", "deny");
        using (var denied = await Send(fixture, ordinary, "workspace.folder.pasteObjectCopy", "objects/copy/prepare",
            new { mode = "copy", sourcePosition = source, linkIds = links, targetPosition = target }))
            await Problem(denied, HttpStatusCode.NotFound, "Workspace.TargetUnavailable");
        await Rule(fixture, admin, "DocBatchLateRead", "document.document", laterId, "read", "allow");
        await Rule(fixture, admin, "DocBatchLateCopy", "document.document", laterId, "documentObjectCopy", "deny");
        using (var denied = await Send(fixture, ordinary, "workspace.folder.pasteObjectCopy", "objects/copy/prepare",
            new { mode = "copy", sourcePosition = source, linkIds = links, targetPosition = target }))
            await Problem(denied, HttpStatusCode.Forbidden, "Workspace.ObjectCopyDenied");
        Assert.Empty(await Rows(fixture, "WorkspaceObjectCopy"));
        await Rule(fixture, admin, "DocBatchLateCopy", "document.document", laterId, "documentObjectCopy", "allow");
        var prepared = await PrepareDocumentBatch(fixture, ordinary, source, target, links);
        var operationId = prepared.GetProperty("operationId").GetString();
        await Rule(fixture, admin, "DocBatchLateCopy", "document.document", laterId, "documentObjectCopy", "deny");
        using (var denied = await Submit("BEFORE-REVOKE")) await Problem(denied, HttpStatusCode.Forbidden, "Workspace.ObjectCopyDenied");
        await Rule(fixture, admin, "DocBatchLateCopy", "document.document", laterId, "documentObjectCopy", "allow");
        var targetId = FolderId(target, await Rows(fixture, "WorkspaceFolderContent"));
        await Rule(fixture, admin, "DocBatchLateParent", "workspace.folder", targetId, "edit", "deny");
        using (var denied = await Submit("BEFORE-PARENT")) await Problem(denied, HttpStatusCode.Forbidden, "Workspace.ParentWriteDenied");
        await Rule(fixture, admin, "DocBatchLateParent", "workspace.folder", targetId, "edit", "allow");
        await CandidateRule("DocBatchLateCandidate", "CANDIDATE-1", "create", "deny");
        using (var denied = await Submit("CANDIDATE")) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Empty(await Rows(fixture, "FileCopyLease"));
        Assert.Equal(3, (await Rows(fixture, "Document")).Length);
        Assert.Equal(6, (await Rows(fixture, "FileUsage")).Length);
        await CandidateRule("DocBatchNewUnreadable", "UNREADABLE-1", "read", "deny");
        using var success = await Submit("UNREADABLE");
        Assert.Equal(HttpStatusCode.OK, success.StatusCode);
        var receipt = await success.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(receipt.GetProperty("succeeded").GetBoolean());
        Assert.Equal(links, receipt.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("sourceLinkId").GetString()));
        Assert.All(receipt.GetProperty("items").EnumerateArray(), item => Assert.Equal(3, item.EnumerateObject().Count()));
        Assert.Equal(6, (await Rows(fixture, "Document")).Length);
        Assert.Equal(12, (await Rows(fixture, "FileUsage")).Length);

        Task<HttpResponseMessage> Submit(string prefix) => Send(fixture, ordinary, "workspace.folder.pasteObjectCopy", "objects/copy",
            new { operationId, values = BatchValues(links, prefix) });
        async Task CandidateRule(string key, string code, string action, string opinion)
        {
            var rule = new DataScopeRuleDto(key, key, null, key, true, "document.document",
                new("attribute", "ECODE", "equal", JsonSerializer.SerializeToElement(code)),
                [new(key + "Subject", "all", null, 0, new Dictionary<string, string> { [action] = opinion })], []);
            using var response = await new FeatureHttpTestRequests(fixture).SendButtonAsync(admin, Assert.Single(fixture.FeatureCatalog, item => item.BehaviorKey == "authorization.dataScope.save"),
                HttpMethod.Put, "/api/authorization/data-scope/rules/" + key, rule);
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "真实Files来源不能借其他Document、只读内部来源或空上下文，拒绝不创建租约或新文件")]
    public async Task DocumentObjectCopy_FileSourceContextMustMatchTheActualOwningDocument()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        var root = await Root(fixture, admin);
        var source = await Folder(fixture, admin, root, "文件真实所属来源");
        await CreateBatchDocument(fixture, admin, source, 1);
        var documentId = (string)Assert.Single(await Rows(fixture, "Document"))["EID"]!;
        var link = (await Rows(fixture, "DocumentFileLink")).OrderBy(item => (long)item["Ordinal"]!).First();
        var reference = new FileUsageReference((string)link["FileUsageId"]!, (string)link["CEID"]!, new(DocumentFileOwnerPolicy.Key, (string)link["LID"]!));
        FileOwnerInternalExecutionContext?[] crossed = [null, CopyFileSource(documentId) with { ObjectId = "another-document" },
            CopyFileSource(documentId) with { ResourceKey = "part.part" }, DocumentCopyExecutions.Metadata(documentId)];
        for (var index = 0; index < crossed.Length; index++)
            await AsCopyActor(fixture, async provider =>
            {
                var files = provider.GetRequiredService<IFileCopyService>();
                var error = await Assert.ThrowsAsync<FileLifecycleException>(() => files.PrepareCopyAsync("wrong-owner-" + index,
                    reference, DocumentFileOwnerPolicy.Key, crossed[index]!));
                Assert.Equal(index == 1 ? 404 : 400, error.StatusCode);
                return true;
            });
        Assert.Empty(await Rows(fixture, "FileCopyLease"));
        Assert.Equal(2, (await Rows(fixture, "FileRecord")).Length);
        Assert.Equal(2, Directory.EnumerateFiles(fixture.StorageRoot).Count());
    }

    // 直接owner消费者仍精确选择真实Document成员；不会按URL或缺声明补来源。
    private static FileOwnerInternalExecutionContext CopyFileSource(string documentId, bool confirming = false) => new(
        new("document.files.copySource", "document.files.copySource", typeof(DocumentFiles), confirming
            ? nameof(DocumentFiles.CopyAsync) : nameof(DocumentFiles.PrepareCopyFilesAsync)), "document.document", documentId);

    // 故障消费者的目标来源只表达真实创建执行，尚未Added/TX证明仍应被原owner拒绝。
    private static FileOwnerCreationContext CopyFileTarget(string documentId) => new(new(
        new("document.document.internal.create", "standardCrud.create", "submit"), "document.document", null)
        { Internal = new("document.document.internal.create", "standardCrud.create", typeof(DocumentFiles), nameof(DocumentFiles.CopyAsync)) }, documentId);
}
