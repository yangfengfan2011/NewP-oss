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
    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "独立Document创建只消费自身正式声明，完整首版仍同事务生成，关闭的更新删除URL不扩权")]
    public async Task DocumentEntryCreate_RequiresOwnDeclarationAndPreservesClosedWrites()
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
        var declaration = Assert.Single(plan.ActionDataAccessCatalog, item => item.Execution == "document.document.create"
            && item.ViewKey == "document.document.list" && item.Surface == "pageActions" && item.ActionKey == "create"
            && item.FeaturePositions.Contains(create.Position));
        var read = Assert.Single(plan.ActionDataAccessCatalog, item => item.Execution == "document.document.read"
            && item.ViewKey == "document.document.detail");
        using (var crossed = await FeatureHttpTestRequests.SendAsync(ordinary, create, HttpMethod.Post, "/api/document/documents", values, read))
            Assert.Equal(HttpStatusCode.BadRequest, crossed.StatusCode);
        using var created = await FeatureHttpTestRequests.SendAsync(ordinary, create, HttpMethod.Post, "/api/document/documents", values, declaration);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
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
        foreach (var method in new[] { HttpMethod.Put, HttpMethod.Delete })
        {
            using var closed = await FeatureHttpTestRequests.SendAsync(ordinary, create, method, "/api/document/documents/" + id,
                new { ENAME = "独立URL仍关闭" }, declaration);
            Assert.Equal(HttpStatusCode.Forbidden, closed.StatusCode);
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
