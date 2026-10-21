using System.Diagnostics;
using System.Security.Claims;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Modules.Document;
using ICP.Platform.Modules.Files.Contracts;
using ICP.Platform.Persistence.Abstractions;
using Microsoft.AspNetCore.Http;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed partial class DocumentFilesSqlServerHttpApiTests
{
    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "独立Document新按钮只检查完整候选且缺借身份写前拒绝，首版同事务与关闭写URL保持")]
    public async Task DocumentEntryCreate_UsesOwnButtonAndPreservesClosedWrites()
    {
        await using var fixture = await ControlledFixtureAsync();
        using var admin = await fixture.LoginAdministratorAsync();
        var create = Point(fixture, "document.document.create");
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "DocumentCreate313", "独立图文档创建",
            "document-create313", "图文档创建主体", create, Point(fixture, "document.document.read"));
        using var ordinary = actor.Client;
        var values = new { ECODE = "DOC-ENTRY-313", ENAME = "正式独立创建" };
        using (var missing = await FeatureHttpTestRequests.SendAsync(ordinary, create, HttpMethod.Post, "/api/document/documents", values))
            Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        var plan = fixture.Services.GetRequiredService<CanonicalModulePlan>();
        var declaration = Assert.Single(plan.ActionDataAccessCatalog, item => item.ViewKey == "document.document.list"
            && item.Surface == "pageActions" && item.ActionKey == "create"
            && item.FeaturePositions.Contains(create.Position));
        var read = Assert.Single(plan.ActionDataAccessCatalog, item => item.Execution == "document.document.read"
            && item.ViewKey == "document.document.detail");
        using (var crossed = await SendButtonAsync(read.Id, create.Position, values))
            Assert.Equal(HttpStatusCode.BadRequest, crossed.StatusCode);
        using var created = await SendButtonAsync(declaration.Id, create.Position, values);
        Assert.True(created.StatusCode == HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        Assert.Equal("document.document.create", declaration.BusinessBinding);
        Assert.Equal("Document", declaration.Object);
        Assert.Equal(["create"], declaration.Actions);
        var id = BusinessAuditSqlServerHttpApiTests.Id(await created.Content.ReadFromJsonAsync<JsonElement>());
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
        var document = await db.Set<Dictionary<string, object?>>("Document").AsNoTracking().SingleAsync();
        Assert.Equal(id, document["EID"]);
        var version = await db.Set<Dictionary<string, object?>>("DocumentVersion").AsNoTracking().SingleAsync();
        var iteration = await db.Set<Dictionary<string, object?>>("DocumentIteration").AsNoTracking().SingleAsync();
        Assert.Equal(version["EID"], document["LatestVersionId"]);
        Assert.Equal(iteration["EID"], version["CurrentIterationId"]);
        Assert.Equal(actor.AccountId, document["CreatedBy"]);
        Assert.Equal(actor.AccountId, document["OwnerId"]);
        // 真候选按本次字段拒绝，不能因已持有创建按钮而略过候选判定；没有首版/版次或文件残留。
        const string deniedCode = "DOC-CANDIDATE-DENIED-333";
        await SaveRuleAsync(fixture, admin, "DocumentCandidate333", "document.document", "ECODE", deniedCode, "create");
        using (var denied = await SendButtonAsync(declaration.Id, create.Position, new { ECODE = deniedCode, ENAME = "候选拒绝" }))
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using (var legacy = new HttpRequestMessage(HttpMethod.Post, "/api/document/documents"))
        {
            legacy.Headers.Add("X-NewP-Feature", create.Position);
            legacy.Headers.Add("X-NewP-Data-Declaration", declaration.Id);
            legacy.Content = JsonContent.Create(new { ECODE = "DOC-LEGACY-REJECTED-333", ENAME = "旧头拒绝" }, options: new JsonSerializerOptions());
            using var rejected = await ordinary.SendAsync(legacy);
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        }
        Assert.Equal(1, await db.Set<Dictionary<string, object?>>("Document").CountAsync());
        Assert.Equal(1, await db.Set<Dictionary<string, object?>>("DocumentVersion").CountAsync());
        Assert.Equal(1, await db.Set<Dictionary<string, object?>>("DocumentIteration").CountAsync());
        Assert.Equal(0, await db.Set<Dictionary<string, object?>>("DocumentFileLink").CountAsync());
        Assert.Equal(0, await db.Set<Dictionary<string, object?>>("FileUsage").CountAsync());
        foreach (var method in new[] { HttpMethod.Put, HttpMethod.Delete })
        {
            using var closed = await FeatureHttpTestRequests.SendAsync(ordinary, create, method, "/api/document/documents/" + id,
                new { ENAME = "独立URL仍关闭" }, declaration);
            Assert.Equal(HttpStatusCode.Forbidden, closed.StatusCode);
        }

        // 直接采用正式目录ID验证HTTP协议，不让辅助方法按旧/新投影替调用方选择请求头。
        async Task<HttpResponseMessage> SendButtonAsync(string buttonId, string position, object input)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/document/documents");
            request.Headers.Add("X-NewP-Feature", position);
            request.Headers.Add("X-NewP-Button", buttonId);
            request.Content = JsonContent.Create(input, options: new JsonSerializerOptions());
            return await ordinary.SendAsync(request);
        }
    }
    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "Workspace实际Document按钮贯通Stage与保存，借身份/错租约owner及保存时撤销create不产生业务残留")]
    public async Task WorkspaceDocumentButton_StagesAndCreatesWithTheSameOwnedLeaseAndCurrentCandidateDecision()
    {
        await using var fixture = await ControlledFixtureAsync();
        using var admin = await fixture.LoginAdministratorAsync();
        var position = await ContentAsync(fixture, admin);
        var actor = await ActorAsync(fixture, admin, "DocumentButton333");
        using var ordinary = actor.Client;
        var feature = Point(fixture, "workspace.folder.createDocument");
        var plan = fixture.Services.GetRequiredService<CanonicalModulePlan>();
        var declaration = Assert.Single(plan.ActionDataAccessCatalog, item => item.ModuleId == "Workspace"
            && item.ViewKey == "workspace.folder.list" && item.Surface == "pageActions" && item.ActionKey == "createDocument"
            && item.FeaturePositions.Contains(feature.Position));
        var borrowed = Assert.Single(plan.ActionDataAccessCatalog, item => item.ModuleId == "Document"
            && item.ViewKey == "document.document.list" && item.Surface == "pageActions" && item.ActionKey == "create");
        const string code = "DOCUMENT-BUTTON-333";
        var values = new { ECODE = code, ENAME = "正式含文件图文档", Description = "同事务首版" };
        byte[] bytes = [1, 2, 3, 4];
        foreach (var button in new string?[] { null, borrowed.Id })
        {
            using var rejected = await StageWithButtonAsync(button);
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        }
        var empty = await ObserveBusinessAsync(fixture);
        Assert.Empty(empty.Documents); Assert.Empty(empty.Files); Assert.Empty(empty.Leases); Assert.Empty(empty.Usages);
        using var stage = await StageWithButtonAsync(declaration.Id);
        Assert.True(stage.StatusCode == HttpStatusCode.OK, await stage.Content.ReadAsStringAsync());
        var staged = await stage.Content.ReadFromJsonAsync<JsonElement>();
        var uploadId = staged.GetProperty("uploadId").GetString()!;
        var stagedFacts = await ObserveBusinessAsync(fixture);
        Assert.Empty(stagedFacts.Documents); Assert.Empty(stagedFacts.Versions); Assert.Empty(stagedFacts.Iterations);
        Assert.Empty(stagedFacts.Usages); Assert.Empty(stagedFacts.Links);
        Assert.Equal("staged", Assert.Single(stagedFacts.Leases)["State"]);
        Assert.Equal(actor.AccountId, Assert.Single(stagedFacts.Leases)["ActorId"]);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(ContentPath(fixture, Assert.Single(stagedFacts.Files))));
        var body = new { position, values, uploads = new { mainUploadId = uploadId } };
        // Stage不签署后续许可：仍从当前候选规则重新裁决，失败保留同一暂存供纠正。
        await SaveRuleAsync(fixture, admin, "DocumentButton333Candidate", "document.document", "ECODE", code, "create");
        using (var denied = await SaveWithButtonAsync(ordinary, body)) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        await SaveRuleAsync(fixture, admin, "DocumentButton333Candidate", "document.document", "ECODE", "not-this-candidate", "create");
        using (var foreign = await SaveWithButtonAsync(admin, body)) Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        var refused = await ObserveBusinessAsync(fixture);
        Assert.Empty(refused.Documents); Assert.Empty(refused.Versions); Assert.Empty(refused.Iterations);
        Assert.Empty(refused.Links); Assert.Empty(refused.References); Assert.Empty(refused.Usages);
        Assert.Equal("staged", Assert.Single(refused.Leases)["State"]);
        using (var saved = await SaveWithButtonAsync(ordinary, body))
        {
            Assert.True(saved.StatusCode == HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
            Assert.True((await saved.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("succeeded").GetBoolean());
        }
        var durable = await ObserveBusinessAsync(fixture);
        var document = Assert.Single(durable.Documents);
        var version = Assert.Single(durable.Versions); var iteration = Assert.Single(durable.Iterations);
        var link = Assert.Single(durable.Links); var usage = Assert.Single(durable.Usages);
        Assert.Equal(document["EID"], version["DocumentId"]); Assert.Equal(version["EID"], iteration["VersionId"]);
        Assert.Equal(iteration["EID"], link["PEID"]); Assert.Equal(usage["EID"], link["FileUsageId"]);
        Assert.Equal(link["LID"], usage["OwnerObjectId"]); Assert.Equal(link["CEID"], usage["FileId"]);
        Assert.Equal(document["EID"], Assert.Single(durable.References)["CEID"]);
        Assert.Equal("active", usage["State"]); Assert.Equal("confirmed", Assert.Single(durable.Leases)["State"]);
        Assert.Equal(actor.AccountId, document["CreatedBy"]);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(ContentPath(fixture, Assert.Single(durable.Files))));

        async Task<HttpResponseMessage> StageWithButtonAsync(string? button)
        {
            using var request = UploadRequest(fixture, position, values, new ByteArrayContent(bytes), "main.pdf");
            if (button is not null) request.Headers.Add("X-NewP-Button", button);
            return await ordinary.SendAsync(request);
        }
        async Task<HttpResponseMessage> SaveWithButtonAsync(HttpClient client, object input)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/workspace/content/documents");
            request.Headers.Add("X-NewP-Feature", feature.Position);
            request.Headers.Add("X-NewP-Button", declaration.Id);
            request.Content = JsonContent.Create(input, options: new JsonSerializerOptions());
            return await client.SendAsync(request);
        }
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "Workspace无文件创建仍由同一正式Document候选按钮原子提交，缺身份与候选拒绝不写任何文件事实")]
    public async Task WorkspaceDocumentButton_WithoutFilesKeepsMetadataOnlyCreationAtomic()
    {
        await using var fixture = await ControlledFixtureAsync();
        using var admin = await fixture.LoginAdministratorAsync();
        var position = await ContentAsync(fixture, admin);
        var actor = await ActorAsync(fixture, admin, "DocumentNoFiles333");
        using var ordinary = actor.Client;
        var feature = Point(fixture, "workspace.folder.createDocument");
        var body = new { position, values = new { ECODE = "NO-FILES-333", ENAME = "无文件图文档" }, uploads = (object?)null };
        using (var missing = await FeatureHttpTestRequests.SendAsync(ordinary, feature, HttpMethod.Post, "/api/workspace/content/documents", body))
            Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        using (var saved = await new FeatureHttpTestRequests(fixture).SendButtonAsync(ordinary, feature, HttpMethod.Post, "/api/workspace/content/documents", body))
        {
            Assert.True(saved.StatusCode == HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
            Assert.True((await saved.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("succeeded").GetBoolean());
        }
        const string deniedCode = "NO-FILES-DENIED-333";
        await SaveRuleAsync(fixture, admin, "DocumentNoFile333", "document.document", "ECODE", deniedCode, "create");
        using (var denied = await new FeatureHttpTestRequests(fixture).SendButtonAsync(ordinary, feature, HttpMethod.Post, "/api/workspace/content/documents",
            new { position, values = new { ECODE = deniedCode, ENAME = "候选拒绝" }, uploads = (object?)null }))
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        var durable = await ObserveBusinessAsync(fixture);
        var document = Assert.Single(durable.Documents);
        var version = Assert.Single(durable.Versions); var iteration = Assert.Single(durable.Iterations);
        Assert.Equal(actor.AccountId, document["OwnerId"]);
        Assert.Equal(version["EID"], document["LatestVersionId"]); Assert.Equal(document["EID"], version["DocumentId"]);
        Assert.Equal(iteration["EID"], version["CurrentIterationId"]); Assert.Equal(version["EID"], iteration["VersionId"]);
        Assert.Equal(document["EID"], Assert.Single(durable.References)["CEID"]);
        Assert.Empty(durable.Links); Assert.Empty(durable.Files); Assert.Empty(durable.Leases); Assert.Empty(durable.Usages);
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "Document下载采用自己的按钮和真实Document目标，保留独立read及双向文件关联，缺借混身份不打开文件")]
    public async Task DownloadButton_UsesActualDocumentAndCurrentReadDownloadDecisions()
    {
        await using var fixture = await ControlledFixtureAsync();
        using var admin = await fixture.LoginAdministratorAsync();
        var position = await ContentAsync(fixture, admin);
        var actor = await ActorAsync(fixture, admin, "DocumentDownload333");
        using var ordinary = actor.Client;
        byte[] bytes = [7, 6, 5, 4];
        var created = await CreateMainAsync(fixture, ordinary, position, "DOWNLOAD-BUTTON-333", bytes);
        var point = Point(fixture, "document.document.download");
        var plan = fixture.Services.GetRequiredService<CanonicalModulePlan>();
        var download = Assert.Single(plan.ActionDataAccessCatalog, item => item.ModuleId == "Document"
            && item.ViewKey == "document.document.detail" && item.Surface == "featureActions" && item.ActionKey == "download"
            && item.FeaturePositions.Contains(point.Position));
        var borrowed = Assert.Single(plan.ActionDataAccessCatalog, item => item.ModuleId == "Workspace"
            && item.ViewKey == "workspace.folder.list" && item.Surface == "pageActions" && item.ActionKey == "createDocument");
        foreach (var button in new string?[] { null, borrowed.Id })
        {
            using var deniedIdentity = await SendAsync(button);
            Assert.Equal(HttpStatusCode.BadRequest, deniedIdentity.StatusCode);
        }
        using (var mixed = await SendAsync(download.Id, legacy: true)) Assert.Equal(HttpStatusCode.BadRequest, mixed.StatusCode);
        using (var content = await SendAsync(download.Id))
        {
            Assert.True(content.StatusCode == HttpStatusCode.OK, await content.Content.ReadAsStringAsync());
            Assert.Equal(bytes, await content.Content.ReadAsByteArrayAsync());
        }
        using (var wrongLink = await SendAsync(download.Id, path: $"/api/document/documents/{created.DocumentId}/files/DocumentFileLink_unknown/content"))
            await ProblemAsync(wrongLink, HttpStatusCode.NotFound, "Files.UsageUnavailable");
        await SaveRuleAsync(fixture, admin, "Download333Rule", "document.document", "EID", created.DocumentId, "download");
        using (var deniedDownload = await SendAsync(download.Id)) await ProblemAsync(deniedDownload, HttpStatusCode.Forbidden, "Document.DownloadDenied");
        var visible = await DocumentFileHttpTestRequests.ReadAsync(fixture, ordinary, Point(fixture, "document.document.read"), FilesUrl(created));
        Assert.Single(visible.GetProperty("files").EnumerateArray());
        Assert.False(visible.GetProperty("downloadAllowed").GetBoolean());
        await SaveRuleAsync(fixture, admin, "Download333Read", "document.document", "EID", created.DocumentId, "read");
        using (var hidden = await SendAsync(download.Id)) await ProblemAsync(hidden, HttpStatusCode.NotFound, "Files.UsageUnavailable");
        var durable = await ObserveBusinessAsync(fixture);
        Assert.Single(durable.Documents); Assert.Single(durable.Links);
        Assert.Equal("active", Assert.Single(durable.Usages)["State"]);
        Assert.Equal("confirmed", Assert.Single(durable.Leases)["State"]);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(ContentPath(fixture, Assert.Single(durable.Files))));

        async Task<HttpResponseMessage> SendAsync(string? button, bool legacy = false, string? path = null)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, path ?? ContentUrl(created));
            request.Headers.Add("X-NewP-Feature", point.Position);
            if (button is not null) request.Headers.Add("X-NewP-Button", button);
            if (legacy) request.Headers.Add("X-NewP-Data-Declaration", download.Id);
            return await ordinary.SendAsync(request);
        }
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "普通主体的新对象read拒绝不阻断原候选创建、首次确认或相同凭据幂等确认")]
    public async Task CreationAndConfirm_PreserveNoReadAndRejectCrossedTypedCredentials()
    {
        await using var fixture = await ControlledFixtureAsync();
        using var admin = await fixture.LoginAdministratorAsync();
        var position = await ContentAsync(fixture, admin);
        var actor = await ActorAsync(fixture, admin, "ConfirmNoRead313");
        using var ordinary = actor.Client;
        const string code = "DOC-CONFIRM-NO-READ-313";
        await SaveRuleAsync(fixture, admin, "ConfirmNoRead313Rule", "document.document", "ECODE", code, "read");
        var created = await CreateMainAsync(fixture, ordinary, position, code, [1, 2, 3]);
        using (var hidden = await DocumentFileHttpTestRequests.SendAsync(fixture, ordinary, Point(fixture, "document.document.read"), HttpMethod.Get, FilesUrl(created)))
            Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        using var scope = fixture.Services.CreateScope();
        var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        var previous = accessor.HttpContext;
        accessor.HttpContext = new DefaultHttpContext { User = new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, actor.AccountId)], "actual-confirm-owner")), RequestServices = scope.ServiceProvider };
        using var trace = new Activity(nameof(CreationAndConfirm_PreserveNoReadAndRejectCrossedTypedCredentials)).Start();
        try
        {
            var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
            var identity = await scope.ServiceProvider.GetRequiredService<DocumentInitialIdentity>().ReadCurrentAsync(created.DocumentId);
            var creation = scope.ServiceProvider.GetRequiredService<DocumentFileCreationScope>();
            var policy = scope.ServiceProvider.GetRequiredService<DocumentFileOwnerPolicy>();
            var files = scope.ServiceProvider.GetRequiredService<IFileLifecycleService>();
            var transactions = scope.ServiceProvider.GetRequiredService<IPlatformTransactionExecutor>();
            // 新owner scope取得实际持久身份；已确认重试不要求旧的活动创建证明。
            var context = await transactions.ExecuteAsync(token =>
            {
                using var proof = creation.BeginCreated(identity, out var value);
                return ValueTask.FromResult(value);
            });
            Task<ConfirmedFileUsage> Confirm(FileOwnerOperationContext value) => transactions.ExecuteAsync(token =>
                new ValueTask<ConfirmedFileUsage>(files.ConfirmUsageAsync(created.UploadId, new(DocumentFileOwnerPolicy.Key, created.LinkId), value, token))).AsTask();
            var expected = new ConfirmedFileUsage(created.UsageId, created.FileId);
            Assert.Equal(expected, await Confirm(context));
            Assert.Equal(expected, await Confirm(context));
            var legacy = new FileOwnerCreationContext(new(new("document.document.internal.create", "standardCrud.create", "submit"), "document.document", null)
            { Internal = new("document.document.internal.create", "standardCrud.create", typeof(DocumentFiles), nameof(DocumentFiles.CreateWithFilesAsync)) }, created.DocumentId);
            var crossed = await Assert.ThrowsAsync<FileLifecycleException>(() => Confirm(legacy));
            Assert.Equal("Document.FileWriteContextInvalid", crossed.Code);
            var wrongContext = await transactions.ExecuteAsync(token =>
            {
                using var proof = creation.BeginCreated(identity with { Master = new() { ["EID"] = "another-document" } }, out var value);
                return ValueTask.FromResult(value);
            });
            var wrongTarget = await Assert.ThrowsAsync<FileLifecycleException>(() => Confirm(wrongContext));
            Assert.Equal(HttpStatusCode.NotFound, (HttpStatusCode)wrongTarget.StatusCode);
            Assert.DoesNotContain(db.ChangeTracker.Entries(), entry => entry.Metadata.Name == "DocumentFileLink" && entry.State == EntityState.Added);
            // 不同owner实例不能借用私有context，真实确认与空批都必须在查关联前拒绝。
            var otherOwner = new DocumentFileCreationScope(db);
            var otherPolicy = new DocumentFileOwnerPolicy(db, otherOwner, null!, null!, null!, null!, null!, scope.ServiceProvider.GetRequiredService<DocumentFileCopyScope>());
            var foreign = await Assert.ThrowsAsync<FileLifecycleException>(() => otherPolicy.VerifyAsync(new(DocumentFileOwnerPolicy.Key, created.LinkId),
                created.FileId, created.UsageId, FileOwnerOperation.Confirm, context, default));
            Assert.Equal("Document.FileWriteContextInvalid", foreign.Code);
            var foreignEmpty = await Assert.ThrowsAsync<FileLifecycleException>(() => otherPolicy.VerifyBatchAsync([], FileOwnerOperation.Confirm, context, default));
            Assert.Equal("Document.FileWriteContextInvalid", foreignEmpty.Code);
            // 首次确认仍需当前事务、同一来源与真实Added Link；结束证明不能授权追加。
            await transactions.ExecuteAsync(async token =>
            {
                using var proof = creation.BeginCreated(identity, out var fresh);
                var link = new Dictionary<string, object?> { ["LID"] = "DocumentFileLink_context333", ["PEID"] = identity.Iteration["EID"],
                    ["CEID"] = created.FileId, ["Role"] = "attachment", ["Ordinal"] = 1L };
                var entry = db.Set<Dictionary<string, object?>>("DocumentFileLink").Add(link);
                await policy.VerifyAsync(new(DocumentFileOwnerPolicy.Key, (string)link["LID"]!), created.FileId, null, FileOwnerOperation.Confirm, fresh, token);
                proof.Dispose();
                var expired = await Assert.ThrowsAsync<FileLifecycleException>(() => policy.VerifyAsync(new(DocumentFileOwnerPolicy.Key, (string)link["LID"]!),
                    created.FileId, null, FileOwnerOperation.Confirm, fresh, token));
                Assert.Equal("Document.CreationProofRequired", expired.Code);
                entry.State = EntityState.Detached;
                return true;
            });
        }
        finally { accessor.HttpContext = previous; }
        var facts = await ObserveBusinessAsync(fixture);
        Assert.Single(facts.Documents); Assert.Single(facts.Usages);
        Assert.Equal("active", Assert.Single(facts.Usages)["State"]);
        Assert.Equal("confirmed", Assert.Single(facts.Leases)["State"]);
    }

}
