using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ICP.Platform.Modules.Authorization.DTOs;
using ICP.Platform.Testing;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed class DataScopeExplanationSqlServerTests
{
    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "最终对象权限解释与真实读取、写入和管理资格一致")]
    public async Task Explanation_UsesRealDecisionsAndPreservesOperatorIdentity()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync();
        using var admin = await fixture.LoginAdministratorAsync();
        var http = new FeatureHttpTestRequests(fixture);
        var manage = http.Organization("persons", "authorize", "detail.pageActions");
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin,
            "Explain", "权限分析", "explain-user", "分析用户", http.Organization("persons"), http.Organization("persons", "edit", "detail.pageActions"));
        using var user = actor.Client;
        var record = await http.CreateButtonAsync(admin, http.Organization("persons", "create", "list.pageActions"), "/api/organization/persons",
            new { PersonNo = "EXPLAIN", ENAME = "权限分析记录", EmploymentStatus = "Active", IsEnabled = true });
        var id = BusinessAuditSqlServerHttpApiTests.Id(record);
        var endpoint = $"/api/authorization/data-scope/objects/organization.person/{id}";
        var explanation = endpoint + "/explanation?accountId=" + actor.AccountId;
        var ruleSave = http.Workbench("authorization.dataScope.save");
        var root = new DataScopeRuleDto("ExplainRoot", "ExplainRoot", null, "根规则", true, "organization.person",
            new("attribute", "EID", "equal", JsonSerializer.SerializeToElement(id)),
            [new("RootSubject", "all", null, 0, new Dictionary<string, string> { ["read"] = "deny", ["edit"] = "deny" })],
            [new("ExplainChild", "ExplainRoot", "ExplainRoot", "子规则", true, "organization.person",
                new("attribute", "ENAME", "equal", JsonSerializer.SerializeToElement("权限分析记录")),
                [new("ChildSubject", "user", actor.AccountId, 1, new Dictionary<string, string> { ["read"] = "allow" })], [])]);
        using (var response = await new FeatureHttpTestRequests(fixture).SendButtonAsync(admin, ruleSave, HttpMethod.Put,
            "/api/authorization/data-scope/rules/ExplainRoot", root))
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var result = await Explain();
        Assert.Equal(actor.AccountId, result.AccountId);
        var read = Assert.Single(result.Actions, item => item.Key == "read");
        Assert.True(read.Allowed);
        Assert.Contains(read.Sources, item => item.Name == "根规则 / 子规则" && item.Status == "effective");
        Assert.Contains(read.Sources, item => item.Id == "ExplainRoot" && item.Status == "descendantOverride");
        Assert.False(Assert.Single(result.Actions, item => item.Key == "edit").Allowed);
        Assert.DoesNotContain(result.Actions, item => item.Key == "create");
        using (var response = await FeatureHttpTestRequests.SendAsync(user, http.Organization("persons"), HttpMethod.Get, $"/api/organization/persons/{id}"))
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using (var response = await http.SendButtonAsync(user, http.Organization("persons", "edit", "detail.pageActions"), HttpMethod.Put,
            $"/api/organization/persons/{id}", new { ENAME = "不应保存" }))
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using (var response = await FeatureHttpTestRequests.SendAsync(user, http.Organization("persons"), HttpMethod.Get, explanation))
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var grant = new DataScopeObjectDto("organization.person", id,
            [new("ExplainObject", "user", actor.AccountId, 1,
                new Dictionary<string, string> { ["read"] = "deny", ["edit"] = "allow" })]);
        using (var response = await FeatureHttpTestRequests.SendAsync(admin, manage, HttpMethod.Put, endpoint, grant))
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        result = await Explain();
        read = Assert.Single(result.Actions, item => item.Key == "read");
        Assert.False(read.Allowed);
        Assert.Equal("object", read.Reason);
        Assert.Contains(read.Sources, item => item.Kind == "rule" && item.Status == "objectOverride");
        Assert.Equal("readRequired", Assert.Single(result.Actions, item => item.Key == "edit").Reason);
        using (var response = await FeatureHttpTestRequests.SendAsync(user, http.Organization("persons"), HttpMethod.Get, $"/api/organization/persons/{id}"))
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using (var response = await FeatureHttpTestRequests.SendAsync(admin, manage, HttpMethod.Get, endpoint + "/explanation"))
        {
            var own = await response.Content.ReadFromJsonAsync<DataScopeExplanationDto>();
            Assert.Equal(fixture.ActorId, own!.AccountId);
            Assert.All(own.Actions, item => { Assert.True(item.Allowed); Assert.Equal("administrator", item.Reason); });
        }
        using (var response = await FeatureHttpTestRequests.SendAsync(admin, manage, HttpMethod.Get, endpoint + "/explanation?accountId=missing"))
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using (var response = await FeatureHttpTestRequests.SendAsync(admin, manage, HttpMethod.Get,
            endpoint + "/explanation?accountId=" + new string('a', 129)))
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.False(problem.TryGetProperty("code", out _));
        }

        async Task<DataScopeExplanationDto> Explain()
        {
            using var response = await FeatureHttpTestRequests.SendAsync(admin, manage, HttpMethod.Get, explanation);
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            return (await response.Content.ReadFromJsonAsync<DataScopeExplanationDto>())!;
        }
    }
}
