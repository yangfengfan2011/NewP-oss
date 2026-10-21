using System.Net;
using System.Text.Json;
using ICP.Platform.Crud.Models;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Modules.Authorization.DTOs;
using ICP.Platform.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed class StandardReadExecutionSqlServerTests
{
    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "原MVC标准读取实际消费唯一list/read绑定并保留Identity管理员资格")]
    public async Task MvcReads_RequireTheirExactSourceAndKeepIdentityAdminOnly()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync();
        using var admin = await fixture.LoginAdministratorAsync();
        var http = new FeatureHttpTestRequests(fixture);
        var roleEntry = fixture.FeatureCatalog.Single(point => point.BehaviorKey == "authorization.role.read");
        var identityEntry = http.Identity();
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "MvcRead309", "MVC正式读取", "mvc-read309", "MVC普通主体", roleEntry, identityEntry);
        using var ordinary = actor.Client;
        var list = OwnedResourceReadHttpRequests.Declaration(fixture, roleEntry, "authorization.role", "list");
        var read = OwnedResourceReadHttpRequests.Declaration(fixture, roleEntry, "authorization.role", "read");
        using (var missing = await FeatureHttpTestRequests.SendAsync(ordinary, roleEntry, HttpMethod.Get, "/api/authorization/roles/query"))
            Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        using (var valid = await FeatureHttpTestRequests.SendAsync(ordinary, roleEntry, HttpMethod.Get, "/api/authorization/roles/query", dataAccess: list))
            Assert.Equal(HttpStatusCode.OK, valid.StatusCode);
        using (var valid = await FeatureHttpTestRequests.SendAsync(ordinary, roleEntry, HttpMethod.Get, "/api/authorization/roles/" + actor.RoleId, dataAccess: read))
            Assert.Equal(HttpStatusCode.OK, valid.StatusCode);
        using (var swapped = await FeatureHttpTestRequests.SendAsync(ordinary, roleEntry, HttpMethod.Get, "/api/authorization/roles/" + actor.RoleId, dataAccess: list))
            Assert.Equal(HttpStatusCode.BadRequest, swapped.StatusCode);
        using (var swapped = await FeatureHttpTestRequests.SendAsync(ordinary, roleEntry, HttpMethod.Get, "/api/authorization/roles/query", dataAccess: read))
            Assert.Equal(HttpStatusCode.BadRequest, swapped.StatusCode);
        var accountList = OwnedResourceReadHttpRequests.Declaration(fixture, identityEntry, "identity.account", "list");
        var accountRead = OwnedResourceReadHttpRequests.Declaration(fixture, identityEntry, "identity.account", "read");
        using (var valid = await FeatureHttpTestRequests.SendAsync(admin, identityEntry, HttpMethod.Get, "/api/identity/accounts", dataAccess: accountList))
            Assert.Equal(HttpStatusCode.OK, valid.StatusCode);
        using (var valid = await FeatureHttpTestRequests.SendAsync(admin, identityEntry, HttpMethod.Get, "/api/identity/accounts/" + fixture.ActorId + "/snapshot", dataAccess: accountRead))
            Assert.Equal(HttpStatusCode.OK, valid.StatusCode);
        using (var denied = await FeatureHttpTestRequests.SendAsync(ordinary, identityEntry, HttpMethod.Get, "/api/identity/accounts", dataAccess: accountList))
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
    }

    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "标准Lookup保留原功能来源、候选范围及不可读当前值的标识遮蔽")]
    public async Task PositionLookup_RequiresItsSourceBindingAndMasksUnreadableCurrentValues()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync();
        using var admin = await fixture.LoginAdministratorAsync();
        var http = new FeatureHttpTestRequests(fixture); var plan = fixture.Services.GetRequiredService<CanonicalModulePlan>();
        var source = http.Organization("persons");
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "LookupExecution309", "Lookup来源",
            "lookup-execution309", "Lookup普通主体", source, http.Organization("positions"));
        using var ordinary = actor.Client;
        var visible = await CreatePositionAsync("LOOKUP-309-Visible");
        var hidden = await CreatePositionAsync("LOOKUP-309-Hidden");
        var rule = new DataScopeRuleDto("Lookup309Hidden", "Lookup309Hidden", null, "Lookup309Hidden", true, "organization.position",
            new("attribute", "EID", "equal", JsonSerializer.SerializeToElement(hidden)),
            [new("Lookup309Subject", "all", null, 0, new Dictionary<string, string> { ["read"] = "deny" })], []);
        using (var saved = await new FeatureHttpTestRequests(fixture).SendButtonAsync(admin, fixture.FeatureCatalog.Single(point => point.BehaviorKey == "authorization.dataScope.save"),
            HttpMethod.Put, "/api/authorization/data-scope/rules/Lookup309Hidden", rule)) Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);
        var path = "/api/organization/positions/options?keyword=LOOKUP-309&pageSize=100&currentValueIds=" + visible + "," + hidden;
        using (var missing = await FeatureHttpTestRequests.SendAsync(ordinary, source, HttpMethod.Get, path))
            Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        var binding = plan.ActionDataAccessCatalog.Single(item => item.Execution == "organization.position.options" && item.ViewKey == "organization.person.list");
        using (var response = await FeatureHttpTestRequests.SendAsync(ordinary, source, HttpMethod.Get, path, dataAccess: binding))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync());
            Assert.Equal(visible, Assert.Single(body.GetProperty("items").EnumerateArray()).GetProperty("value").GetString());
            var current = body.GetProperty("currentItems").EnumerateArray().Single(item => item.GetProperty("value").GetString() == hidden);
            Assert.Equal("***", current.GetProperty("label").GetString());
            Assert.True(current.GetProperty("disabled").GetBoolean());
        }
        using var wrongSource = await FeatureHttpTestRequests.SendAsync(ordinary, http.Organization("positions"), HttpMethod.Get, path, dataAccess: binding);
        Assert.Equal(HttpStatusCode.Forbidden, wrongSource.StatusCode);

        async Task<string> CreatePositionAsync(string name)
        {
            using var response = await http.SendButtonAsync(admin, http.Organization("positions", "create", "list.pageActions"),
                HttpMethod.Post, "/api/organization/positions", new { ENAME = name });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            return BusinessAuditSqlServerHttpApiTests.Id(JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync()));
        }
    }

    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "实际Example导出必须绑定原导出来源，内部列表读取不能冒领另一个入口")]
    public async Task ExampleExport_RequiresItsDeclaredQueryAndRetainsFinalReadScope()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeExample: true);
        using var admin = await fixture.LoginAdministratorAsync();
        var http = new FeatureHttpTestRequests(fixture); var plan = fixture.Services.GetRequiredService<CanonicalModulePlan>();
        var read = http.Example(); var export = http.Example("export"); var manage = http.Example("authorize");
        using var created = await FeatureHttpTestRequests.SendAsync(admin, http.Example("create"), HttpMethod.Post, "/api/example/records",
            new { ECODE = "QUERY-309", ENAME = "QUERY-309", RecordSource = "Manual" },
            plan.ActionDataAccessCatalog.Single(item => item.BusinessBinding == "example.record.create"));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = BusinessAuditSqlServerHttpApiTests.Id(JsonSerializer.Deserialize<JsonElement>(await created.Content.ReadAsStringAsync()));
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "ExampleQuery309", "导出声明反例",
            "example-query309", "导出普通主体", read, export);
        using var ordinary = actor.Client;
        var request = new StandardCrudExportRequest("all", [new("code", "ECODE", "编码"), new("name", "ENAME", "名称")],
            new(PageIndex: 2, PageSize: 1, Filters: new Dictionary<string, string?> { ["keyword"] = "QUERY-309" }));
        using (var missing = await FeatureHttpTestRequests.SendAsync(ordinary, export, HttpMethod.Post, "/api/example/records/export", request))
            Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        var binding = plan.ActionDataAccessCatalog.Single(item => item.Provider == "standardCrud.export" && item.FeaturePositions.Contains(export.Position));
        var listBinding = plan.ActionDataAccessCatalog.Single(item => item.Execution == "example.record.list");
        var detailBinding = plan.ActionDataAccessCatalog.Single(item => item.Execution == "example.record.read");
        using (var allowed = await FeatureHttpTestRequests.SendAsync(ordinary, export, HttpMethod.Post, "/api/example/records/export", request, binding))
        {
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
            Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", allowed.Content.Headers.ContentType!.MediaType);
        }
        using (var crossed = await FeatureHttpTestRequests.SendAsync(ordinary, export, HttpMethod.Post, "/api/example/records/export", request, listBinding))
            Assert.Equal(HttpStatusCode.BadRequest, crossed.StatusCode);
        using (var wrongPosition = await FeatureHttpTestRequests.SendAsync(ordinary, read, HttpMethod.Post, "/api/example/records/export", request, binding))
            Assert.Equal(HttpStatusCode.Forbidden, wrongPosition.StatusCode);
        using (var exportAsList = await FeatureHttpTestRequests.SendAsync(ordinary, read, HttpMethod.Get, "/api/example/records", dataAccess: binding))
            Assert.Equal(HttpStatusCode.BadRequest, exportAsList.StatusCode);
        var management = plan.ActionDataAccessCatalog.Single(item => item.BusinessBinding == "example.record.authorize" && item.FeaturePositions.Contains(manage.Position));
        using (var revoke = await FeatureHttpTestRequests.SendAsync(admin, manage, HttpMethod.Put,
            "/api/authorization/data-scope/objects/example.record/" + id,
            new DataScopeObjectDto("example.record", id, [new("Query309Deny", "all", null, 0, new Dictionary<string, string> { ["read"] = "deny" })]), management))
            Assert.Equal(HttpStatusCode.NoContent, revoke.StatusCode);
        using (var hidden = await FeatureHttpTestRequests.SendAsync(ordinary, read, HttpMethod.Get, "/api/example/records/" + id, dataAccess: detailBinding))
            Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        using var filtered = await FeatureHttpTestRequests.SendAsync(ordinary, read, HttpMethod.Get, "/api/example/records?keyword=QUERY-309", dataAccess: listBinding);
        Assert.Equal(HttpStatusCode.OK, filtered.StatusCode);
        Assert.Equal(0, JsonSerializer.Deserialize<JsonElement>(await filtered.Content.ReadAsStringAsync()).GetProperty("total").GetInt32());
    }
}
