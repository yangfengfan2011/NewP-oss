using System.Net;
using System.Text.Json;
using ICP.Platform.Modules.Authorization.DTOs;
using ICP.Platform.Presentation;
using ICP.Platform.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed class DataScopeTemporalSqlServerTests
{
    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "固定Instant保存时一次规范化，CalendarDate不转换时区，读取不随查看者重新解释")]
    public async Task FixedTemporalEquality_UsesCanonicalSavedValues()
    {
        var presentation = new MutablePresentation();
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(services =>
            services.AddSingleton<IRequestPresentationContextAccessor>(presentation));
        using var admin = await fixture.LoginAdministratorAsync();
        var http = new FeatureHttpTestRequests(fixture);
        var read = http.Organization("persons");
        using var ordinary = (await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "TemporalScope", "日期范围", "temporal-scope", "日期读取者", read)).Client;
        var person = await FeatureHttpTestRequests.CreateAsync(admin, http.Organization("persons", "create", "list.pageActions"), "/api/organization/persons",
            new { PersonNo = "TEMPORAL-SCOPE", ENAME = "日期范围目标", EmploymentStatus = "Active", IsEnabled = true,
                BirthDate = "2000-02-29", EmploymentStartedAt = "2030-01-02T09:02:03" });
        var personId = BusinessAuditSqlServerHttpApiTests.Id(person);
        var entry = fixture.FeatureCatalog.Single(point => point.BehaviorKey == "authorization.dataScope.read");
        var save = fixture.FeatureCatalog.Single(point => point.BehaviorKey == "authorization.dataScope.save");
        var rule = new DataScopeRuleDto("Scope_Temporal", "Scope_Temporal", null, "任职时间", true, "organization.person",
            new("attribute", "EmploymentStartedAt", "equal", JsonSerializer.SerializeToElement("2030-01-02T09:02:03")),
            [new("Subject_Temporal", "all", null, 0, new Dictionary<string, string> { ["read"] = "deny" })], []);
        await SaveAsync(rule, HttpStatusCode.NoContent);
        var stored = await FeatureHttpTestRequests.ReadAsync(admin, entry, "/api/authorization/data-scope/rules");
        Assert.Equal("2030-01-02T01:02:03Z", stored[0].GetProperty("condition").GetProperty("value").GetString());
        presentation.Current = new("en-US", "America/New_York");
        await AssertDeniedAsync();
        await SaveAsync(stored[0].Deserialize<DataScopeRuleDto>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!, HttpStatusCode.NoContent);
        var audit = await FeatureHttpTestRequests.ReadAsync(admin, http.Audit(), "/api/audit/business-changes?TargetId=Scope_Temporal&EventType=dataScope.rule.changed");
        Assert.Single(audit.GetProperty("items").EnumerateArray());
        rule = rule with { Condition = new("attribute", "BirthDate", "equal", JsonSerializer.SerializeToElement("2000-02-29")) };
        await SaveAsync(rule, HttpStatusCode.NoContent);
        await AssertDeniedAsync();
        await SaveAsync(rule with { Condition = rule.Condition with { Value = JsonSerializer.SerializeToElement("2000-02-30") } }, HttpStatusCode.BadRequest);
        stored = await FeatureHttpTestRequests.ReadAsync(admin, entry, "/api/authorization/data-scope/rules");
        Assert.Equal("2000-02-29", stored[0].GetProperty("condition").GetProperty("value").GetString());
        var empty = JsonSerializer.SerializeToElement((object?)null);
        var compound = new DataScopeConditionDto("group", "", "and", empty, Children:
        [
            new("attribute", "EmploymentStartedAt", "greaterThanOrEqual", JsonSerializer.SerializeToElement("2030-01-01T20:02:03")),
            new("attribute", "BirthDate", "lessThanOrEqual", JsonSerializer.SerializeToElement("2000-02-29"))
        ]);
        await SaveAsync(rule with { Condition = compound }, HttpStatusCode.NoContent);
        stored = await FeatureHttpTestRequests.ReadAsync(admin, entry, "/api/authorization/data-scope/rules");
        Assert.Equal("2030-01-02T01:02:03Z", stored[0].GetProperty("condition").GetProperty("children")[0].GetProperty("value").GetString());
        await AssertDeniedAsync();
        await SaveAsync(rule with { Condition = compound with { Children =
            [compound.Children![0], compound.Children[1] with { Value = JsonSerializer.SerializeToElement("2000-02-30") }] } }, HttpStatusCode.BadRequest);
        stored = await FeatureHttpTestRequests.ReadAsync(admin, entry, "/api/authorization/data-scope/rules");
        Assert.Equal("2000-02-29", stored[0].GetProperty("condition").GetProperty("children")[1].GetProperty("value").GetString());

        async Task SaveAsync(DataScopeRuleDto value, HttpStatusCode expected)
        {
            using var response = await FeatureHttpTestRequests.SendAsync(admin, save, HttpMethod.Put, "/api/authorization/data-scope/rules/Scope_Temporal", value);
            Assert.Equal(expected, response.StatusCode);
        }
        async Task AssertDeniedAsync()
        {
            using var response = await FeatureHttpTestRequests.SendAsync(ordinary, read, HttpMethod.Get, $"/api/organization/persons/{personId}");
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            var list = await FeatureHttpTestRequests.ReadAsync(ordinary, read, "/api/organization/persons");
            Assert.Equal(1, list.GetProperty("total").GetInt32());
        }
    }

    private sealed class MutablePresentation : IRequestPresentationContextAccessor
    {
        public RequestPresentationContext? Current { get; set; } = new("zh-CN", "Asia/Shanghai");
    }
}
