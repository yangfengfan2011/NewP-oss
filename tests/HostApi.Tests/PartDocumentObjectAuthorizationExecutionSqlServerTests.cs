using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Modules.Authorization.DTOs;
using ICP.Platform.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed class PartDocumentObjectAuthorizationExecutionSqlServerTests
{
    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "图文档复用同一owner能力，查看和管理辅助读取仍按各自原资格")]
    public async Task DocumentPermissions_PreserveReadOnlyAndManagementAuxiliarySources()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeDocument: true);
        using var admin = await fixture.LoginAdministratorAsync();
        var create = Assert.Single(fixture.FeatureCatalog, point => point.BehaviorKey == "document.document.create");
        var read = Assert.Single(fixture.FeatureCatalog, point => point.ModuleId == "Document"
            && point.Kind == "navigation" && point.Key == "document.documents");
        var manage = Assert.Single(fixture.FeatureCatalog, point => point.ModuleId == "Document"
            && point.BehaviorKey == "document.document.authorize" && point.ActionScope == "list.rowActions");
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "Auth318DocumentManager", "图文档权限角色",
            "auth318.document.manager", "图文档权限主体", read, manage);
        using var ordinary = actor.Client;
        using var created = await FeatureHttpTestRequests.SendAsync(admin, create, HttpMethod.Post, "/api/document/documents",
            new { ECODE = "AUTH318-DOC-TARGET", ENAME = "图文档权限来源" },
            OwnedResourceReadHttpRequests.Declaration(fixture, create, "document.document", "create"));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = BusinessAuditSqlServerHttpApiTests.Id(await created.Content.ReadFromJsonAsync<JsonElement>());
        var plan = fixture.Services.GetRequiredService<CanonicalModulePlan>();
        var view = Assert.Single(plan.ActionDataAccessCatalog, declaration => declaration.Execution == "document.document.authorization.view"
            && declaration.Surface == "read");
        var manager = Assert.Single(plan.ActionDataAccessCatalog, declaration => declaration.Execution == "document.document.authorization.manage"
            && declaration.Surface == "rowActions");
        var foreign = Assert.Single(plan.ActionDataAccessCatalog, declaration => declaration.Execution == "part.part.authorization.view"
            && declaration.Surface == "read");
        var root = "/api/document/documents/" + id + "/permissions";
        await SaveRuleAsync(new() { ["read"] = "allow", ["authorize"] = "deny" });
        using (var inspected = await FeatureHttpTestRequests.SendAsync(ordinary, read, HttpMethod.Get, root + "/explanation", dataAccess: view))
        {
            Assert.Equal(HttpStatusCode.OK, inspected.StatusCode);
            var body = await inspected.Content.ReadFromJsonAsync<JsonElement>();
            Assert.False(Assert.Single(body.GetProperty("actions").EnumerateArray(), action => action.GetProperty("key").GetString() == "authorize")
                .GetProperty("allowed").GetBoolean());
        }
        using (var missing = await FeatureHttpTestRequests.SendAsync(ordinary, read, HttpMethod.Get, root + "/catalog"))
            Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        using (var crossed = await FeatureHttpTestRequests.SendAsync(ordinary, read, HttpMethod.Get, root + "/catalog", dataAccess: foreign))
            Assert.Equal(HttpStatusCode.BadRequest, crossed.StatusCode);
        using (var auxiliary = await FeatureHttpTestRequests.SendAsync(ordinary, manage, HttpMethod.Get, root + "/catalog", dataAccess: manager))
            Assert.Equal(HttpStatusCode.OK, auxiliary.StatusCode);
        using (var forbidden = await FeatureHttpTestRequests.SendAsync(ordinary, manage, HttpMethod.Get, root + "/eligibility", dataAccess: manager))
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        await SaveRuleAsync(new());
        var configuration = new DataScopeObjectConfiguration("document.document", id, []);
        using (var missing = await FeatureHttpTestRequests.SendAsync(ordinary, manage, HttpMethod.Put, root, configuration))
            Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        using (var saved = await FeatureHttpTestRequests.SendAsync(ordinary, manage, HttpMethod.Put, root, configuration, manager))
            Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);

        async Task SaveRuleAsync(Dictionary<string, string> actions)
        {
            const string key = "Auth318DocumentSourceRule";
            var rule = new DataScopeRuleDto(key, key, null, "图文档原权限阈值", true, "document.document",
                new("attribute", "EID", "equal", JsonSerializer.SerializeToElement(id)),
                [new(key + "Subject", "user", actor.AccountId, 0, actions)], []);
            var save = Assert.Single(fixture.FeatureCatalog, point => point.BehaviorKey == "authorization.dataScope.save");
            using var response = await FeatureHttpTestRequests.SendAsync(admin, save, HttpMethod.Put,
                "/api/authorization/data-scope/rules/" + key, rule);
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "普通Part查看保留read且不升级authorize，声明缺失或借用创建来源仍拒绝")]
    public async Task PartReadOnly_PreservesReadWithoutAuthorizeAndRejectsMissingOrCreateSources()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includePart: true);
        using var admin = await fixture.LoginAdministratorAsync();
        var create = Assert.Single(fixture.FeatureCatalog, point => point.BehaviorKey == "part.part.create");
        var read = Assert.Single(fixture.FeatureCatalog, point => point.ModuleId == "Part"
            && point.Kind == "navigation" && point.Key == "part.parts");
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "Auth318PartViewer", "只读权限查看角色",
            "auth318.part.viewer", "只读权限查看主体", read);
        using var ordinary = actor.Client;
        var createSource = OwnedResourceReadHttpRequests.Declaration(fixture, create, "part.part", "create");
        using var created = await FeatureHttpTestRequests.SendAsync(admin, create, HttpMethod.Post, "/api/part/parts",
            new { ECODE = "AUTH318-PART-VIEW", ENAME = "只读授权查看目标" }, createSource);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = BusinessAuditSqlServerHttpApiTests.Id(await created.Content.ReadFromJsonAsync<JsonElement>());
        var saveRules = Assert.Single(fixture.FeatureCatalog, point => point.BehaviorKey == "authorization.dataScope.save");
        const string ruleId = "Auth318PartViewRule";
        var rule = new DataScopeRuleDto(ruleId, ruleId, null, "可读但不可授权", true, "part.part",
            new("attribute", "EID", "equal", JsonSerializer.SerializeToElement(id)),
            [new("Auth318PartViewSubject", "user", actor.AccountId, 0,
                new Dictionary<string, string> { ["read"] = "allow", ["authorize"] = "deny" })], []);
        using (var saved = await FeatureHttpTestRequests.SendAsync(admin, saveRules, HttpMethod.Put,
            "/api/authorization/data-scope/rules/" + ruleId, rule))
            Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);
        var path = "/api/part/parts/" + id + "/permissions/explanation";
        var readSource = Assert.Single(fixture.Services.GetRequiredService<CanonicalModulePlan>().ActionDataAccessCatalog,
            declaration => declaration.Execution == "part.part.authorization.view" && declaration.ViewKey == "part.part.permissions"
                && declaration.Surface == "read" && declaration.FeaturePositions.Contains(read.Position));
        using (var allowed = await FeatureHttpTestRequests.SendAsync(ordinary, read, HttpMethod.Get, path, dataAccess: readSource))
        {
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
            var body = await allowed.Content.ReadFromJsonAsync<JsonElement>();
            Assert.False(Assert.Single(body.GetProperty("actions").EnumerateArray(), action => action.GetProperty("key").GetString() == "authorize")
                .GetProperty("allowed").GetBoolean());
        }
        using var missing = await FeatureHttpTestRequests.SendAsync(ordinary, read, HttpMethod.Get, path);
        using var wrongOperation = await FeatureHttpTestRequests.SendAsync(ordinary, read, HttpMethod.Get, path, dataAccess: createSource);
        var detailSource = Assert.Single(fixture.Services.GetRequiredService<CanonicalModulePlan>().ActionDataAccessCatalog,
            declaration => declaration.Execution == "part.part.read" && declaration.ViewKey == "part.part.detail" && declaration.Surface == "read");
        using var wrongReadEntry = await FeatureHttpTestRequests.SendAsync(ordinary, read, HttpMethod.Get, path, dataAccess: detailSource);
        Assert.True(missing.StatusCode == HttpStatusCode.BadRequest && wrongOperation.StatusCode == HttpStatusCode.BadRequest
            && wrongReadEntry.StatusCode == HttpStatusCode.BadRequest,
            $"missing={(int)missing.StatusCode}, borrowedCreate={(int)wrongOperation.StatusCode}, borrowedDetail={(int)wrongReadEntry.StatusCode}");
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "真实Part授权入口拒绝缺声明和借用另一资源的合法管理声明")]
    public async Task PartManagement_RejectsMissingAndBorrowedResourceDeclarations()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includePart: true);
        using var admin = await fixture.LoginAdministratorAsync();
        var create = Assert.Single(fixture.FeatureCatalog, point => point.BehaviorKey == "part.part.create");
        var manage = Assert.Single(fixture.FeatureCatalog, point => point.ModuleId == "Part"
            && point.BehaviorKey == "part.part.authorize" && point.ActionScope == "list.rowActions");
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "Auth318PartManager", "零组件授权角色",
            "auth318.part.manager", "零组件授权普通主体", manage);
        using var ordinary = actor.Client;
        using var created = await FeatureHttpTestRequests.SendAsync(admin, create, HttpMethod.Post, "/api/part/parts",
            new { ECODE = "AUTH318-PART-TARGET", ENAME = "授权来源真实零组件" },
            OwnedResourceReadHttpRequests.Declaration(fixture, create, "part.part", "create"));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = BusinessAuditSqlServerHttpApiTests.Id(await created.Content.ReadFromJsonAsync<JsonElement>());
        var plan = fixture.Services.GetRequiredService<CanonicalModulePlan>();
        var borrowed = Assert.Single(plan.ActionDataAccessCatalog, declaration =>
            declaration.Execution == "authorization.role.authorization.manage" && declaration.Provider == "authorization.object.manage");
        var path = "/api/part/parts/" + id + "/permissions/eligibility";
        var own = Assert.Single(plan.ActionDataAccessCatalog, declaration =>
            declaration.Execution == "part.part.authorization.manage" && declaration.Surface == "rowActions");
        using (var allowed = await FeatureHttpTestRequests.SendAsync(ordinary, manage, HttpMethod.Get, path, dataAccess: own))
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        using var missing = await FeatureHttpTestRequests.SendAsync(ordinary, manage, HttpMethod.Get, path);
        using var wrongResource = await FeatureHttpTestRequests.SendAsync(ordinary, manage, HttpMethod.Get, path, dataAccess: borrowed);
        Assert.True(missing.StatusCode == HttpStatusCode.BadRequest && wrongResource.StatusCode == HttpStatusCode.BadRequest,
            $"missing={(int)missing.StatusCode}, borrowedRole={(int)wrongResource.StatusCode}");

        // 原owner的三个辅助读端点仅read；管理来源不能把它们升级为authorize。
        await SaveRuleAsync(new() { ["read"] = "allow", ["authorize"] = "deny" });
        foreach (var suffix in new[] { "/catalog", "/subjects/user", "/explanation" })
        {
            using var auxiliary = await FeatureHttpTestRequests.SendAsync(ordinary, manage, HttpMethod.Get,
                "/api/part/parts/" + id + "/permissions" + suffix, dataAccess: own);
            Assert.Equal(HttpStatusCode.OK, auxiliary.StatusCode);
        }
        foreach (var suffix in new[] { "/eligibility", "", "/subjects/role" })
        {
            using var denied = await FeatureHttpTestRequests.SendAsync(ordinary, manage, HttpMethod.Get,
                "/api/part/parts/" + id + "/permissions" + suffix, dataAccess: own);
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        }
        var viewSource = Assert.Single(plan.ActionDataAccessCatalog, declaration =>
            declaration.Execution == "part.part.authorization.view" && declaration.Surface == "read");
        using (var crossed = await FeatureHttpTestRequests.SendAsync(ordinary, manage, HttpMethod.Get, path, dataAccess: viewSource))
            Assert.Equal(HttpStatusCode.BadRequest, crossed.StatusCode);

        // 没有edit仍可完整委派edit；提交后失去read/authorize仍成功且不回读敏感配置。
        await SaveRuleAsync(new() { ["edit"] = "deny" });
        var configuration = new DataScopeObjectConfiguration("part.part", id,
            [new("Auth318PartSelf", "user", actor.AccountId, 1,
                new Dictionary<string, string> { ["read"] = "deny", ["authorize"] = "deny", ["edit"] = "allow" })]);
        using (var saved = await FeatureHttpTestRequests.SendAsync(ordinary, manage, HttpMethod.Put,
            "/api/part/parts/" + id + "/permissions", configuration, own))
            Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);
        using (var lost = await FeatureHttpTestRequests.SendAsync(ordinary, manage, HttpMethod.Get,
            "/api/part/parts/" + id + "/permissions", dataAccess: own))
            Assert.Equal(HttpStatusCode.NotFound, lost.StatusCode);

        async Task SaveRuleAsync(Dictionary<string, string> actions)
        {
            const string root = "Auth318PartManagerRule";
            var rule = new DataScopeRuleDto(root, root, null, "管理来源原资格", true, "part.part",
                new("attribute", "EID", "equal", JsonSerializer.SerializeToElement(id)),
                [new(root + "Subject", "user", actor.AccountId, 0, actions)], []);
            var save = Assert.Single(fixture.FeatureCatalog, point => point.BehaviorKey == "authorization.dataScope.save");
            using var response = await FeatureHttpTestRequests.SendAsync(admin, save, HttpMethod.Put,
                "/api/authorization/data-scope/rules/" + root, rule);
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }
    }
}
