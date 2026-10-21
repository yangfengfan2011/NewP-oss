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
            var contract = Assert.Single(fixture.Services.GetRequiredService<CanonicalModulePlan>().Modules.SelectMany(module => module.DataExecutions), item => item.Key == "document.document.internal.create");
            var source = new DataExecutionWriteContext(new(contract.Key, contract.Provider, "submit"), "document.document", null)
            { Internal = new(contract.Key, contract.Provider, typeof(DocumentFiles), nameof(DocumentFiles.CreateWithFilesAsync)) };
            var context = new FileOwnerCreationContext(source, created.DocumentId);
            var files = scope.ServiceProvider.GetRequiredService<IFileLifecycleService>();
            var transactions = scope.ServiceProvider.GetRequiredService<IPlatformTransactionExecutor>();
            Task<ConfirmedFileUsage> Confirm(FileOwnerCreationContext value) => transactions.ExecuteAsync(token =>
                new ValueTask<ConfirmedFileUsage>(files.ConfirmUsageAsync(created.UploadId, new(DocumentFileOwnerPolicy.Key, created.LinkId), value, token))).AsTask();
            var expected = new ConfirmedFileUsage(created.UsageId, created.FileId);
            Assert.Equal(expected, await Confirm(context));
            Assert.Equal(expected, await Confirm(context));
            var crossed = await Assert.ThrowsAsync<FileLifecycleException>(() => Confirm(context with { Source = source with { ResourceKey = "part.part" } }));
            Assert.Equal("Document.FileWriteContextInvalid", crossed.Code);
            var wrongTarget = await Assert.ThrowsAsync<FileLifecycleException>(() => Confirm(context with { ObjectId = "another-document" }));
            Assert.Equal(HttpStatusCode.NotFound, (HttpStatusCode)wrongTarget.StatusCode);
            Assert.DoesNotContain(scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>().ChangeTracker.Entries(), entry => entry.Metadata.Name == "DocumentFileLink" && entry.State == EntityState.Added);
        }
        finally { accessor.HttpContext = previous; }
        var facts = await ObserveBusinessAsync(fixture);
        Assert.Single(facts.Documents); Assert.Single(facts.Usages);
        Assert.Equal("active", Assert.Single(facts.Usages)["State"]);
        Assert.Equal("confirmed", Assert.Single(facts.Leases)["State"]);
    }

}
