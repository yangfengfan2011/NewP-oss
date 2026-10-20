using System.Net;
using System.Net.Http.Json;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Modules.Authorization.Services;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Text.Json;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Modules.Workspace;
using ICP.Platform.Modules.Authorization.DTOs;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static ICP.Platform.HostApi.Tests.EntityOwnershipBackfillSqlServerHttpApiTests;

namespace ICP.Platform.HostApi.Tests;

public sealed class WorkspaceObjectExecutionSqlServerTests
{
    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "Workspace真实Part创建缺声明在写前拒绝；合法普通候选创建后不可读仍成功")]
    public async Task PartCreate_RequiresItsOwnDeclarationAndKeepsUnreadableSuccess()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        var position = await ContainerAsync(fixture, admin);
        var entry = Assert.Single(fixture.FeatureCatalog, point => point.ModuleId == "Workspace" && point.Kind == "navigation" && point.Key == "workspace");
        var create = Assert.Single(fixture.FeatureCatalog, point => point.ModuleId == "Workspace"
            && point.BehaviorKey == "workspace.folder.createPart" && point.ActionScope == "list.pageActions");
        var read = Assert.Single(fixture.FeatureCatalog, point => point.ModuleId == "Part"
            && point.Kind == "navigation" && point.Key == "part.parts");
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "WorkspacePart315", "组合Part创建",
            "workspace-part315", "组合Part普通主体", entry, create, read);
        using var ordinary = actor.Client;
        var body = new { position, values = new { ECODE = "WORKSPACE-PART-315", ENAME = "创建后不可读" } };
        var before = await Snapshot();
        using (var missing = await FeatureHttpTestRequests.SendAsync(ordinary, create, HttpMethod.Post,
            "/api/workspace/content/parts", body))
            Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Equal(before, await Snapshot());

        var rule = new DataScopeRuleDto("WorkspacePartHidden315", "WorkspacePartHidden315", null, "组合候选创建后不可读", true,
            "part.part", new("attribute", "ECODE", "equal", JsonSerializer.SerializeToElement(body.values.ECODE)),
            [new("WorkspacePartHiddenSubject315", "all", null, 0, new Dictionary<string, string> { ["read"] = "deny" })], []);
        using (var saved = await new FeatureHttpTestRequests(fixture).SendButtonAsync(admin, Point(fixture, "authorization.dataScope.save"), HttpMethod.Put,
            "/api/authorization/data-scope/rules/WorkspacePartHidden315", rule))
            Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);
        var plan = fixture.Services.GetRequiredService<CanonicalModulePlan>();
        var declaration = Assert.Single(plan.ActionDataAccessCatalog, item => item.Execution == "workspace.content.createPart"
            && item.FeaturePositions.Contains(create.Position));
        using (var created = await FeatureHttpTestRequests.SendAsync(ordinary, create, HttpMethod.Post,
            "/api/workspace/content/parts", body, declaration))
        {
            Assert.Equal(HttpStatusCode.OK, created.StatusCode);
            var receipt = JsonSerializer.Deserialize<JsonElement>(await created.Content.ReadAsStringAsync());
            Assert.True(receipt.GetProperty("succeeded").GetBoolean());
            Assert.False(receipt.TryGetProperty("id", out _));
        }
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
        var part = await db.Set<Dictionary<string, object?>>("Part").AsNoTracking()
            .SingleAsync(row => EF.Property<string>(row, "ECODE") == body.values.ECODE);
        var id = (string)part["EID"]!;
        Assert.True(await db.Set<Dictionary<string, object?>>("WorkspaceFolderContent").AsNoTracking()
            .AnyAsync(row => EF.Property<string>(row, "ChildModelKey") == "Part" && EF.Property<string>(row, "CEID") == id));
        var readDeclaration = Assert.Single(plan.ActionDataAccessCatalog, item => item.Execution == "part.part.read"
            && item.FeaturePositions.Contains(read.Position));
        using var hidden = await FeatureHttpTestRequests.SendAsync(ordinary, read, HttpMethod.Get, "/api/part/parts/" + id,
            dataAccess: readDeclaration);
        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);

        async Task<string> Snapshot()
        {
            using var snapshotScope = fixture.Services.CreateScope();
            var snapshotDb = snapshotScope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
            var counts = new List<int>();
            foreach (var model in new[] { "Part", "WorkspaceFolderContent", "BusinessChange" })
                counts.Add(await snapshotDb.Set<Dictionary<string, object?>>(model).CountAsync());
            return JsonSerializer.Serialize(counts);
        }
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "真实图文档上传和确认复用同一组合声明；缺/串用来源不产生File或消费lease，新对象不可读仍成功")]
    public async Task DocumentCreate_UsesActualUploadCredentialsAndRejectsMissingOrCrossedSource()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        var container = await ContainerAsync(fixture, admin);
        var position = new WorkspacePosition(container.SpaceId, container.RootFolderId, container.LinkIds);
        var entry = Assert.Single(fixture.FeatureCatalog, point => point.ModuleId == "Workspace" && point.Kind == "navigation" && point.Key == "workspace");
        var create = Assert.Single(fixture.FeatureCatalog, point => point.ModuleId == "Workspace"
            && point.BehaviorKey == "workspace.folder.createDocument" && point.ActionScope == "list.pageActions");
        var read = Assert.Single(fixture.FeatureCatalog, point => point.ModuleId == "Document" && point.Kind == "navigation" && point.Key == "document.documents");
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "WorkspaceDoc315", "组合图文档",
            "workspace-doc315", "组合图文档普通主体", entry, create, read);
        using var ordinary = actor.Client;
        var plan = fixture.Services.GetRequiredService<CanonicalModulePlan>();
        var declaration = Assert.Single(plan.ActionDataAccessCatalog, item => item.Execution == "workspace.content.createDocument"
            && item.FeaturePositions.Contains(create.Position));
        var crossed = Assert.Single(plan.ActionDataAccessCatalog, item => item.Execution == "workspace.content.createPart");
        var values = new { ECODE = "WORKSPACE-DOC-315", ENAME = "带文件且创建后不可读" };
        var beforeStage = await ObjectSnapshot(fixture);
        foreach (var source in new[] { (string?)null, crossed.Id })
        {
            using var request = DocumentFilesSqlServerHttpApiTests.UploadRequest(fixture, position, values,
                new ByteArrayContent([0x25, 0x50, 0x44, 0x46]), "source315.pdf");
            if (source is not null) request.Headers.Add("X-NewP-Data-Declaration", source);
            using var rejected = await ordinary.SendAsync(request);
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
            Assert.Equal(beforeStage, await ObjectSnapshot(fixture));
        }
        await SaveRule(fixture, admin, "WorkspaceDocHidden315", "document.document", "ECODE", values.ECODE, "read", "deny");
        string uploadId;
        using (var request = DocumentFilesSqlServerHttpApiTests.UploadRequest(fixture, position, values,
            new ByteArrayContent([0x25, 0x50, 0x44, 0x46]), "source315.pdf"))
        {
            request.Headers.Add("X-NewP-Data-Declaration", declaration.Id);
            using var staged = await ordinary.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, staged.StatusCode);
            uploadId = JsonSerializer.Deserialize<JsonElement>(await staged.Content.ReadAsStringAsync()).GetProperty("uploadId").GetString()!;
        }
        var beforeCreate = await ObjectSnapshot(fixture);
        var body = new { position, values, uploads = new { mainUploadId = uploadId } };
        foreach (var source in new[] { (ActionDataAccessRequirement?)null, crossed })
        {
            using var rejected = await FeatureHttpTestRequests.SendAsync(ordinary, create, HttpMethod.Post,
                "/api/workspace/content/documents", body, source);
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
            Assert.Equal(beforeCreate, await ObjectSnapshot(fixture));
        }
        using (var saved = await FeatureHttpTestRequests.SendAsync(ordinary, create, HttpMethod.Post,
            "/api/workspace/content/documents", body, declaration))
        {
            Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
            var receipt = JsonSerializer.Deserialize<JsonElement>(await saved.Content.ReadAsStringAsync());
            Assert.True(receipt.GetProperty("succeeded").GetBoolean());
            Assert.False(receipt.TryGetProperty("id", out _));
        }
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
        var document = await db.Set<Dictionary<string, object?>>("Document").AsNoTracking().SingleAsync();
        var link = await db.Set<Dictionary<string, object?>>("DocumentFileLink").AsNoTracking().SingleAsync();
        var usage = await db.Set<Dictionary<string, object?>>("FileUsage").AsNoTracking().SingleAsync();
        var lease = await db.Set<Dictionary<string, object?>>("FileUploadLease").AsNoTracking().SingleAsync();
        Assert.Equal("active", usage["State"]); Assert.Equal("confirmed", lease["State"]);
        Assert.Equal(usage["EID"], link["FileUsageId"]); Assert.Equal(usage["EID"], lease["ConfirmedUsageId"]);
        var readDeclaration = Assert.Single(plan.ActionDataAccessCatalog, item => item.Execution == "document.document.read"
            && item.ViewKey == "document.document.detail" && item.FeaturePositions.Contains(read.Position));
        using var hidden = await FeatureHttpTestRequests.SendAsync(ordinary, read, HttpMethod.Get,
            "/api/document/documents/" + document["EID"], dataAccess: readDeclaration);
        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "混合选择删除核后续Document资格且不串用移除引用来源，全部引用与对象同事务")]
    public async Task MixedDelete_RejectsLaterTargetAndReferenceSourceBeforeAnyCleanup()
    {
        var checkedTargets = new List<(string Object, string Id, string[] Actions)>();
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(services =>
            services.Replace(ServiceDescriptor.Scoped<IObjectActionAccess>(provider =>
                new TracedObjectActions(ActivatorUtilities.CreateInstance<ObjectActionAccess>(provider), checkedTargets))),
            includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        var position = await ContainerAsync(fixture, admin);
        var part = await CreateAsync(fixture, admin, position, "Part", "parts", "createPart", "MixedPart315");
        var document = await CreateAsync(fixture, admin, position, "Document", "documents", "createDocument", "MixedDoc315");
        var entry = Assert.Single(fixture.FeatureCatalog, point => point.ModuleId == "Workspace" && point.Kind == "navigation" && point.Key == "workspace");
        var delete = Assert.Single(fixture.FeatureCatalog, point => point.ModuleId == "Workspace" && point.Key == "deleteObjects" && point.ActionScope == "list.pageActions");
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "MixedDelete315", "混合删除",
            "mixed-delete315", "混合删除普通主体", entry, delete);
        using var ordinary = actor.Client;
        var plan = fixture.Services.GetRequiredService<CanonicalModulePlan>();
        var declaration = Assert.Single(plan.ActionDataAccessCatalog, item => item.ActionKey == delete.Key && item.Surface == "pageActions"
            && item.FeaturePositions.Contains(delete.Position));
        var remove = Assert.Single(plan.ActionDataAccessCatalog, item => item.Execution == "workspace.clipboard.remove" && item.ActionKey == "removeSelected");
        var body = new { position, linkIds = new[] { part.LinkId, document.LinkId } };
        var before = await ObjectSnapshot(fixture);
        foreach (var source in new[] { (ActionDataAccessRequirement?)null, remove })
        {
            using var rejected = await FeatureHttpTestRequests.SendAsync(ordinary, delete, HttpMethod.Post,
                "/api/workspace/content/delete", body, source);
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
            Assert.Equal(before, await ObjectSnapshot(fixture));
        }
        await SaveRule(fixture, admin, "MixedDocDenied315", "document.document", "EID", document.Id, "delete", "deny");
        before = await ObjectSnapshot(fixture);
        using (var denied = await SendButtonAsync(ordinary, delete, declaration.Id, body))
            Assert.True(denied.StatusCode == HttpStatusCode.Forbidden, await denied.Content.ReadAsStringAsync());
        Assert.Equal(new[] { part.Id, document.Id }, checkedTargets.Where(item => item.Actions.Contains("delete"))
            .Select(item => item.Id).ToArray());
        Assert.Equal(before, await ObjectSnapshot(fixture));
        await SaveRule(fixture, admin, "MixedDocDenied315", "document.document", "EID", document.Id, "delete", "allow");
        using var accepted = await SendButtonAsync(ordinary, delete, declaration.Id, body);
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
        Assert.False(await db.Set<Dictionary<string, object?>>("Part").AnyAsync(row => EF.Property<string>(row, "EID") == part.Id));
        Assert.False(await db.Set<Dictionary<string, object?>>("Document").AnyAsync(row => EF.Property<string>(row, "EID") == document.Id));
        Assert.False(await db.Set<Dictionary<string, object?>>("WorkspaceFolderContent").AnyAsync(row =>
            EF.Property<string>(row, "CEID") == part.Id || EF.Property<string>(row, "CEID") == document.Id));
    }

    // 请求身份取自实际编译条目；迁移前旧条目不能用新header冒充可执行按钮。
    private static async Task<HttpResponseMessage> SendButtonAsync(HttpClient client, FeaturePoint point, string buttonId, object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/workspace/content/delete");
        request.Headers.Add("X-NewP-Feature", point.Position);
        request.Headers.Add("X-NewP-Button", buttonId);
        request.Content = JsonContent.Create(body, options: new JsonSerializerOptions());
        return await client.SendAsync(request);
    }

    // 只记录真实通用判权调用；仍由正式资源读取与规则引擎给出结果，不替换允许/拒绝。
    private sealed class TracedObjectActions(ObjectActionAccess inner,
        List<(string Object, string Id, string[] Actions)> calls) : IObjectActionAccess
    {
        public Task<DataExecutionDecision> CheckAsync(string name, string id, IReadOnlyList<string> actions,
            CancellationToken token = default)
        {
            calls.Add((name, id, actions.ToArray()));
            return inner.CheckAsync(name, id, actions, token);
        }
        public Task<DataExecutionDecision> CheckAsync(string name, string id, IReadOnlyList<string> actions,
            object context, CancellationToken token = default)
        {
            calls.Add((name, id, actions.ToArray()));
            return inner.CheckAsync(name, id, actions, context, token);
        }
        public Task<DataExecutionDecision> CheckCandidateAsync(string name, IReadOnlyDictionary<string, object?> candidate,
            IReadOnlyList<string> actions, CancellationToken token = default) => inner.CheckCandidateAsync(name, candidate, actions, token);
    }

    // 使用真实规则API，Id/RootId保持同一根；不注入恒真provider或伪造主体权限。
    private static async Task SaveRule(BusinessAuditHostFixture fixture, HttpClient admin, string key,
        string resource, string field, string value, string action, string opinion)
    {
        var rule = new DataScopeRuleDto(key, key, null, key, true, resource,
            new("attribute", field, "equal", JsonSerializer.SerializeToElement(value)),
            [new(key + "Subject", "all", null, 0, new Dictionary<string, string> { [action] = opinion })], []);
        using var saved = await new FeatureHttpTestRequests(fixture).SendButtonAsync(admin, Point(fixture, "authorization.dataScope.save"), HttpMethod.Put,
            "/api/authorization/data-scope/rules/" + key, rule);
        Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);
    }

    // 快照覆盖被此组合改动的实际对象、引用、文件凭据与审计；拒绝不能消费lease或部分清理。
    private static async Task<string> ObjectSnapshot(BusinessAuditHostFixture fixture)
    {
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
        var counts = new Dictionary<string, int>();
        foreach (var model in new[] { "Part", "Document", "DocumentVersion", "DocumentIteration", "WorkspaceFolderContent",
            "DocumentFileLink", "FileRecord", "FileUsage", "FileUploadLease", "BusinessChange" })
            counts[model] = await db.Set<Dictionary<string, object?>>(model).CountAsync();
        return JsonSerializer.Serialize(counts);
    }
}
