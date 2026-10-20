using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Modules.Authorization.DTOs;
using ICP.Platform.Modules.Authorization.Services;
using ICP.Platform.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed class PositionButtonExecutionSqlServerTests
{
    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "岗位编辑按按钮自身要求执行，规范目标来自正式路由")]
    public async Task PositionUpdate_UsesButtonIdentityAndRejectsMissingOrBorrowedIdentity()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync();
        using var admin = await fixture.LoginAdministratorAsync();
        var http = new FeatureHttpTestRequests(fixture);
        var create = http.Organization("positions", "create", "list.pageActions");
        var created = await FeatureHttpTestRequests.CreateAsync(admin, create, "/api/organization/positions",
            new { ENAME = "按钮声明岗位323" }, http.Declaration("organization.position.create", create));
        var id = BusinessAuditSqlServerHttpApiTests.Id(created);
        var edit = http.Organization("positions", "edit", "list.rowActions");
        var plan = fixture.Services.GetRequiredService<CanonicalModulePlan>();
        var button = plan.ActionDataAccessCatalog.Single(item => item.ViewKey == "organization.position.list"
            && item.Surface == "rowActions" && item.ActionKey == "edit");
        using var request = new HttpRequestMessage(HttpMethod.Put, "/api/organization/positions/" + id);
        request.Headers.Add("X-NewP-Button", button.Id);
        request.Headers.Add("X-NewP-Feature", edit.Position);
        request.Content = JsonContent.Create(new { ENAME = "按钮编辑成功323" }, options: new JsonSerializerOptions());
        using var response = await admin.SendAsync(request);
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        var path = "/api/organization/positions/" + id;
        var detailFeature = http.Organization("positions", "edit", "detail.pageActions");
        var detailButton = plan.ActionDataAccessCatalog.Single(item => item.ViewKey == "organization.position.detail" && item.ActionKey == "edit");
        using (var rejected = await SendAsync(admin, detailFeature, path, button.Id, new { ENAME = "列表身份不得借详情位置" }))
            Assert.Equal(HttpStatusCode.Forbidden, rejected.StatusCode);
        using (var rejected = await SendAsync(admin, edit, path, detailButton.Id, new { ENAME = "详情身份不得借列表位置" }))
            Assert.Equal(HttpStatusCode.Forbidden, rejected.StatusCode);
        using (var detailWrite = await SendAsync(admin, detailFeature, path, detailButton.Id, new { ENAME = "按钮编辑成功323" }))
            Assert.Equal(HttpStatusCode.OK, detailWrite.StatusCode);
        // 同一业务的两个按钮独立赋权；合法详情身份不能继承仅授予列表按钮的主体资格。
        var rowActor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "ButtonRowOnly323", "仅岗位列表编辑",
            "button-row-only-323", "岗位列表用户", [http.Organization("positions"), edit]);
        using (var ordinary = rowActor.Client)
        {
            using var denied = await SendAsync(ordinary, detailFeature, path, detailButton.Id, new { ENAME = "不得隐式授予详情编辑" });
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            using var allowed = await SendAsync(ordinary, edit, path, button.Id, new { ENAME = "按钮编辑成功323" });
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        }
        using (var missingFeature = new HttpRequestMessage(HttpMethod.Put, path))
        {
            missingFeature.Headers.Add("X-NewP-Button", button.Id);
            missingFeature.Content = JsonContent.Create(new { ENAME = "不得缺少功能位置" }, options: new JsonSerializerOptions());
            using var rejected = await admin.SendAsync(missingFeature);
            Assert.True((int)rejected.StatusCode is >= 400 and < 500, await rejected.Content.ReadAsStringAsync());
        }
        foreach (var invalid in new string?[] { null, "unknown/button", http.Declaration("organization.position.create", create).Id })
        {
            using var rejected = await SendAsync(admin, edit, path, invalid, new { ENAME = "不得写入" });
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        }
        using (var rejected = await SendAsync(admin, http.Organization("positions"), path, button.Id, new { ENAME = "不得串位置" }))
            Assert.Equal(HttpStatusCode.Forbidden, rejected.StatusCode);
        using (var rejected = await SendAsync(admin, edit, path, button.Id, new { ENAME = "不得替换", EID = "Position_other" }))
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        using (var rejected = await SendAsync(admin, edit, path, button.Id, new { ENAME = "不得混写" }, button.Id))
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        using (var rejected = await SendAsync(admin, edit, path, null, new { ENAME = "旧身份不得补救" }, button.Id))
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        Assert.Equal("按钮编辑成功323", await NameAsync(http, admin, id));
        Assert.Equal("Position", button.Object);
        Assert.Equal(new[] { "edit" }, button.Actions);
        Assert.DoesNotContain(plan.Modules.SelectMany(module => module.DataExecutions), item => item.Key == "organization.position.update");
    }

    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "编辑前事实判权，写后不可读仍成功，下一请求与明确拒绝及故障均不写")]
    public async Task PositionUpdate_BeforeFactsCurrentDenialAndDependencyFailureArePreserved()
    {
        var fault = new DecisionFault();
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(services =>
            services.AddScoped<IDataScopeObjectDecisionProvider>(provider => new FaultedDecision(
                (DataScopeReadPlans)provider.GetRequiredService<IDataScopeReadPlanProvider>(), fault)));
        using var admin = await fixture.LoginAdministratorAsync();
        var http = new FeatureHttpTestRequests(fixture);
        var create = http.Organization("positions", "create", "list.pageActions");
        var created = await FeatureHttpTestRequests.CreateAsync(admin, create, "/api/organization/positions",
            new { ENAME = "编辑前可读323" }, http.Declaration("organization.position.create", create));
        var id = BusinessAuditSqlServerHttpApiTests.Id(created);
        var edit = http.Organization("positions", "edit", "list.rowActions");
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "ButtonBefore323", "岗位按钮事实",
            "button-before-323", "岗位事实用户", [http.Organization("positions"), edit]);
        using var ordinary = actor.Client;
        var button = fixture.Services.GetRequiredService<CanonicalModulePlan>().ActionDataAccessCatalog.Single(item =>
            item.ViewKey == "organization.position.list" && item.Surface == "rowActions" && item.ActionKey == "edit");
        var path = "/api/organization/positions/" + id;
        await SaveRuleAsync(fixture, admin, "ENAME", "编辑前可读323", "deny");
        using (var denied = await SendAsync(ordinary, edit, path, button.Id, new { ENAME = "不得写入" }))
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Equal("编辑前可读323", await NameAsync(http, admin, id));
        fault.Throw = true;
        try
        {
            using var failed = await SendAsync(ordinary, edit, path, button.Id, new { ENAME = "不得故障后写入" });
            Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
        }
        finally { fault.Throw = false; }
        Assert.Equal("编辑前可读323", await NameAsync(http, admin, id));
        await SaveRuleAsync(fixture, admin, "ENAME", "编辑前可读323", "allow");
        await SaveRuleAsync(fixture, admin, "ENAME", "编辑后不可读323", "deny", "deny", "ButtonAfter323");
        using (var updated = await SendAsync(ordinary, edit, path, button.Id, new { ENAME = "编辑后不可读323" }))
        {
            Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
            var receipt = await updated.Content.ReadFromJsonAsync<JsonElement>();
            Assert.True(receipt.GetProperty("succeeded").GetBoolean());
        }
        Assert.Equal("编辑后不可读323", await NameAsync(http, admin, id));
        using (var denied = await SendAsync(ordinary, edit, path, button.Id, new { ENAME = "不能沿用前次允许" }))
            Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        Assert.Equal("编辑后不可读323", await NameAsync(http, admin, id));
    }

    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "edit停用仍保留完整读取依赖，read停用使用原裁决语义")]
    public async Task PositionUpdate_DisabledEditRetainsReadDependency()
    {
        foreach (var readEnabled in new[] { true, false })
        {
            await using var fixture = await BusinessAuditHostFixture.CreateAsync(actionStates:
                new Dictionary<string, bool> { ["edit"] = false, ["read"] = readEnabled });
            using var admin = await fixture.LoginAdministratorAsync();
            var http = new FeatureHttpTestRequests(fixture);
            var create = http.Organization("positions", "create", "list.pageActions");
            var created = await FeatureHttpTestRequests.CreateAsync(admin, create, "/api/organization/positions",
                new { ENAME = "动作开关323" }, http.Declaration("organization.position.create", create));
            var id = BusinessAuditSqlServerHttpApiTests.Id(created);
            var edit = http.Organization("positions", "edit", "list.rowActions");
            var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "ButtonSwitch323", "岗位按钮开关",
                "button-switch-323", "岗位开关用户", [http.Organization("positions"), edit]);
            using var ordinary = actor.Client;
            await SaveRuleAsync(fixture, admin, "EID", id, "deny", "deny");
            var button = fixture.Services.GetRequiredService<CanonicalModulePlan>().ActionDataAccessCatalog.Single(item =>
                item.ViewKey == "organization.position.list" && item.Surface == "rowActions" && item.ActionKey == "edit");
            using var response = await SendAsync(ordinary, edit, "/api/organization/positions/" + id, button.Id,
                new { ENAME = "动作停用后323" });
            Assert.Equal(readEnabled ? HttpStatusCode.NotFound : HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(readEnabled ? "动作开关323" : "动作停用后323", await NameAsync(http, admin, id));
        }
    }

    private static async Task<string?> NameAsync(FeatureHttpTestRequests http, HttpClient admin, string id)
    {
        var read = http.Organization("positions");
        var detail = await FeatureHttpTestRequests.ReadAsync(admin, read, "/api/organization/positions/" + id,
            http.Declaration("organization.position.read", read));
        return detail.GetProperty("fields").GetProperty("ENAME").GetString();
    }

    private static async Task SaveRuleAsync(BusinessAuditHostFixture fixture, HttpClient admin,
        string field, string value, string edit, string read = "allow", string key = "ButtonRule323")
    {
        var rule = new DataScopeRuleDto(key, key, null, key, true, "organization.position",
            new("attribute", field, "equal", JsonSerializer.SerializeToElement(value)),
            [new("Subject_" + key, "all", null, 0, new Dictionary<string, string> { ["read"] = read, ["edit"] = edit })], []);
        using var response = await FeatureHttpTestRequests.SendAsync(admin,
            fixture.FeatureCatalog.Single(point => point.BehaviorKey == "authorization.dataScope.save"), HttpMethod.Put,
            "/api/authorization/data-scope/rules/" + key, rule);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, FeaturePoint feature, string path,
        string? buttonId, object body, string? legacy = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, path);
        request.Headers.Add("X-NewP-Feature", feature.Position);
        if (buttonId is not null) request.Headers.Add("X-NewP-Button", buttonId);
        if (legacy is not null) request.Headers.Add("X-NewP-Data-Declaration", legacy);
        request.Content = JsonContent.Create(body, options: new JsonSerializerOptions());
        return await client.SendAsync(request);
    }

    private sealed class DecisionFault { internal bool Throw { get; set; } }
    private sealed class FaultedDecision(IDataScopeObjectDecisionProvider inner, DecisionFault fault) : IDataScopeObjectDecisionProvider
    {
        public Task<bool> IsAllowedAsync(string resourceKey, string objectId, string actionKey, CancellationToken cancellationToken = default) =>
            fault.Throw && actionKey == "edit" ? throw new InvalidOperationException("injected object decision failure")
                : inner.IsAllowedAsync(resourceKey, objectId, actionKey, cancellationToken);
        public Task<bool> IsCandidateAllowedAsync(string resourceKey, IReadOnlyDictionary<string, object?> candidate,
            string actionKey, CancellationToken cancellationToken = default) => inner.IsCandidateAllowedAsync(resourceKey, candidate, actionKey, cancellationToken);
    }
}
