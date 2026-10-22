using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ICP.Platform.Testing;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Modules.Authorization.DTOs;
using ICP.Platform.Modules.Document;
using ICP.Platform.Modules.Files.Contracts;
using Microsoft.Extensions.DependencyInjection;
using ICP.Platform.Persistence.EfCore;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
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
        var declaration = new FeatureHttpTestRequests(fixture).Declaration("workspace.folder.pasteObjectCopy", point);
        var body = new { mode = "copy", sourcePosition = source, linkIds = new[] { lid }, targetPosition = target };
        var wrong = new FeatureHttpTestRequests(fixture).Declaration("workspace.folder.copy", Point(fixture, "workspace.folder.copy"));
        using (var swapped = await FeatureHttpTestRequests.SendAsync(ordinary, point, HttpMethod.Post,
            "/api/workspace/content/objects/copy/prepare", body, wrong))
            await Problem(swapped, HttpStatusCode.BadRequest, "ButtonExecution.BindingInvalid");
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
        FileOwnerInternalExecutionContext?[] crossed = [null, RetiredCopyFileSource(documentId) with { ObjectId = "another-document" },
            RetiredCopyFileSource(documentId) with { ResourceKey = "part.part" }, new(new("document.files.copyMetadata", "document.backend", typeof(DocumentFiles), nameof(DocumentFiles.ReadCopySourceAsync)), "document.document", documentId)];
        for (var index = 0; index < crossed.Length; index++)
            await AsCopyActor(fixture, async provider =>
            {
                var files = provider.GetRequiredService<IFileCopyService>();
                var error = await Assert.ThrowsAsync<FileLifecycleException>(() => files.PrepareCopyAsync("wrong-owner-" + index,
                    reference, DocumentFileOwnerPolicy.Key, crossed[index]!));
                Assert.Equal(400, error.StatusCode);
                return true;
            });
        // 迁移后的owner来源必须由真实读取作用域产生，已登记的旧record本身不能证明这次调用来自owner。
        await AsCopyActor(fixture, async provider =>
        {
            var error = await Assert.ThrowsAsync<FileLifecycleException>(() => provider.GetRequiredService<IFileCopyService>()
                .PrepareCopyAsync("registered-record-without-origin", reference, DocumentFileOwnerPolicy.Key, RetiredCopyFileSource(documentId)));
            Assert.Equal(400, error.StatusCode);
            Assert.Equal("Document.FileReadContextInvalid", error.Code);
            return true;
        });
        Assert.Empty(await Rows(fixture, "FileCopyLease"));
        Assert.Equal(2, (await Rows(fixture, "FileRecord")).Length);
        Assert.Equal(2, Directory.EnumerateFiles(fixture.StorageRoot).Count());
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "私有文件副本来源绑定真实owner/阶段/事务，错属Document和缺Added创建证明不能产生租约或使用")]
    public async Task DocumentObjectCopy_PrivateOriginsRequireTheirCurrentOwnerPhaseAndCreationProof()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        var folder = await Folder(fixture, admin, await Root(fixture, admin), "副本来源生命周期");
        await CreateBatchDocument(fixture, admin, folder, 1);
        await CreateBatchDocument(fixture, admin, folder, 2);
        var documents = await Rows(fixture, "Document");
        var firstId = (string)Assert.Single(documents, row => Equals(row["ECODE"], "BATCH-SOURCE-1"))["EID"]!;
        var secondId = (string)Assert.Single(documents, row => Equals(row["ECODE"], "BATCH-SOURCE-2"))["EID"]!;
        await AsCopyActor(fixture, async provider =>
        {
            var identityReader = provider.GetRequiredService<DocumentInitialIdentity>();
            var first = await identityReader.ReadCurrentAsync(firstId);
            var second = await identityReader.ReadCurrentAsync(secondId);
            var db = provider.GetRequiredService<EfPlatformDbContext>();
            var actualLink = await db.Set<Dictionary<string, object?>>("DocumentFileLink").AsNoTracking()
                .FirstAsync(row => EF.Property<string>(row, "PEID") == (string)first.Iteration["EID"]!);
            var reference = new FileUsageReference((string)actualLink["FileUsageId"]!, (string)actualLink["CEID"]!,
                new(DocumentFileOwnerPolicy.Key, (string)actualLink["LID"]!));
            var owner = provider.GetRequiredService<DocumentFileCopyScope>();
            var files = provider.GetRequiredService<IFileCopyService>();
            var policy = Assert.Single(provider.GetServices<IFileOwnerPolicy>(), item => item.OwnerKey == DocumentFileOwnerPolicy.Key);
            var anotherOwner = new DocumentFileCopyScope(db, provider.GetRequiredService<IHttpContextAccessor>());
            using (anotherOwner.Begin(first, DocumentFileCopyPhase.Prepare, out var wrongOwner))
                await RejectPrepare("another-owner", wrongOwner, 400);
            var expired = owner.Begin(first, DocumentFileCopyPhase.Prepare, out var ended);
            expired.Dispose();
            await RejectPrepare("ended-origin", ended, 400);
            using (owner.Begin(first, DocumentFileCopyPhase.Metadata, out var metadata))
                await RejectPrepare("metadata-is-not-copy-source", metadata, 400);
            using (owner.Begin(second, DocumentFileCopyPhase.Prepare, out var otherDocument))
                await RejectPrepare("another-real-document", otherDocument, 404);

            // 有效来源一旦换事务即失效；用真实非空批次核policy而非Files的独立事务前置检查。
            using (owner.Begin(first, DocumentFileCopyPhase.Prepare, out var outside))
            {
                await using var transaction = await db.Database.BeginTransactionAsync();
                var changed = await Assert.ThrowsAsync<FileLifecycleException>(() => policy.VerifyBatchAsync([reference],
                    FileOwnerOperation.CopySource, outside, default));
                Assert.Equal("Document.FileReadContextInvalid", changed.Code);
                await transaction.RollbackAsync();
            }

            var creation = provider.GetRequiredService<DocumentFileCreationScope>();
            await using (var transaction = await db.Database.BeginTransactionAsync())
            {
                using var copied = creation.BeginCopied(first, out var target);
                // 私有来源也不能把已持久Link冒充首次Added关联。
                var notAdded = await Assert.ThrowsAsync<FileLifecycleException>(() => policy.VerifyAsync(reference.Owner,
                    reference.FileId, null, FileOwnerOperation.ConfirmCopy, target, default));
                Assert.Equal("Document.CreationProofRequired", notAdded.Code);
                var added = new Dictionary<string, object?>(actualLink) { ["LID"] = "DocumentFileLink_copy-origin-proof",
                    ["PEID"] = second.Iteration["EID"], ["FileUsageId"] = null };
                db.Set<Dictionary<string, object?>>("DocumentFileLink").Add(added);
                var wrongIteration = await Assert.ThrowsAsync<FileLifecycleException>(() => policy.VerifyAsync(
                    new(DocumentFileOwnerPolicy.Key, (string)added["LID"]!), reference.FileId, null,
                    FileOwnerOperation.ConfirmCopy, target, default));
                Assert.Equal("Document.CreationProofRequired", wrongIteration.Code);
                await transaction.RollbackAsync();
            }
            return true;

            async Task RejectPrepare(string key, FileOwnerOperationContext context, int status)
            {
                var failure = await Assert.ThrowsAsync<FileLifecycleException>(() => files.PrepareCopyAsync(key, reference,
                    DocumentFileOwnerPolicy.Key, context));
                Assert.Equal(status, failure.StatusCode);
                Assert.Equal(status == 400 ? "Document.FileReadContextInvalid" : "Files.UsageUnavailable", failure.Code);
            }
        });
        Assert.Empty(await Rows(fixture, "FileCopyLease"));
        Assert.Equal(4, (await Rows(fixture, "FileRecord")).Length);
        Assert.Equal(4, (await Rows(fixture, "FileUsage")).Length);
        Assert.Equal(4, Directory.EnumerateFiles(fixture.StorageRoot).Count());
    }

    // 仅保留旧ABI负面资产，迁移后已登记的固定record也不能替代私有origin。
    private static FileOwnerInternalExecutionContext RetiredCopyFileSource(string documentId, bool confirming = false) => new(
        new("document.files.copySource", "document.files.copySource", typeof(DocumentFiles), confirming
            ? nameof(DocumentFiles.CopyAsync) : nameof(DocumentFiles.PrepareCopyFilesAsync)), "document.document", documentId);

}
