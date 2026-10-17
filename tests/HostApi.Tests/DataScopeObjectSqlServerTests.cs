using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ICP.Platform.Modules.Authorization.DTOs;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed class DataScopeObjectSqlServerTests
{
    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "授权与读取动作开关独立，配置在正式重装配后保留")]
    public async Task ObjectActions_RestartKeepsConfigurationAndIndependentReadDependency()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync();
        var actions = fixture.Services.GetRequiredService<CanonicalModulePlan>().DataActionCatalog;
        Assert.Equal(new[] { "read", "create", "edit", "delete", "authorize" }, actions.Select(action => action.Key));
        Assert.All(actions.Where(action => action.Key is "authorize" or "delete" or "edit"), action => Assert.True(action.RequiresRead));
        Assert.All(actions.Where(action => action.Key is "create" or "read"), action => Assert.False(action.RequiresRead));
        var http = new FeatureHttpTestRequests(fixture);
        using var admin = await fixture.LoginAdministratorAsync();
        var read = http.Organization("persons");
        var manage = fixture.FeatureCatalog.First(point => point.BehaviorKey == "organization.person.authorize");
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "ObjectSwitch", "对象开关",
            "object-switch", "对象开关用户", read, manage);
        actor.Client.Dispose();
        var created = await FeatureHttpTestRequests.CreateAsync(admin, http.Organization("persons", "create", "list.pageActions"),
            "/api/organization/persons", new { PersonNo = "OBJECT-SWITCH", ENAME = "Object Switch", EmploymentStatus = "Active", IsEnabled = true });
        var id = BusinessAuditSqlServerHttpApiTests.Id(created);
        var endpoint = $"/api/authorization/data-scope/objects/organization.person/{id}";
        var alias = $"/api/authorization/data-scope/objects/organization.person/{id.ToLowerInvariant()}";
        using (var response = await FeatureHttpTestRequests.SendAsync(admin, manage, HttpMethod.Get, alias))
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using (var response = await FeatureHttpTestRequests.SendAsync(admin, manage, HttpMethod.Put, alias,
            new DataScopeObjectDto("organization.person", id.ToLowerInvariant(), [])))
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var config = new DataScopeObjectDto("organization.person", id,
            [new("ObjectSwitchSubject", "user", actor.AccountId, 1,
                new Dictionary<string, string> { ["read"] = "allow", ["authorize"] = "deny" })]);
        using (var response = await FeatureHttpTestRequests.SendAsync(admin, manage, HttpMethod.Put, endpoint, config))
            Assert.True(response.StatusCode == HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
        using (var ordinary = await LoginCurrentAsync("object-switch"))
        using (var response = await FeatureHttpTestRequests.SendAsync(ordinary, manage, HttpMethod.Get, endpoint))
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        await fixture.RestartWithActionAsync("authorize", false);
        using (var ordinary = await LoginCurrentAsync("object-switch"))
        using (var response = await FeatureHttpTestRequests.SendAsync(ordinary, manage, HttpMethod.Get, endpoint))
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using (var administrator = await LoginCurrentAsync("admin"))
        {
            using var response = await FeatureHttpTestRequests.SendAsync(administrator, manage, HttpMethod.Put, endpoint,
                config with { Subjects = [config.Subjects[0] with
                { Actions = new Dictionary<string, string> { ["read"] = "deny", ["authorize"] = "allow" } }] });
            Assert.True(response.StatusCode == HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
        }
        await fixture.RestartWithActionAsync("read", false);
        using (var ordinary = await LoginCurrentAsync("object-switch"))
        {
            using var readResponse = await FeatureHttpTestRequests.SendAsync(ordinary, read, HttpMethod.Get, $"/api/organization/persons/{id}");
            Assert.Equal(HttpStatusCode.OK, readResponse.StatusCode);
            using var manageResponse = await FeatureHttpTestRequests.SendAsync(ordinary, manage, HttpMethod.Get, endpoint);
            Assert.Equal(HttpStatusCode.OK, manageResponse.StatusCode);
        }
        await fixture.RestartWithActionAsync("read", true);
        using (var ordinary = await LoginCurrentAsync("object-switch"))
        {
            using var readResponse = await FeatureHttpTestRequests.SendAsync(ordinary, read, HttpMethod.Get, $"/api/organization/persons/{id}");
            Assert.Equal(HttpStatusCode.NotFound, readResponse.StatusCode);
            using var manageResponse = await FeatureHttpTestRequests.SendAsync(ordinary, manage, HttpMethod.Get, endpoint);
            Assert.Equal(HttpStatusCode.NotFound, manageResponse.StatusCode);
        }

        async Task<HttpClient> LoginCurrentAsync(string code)
        {
            var client = fixture.CreateClient();
            await BusinessAuditHostFixture.RefreshAntiforgeryAsync(client);
            using var response = await client.PostAsJsonAsync("/api/auth/login", new { ECODE = code, Password = "Audit-test-secure-938!" });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            await BusinessAuditHostFixture.RefreshAntiforgeryAsync(client);
            return client;
        }
    }

    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "人员对象授权双向覆盖、整份保存、自身失权与审计")]
    public async Task PersonObject_OverridesRuleAndSavesOwnRevocation()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync();
        var http = new FeatureHttpTestRequests(fixture);
        using var admin = await fixture.LoginAdministratorAsync();
        var read = http.Organization("persons");
        var create = http.Organization("persons", "create", "list.pageActions");
        var manage = fixture.FeatureCatalog.First(point => point.BehaviorKey == "organization.person.authorize");
        var ruleSave = fixture.FeatureCatalog.Single(point => point.BehaviorKey == "authorization.dataScope.save");
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "ObjectScope", "对象范围",
            "object-scope", "对象范围用户", read, manage);
        using var ordinary = actor.Client;
        var readerActor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "ObjectReader", "对象读者",
            "object-reader", "对象只读用户", read);
        using var readerOnly = readerActor.Client;
        var created = await FeatureHttpTestRequests.CreateAsync(admin, create, "/api/organization/persons",
            new { PersonNo = "OBJECT-SCOPE", ENAME = "Object Scope", EmploymentStatus = "Active", IsEnabled = true });
        var id = BusinessAuditSqlServerHttpApiTests.Id(created);
        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
            db.Set<Dictionary<string, object?>>(db.ResolveModelKey("Role")).Add(new()
            {
                ["EID"] = id, ["ECODE"] = "SAME-OBJECT-ID", ["ENAME"] = "相同对象 ID 角色",
                ["IsEnabled"] = true, ["IsBuiltIn"] = false,
                ["CreatedBy"] = fixture.ActorId, ["UpdatedBy"] = fixture.ActorId,
                ["CreatedAt"] = DateTime.UtcNow, ["UpdatedAt"] = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }
        var endpoint = $"/api/authorization/data-scope/objects/organization.person/{id}";
        using (var response = await FeatureHttpTestRequests.SendAsync(readerOnly, read, HttpMethod.Get, endpoint))
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using (var response = await FeatureHttpTestRequests.SendAsync(readerOnly, read, HttpMethod.Get, endpoint + "/eligibility"))
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using (var response = await FeatureHttpTestRequests.SendAsync(readerOnly, read, HttpMethod.Get, endpoint + "/subjects/user"))
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await AssertManagementDeniedAsync(readerOnly, HttpStatusCode.Forbidden);
        var rule = new DataScopeRuleDto("ObjectScopeRule", "ObjectScopeRule", null, "拒绝读取", true, "organization.person",
            new("attribute", "EID", "equal", JsonSerializer.SerializeToElement(id)),
            [new("ObjectScopeRuleSubject", "all", null, 0, new Dictionary<string, string> { ["read"] = "deny" })], []);
        using (var response = await FeatureHttpTestRequests.SendAsync(admin, ruleSave, HttpMethod.Put,
            "/api/authorization/data-scope/rules/ObjectScopeRule", rule))
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        using (var response = await FeatureHttpTestRequests.SendAsync(ordinary, read, HttpMethod.Get, $"/api/organization/persons/{id}"))
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var allow = new DataScopeObjectDto("organization.person", id,
            [new("ObjectSubject", "user", actor.AccountId, 4,
                new Dictionary<string, string> { ["read"] = "allow", ["authorize"] = "allow" }),
             new("TransferredAuthorize", "user", readerActor.AccountId, 1,
                new Dictionary<string, string> { ["read"] = "allow", ["authorize"] = "allow" })]);
        using (var response = await FeatureHttpTestRequests.SendAsync(admin, manage, HttpMethod.Put, endpoint, allow))
            Assert.True(response.StatusCode == HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
        using (var response = await FeatureHttpTestRequests.SendAsync(readerOnly, manage, HttpMethod.Get, endpoint))
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using (var response = await FeatureHttpTestRequests.SendAsync(ordinary, read, HttpMethod.Get, $"/api/organization/persons/{id}"))
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using (var response = await FeatureHttpTestRequests.SendAsync(admin, ruleSave, HttpMethod.Put,
            "/api/authorization/data-scope/rules/ObjectScopeRule", rule with
            { Subjects = [rule.Subjects[0] with { Actions = new Dictionary<string, string> { ["read"] = "allow" } }] }))
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        using (var response = await FeatureHttpTestRequests.SendAsync(admin, manage, HttpMethod.Put, endpoint,
            allow with { Subjects = [allow.Subjects[0] with
            { Actions = new Dictionary<string, string> { ["read"] = "deny", ["authorize"] = "allow" } }] }))
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        using (var response = await FeatureHttpTestRequests.SendAsync(ordinary, read, HttpMethod.Get, $"/api/organization/persons/{id}"))
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using (var response = await FeatureHttpTestRequests.SendAsync(admin, manage, HttpMethod.Put, endpoint,
            allow with { Subjects = [allow.Subjects[0] with
            { Actions = new Dictionary<string, string> { ["read"] = "unset", ["authorize"] = "allow" } }] }))
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        using (var response = await FeatureHttpTestRequests.SendAsync(ordinary, read, HttpMethod.Get, $"/api/organization/persons/{id}"))
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using (var response = await FeatureHttpTestRequests.SendAsync(admin, ruleSave, HttpMethod.Put,
            "/api/authorization/data-scope/rules/ObjectScopeRule", rule))
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        using (var response = await FeatureHttpTestRequests.SendAsync(ordinary, read, HttpMethod.Get, $"/api/organization/persons/{id}"))
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using (var response = await FeatureHttpTestRequests.SendAsync(admin, manage, HttpMethod.Put, endpoint, allow))
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        // 两个不同且都匹配的规范选择器竞争；不能用技术ID伪造重复all项。
        var samePriority = allow with { Subjects = [
            new("ObjectPriorityAllow", "all", null, 2, new Dictionary<string, string> { ["read"] = "allow" }),
            new("ObjectPriorityDeny", "user", actor.AccountId, 2, new Dictionary<string, string> { ["read"] = "deny" })] };
        using (var response = await FeatureHttpTestRequests.SendAsync(admin, manage, HttpMethod.Put, endpoint, samePriority))
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        using (var response = await FeatureHttpTestRequests.SendAsync(ordinary, read, HttpMethod.Get, $"/api/organization/persons/{id}"))
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using (var response = await FeatureHttpTestRequests.SendAsync(admin, manage, HttpMethod.Put, endpoint,
            samePriority with { Subjects = [samePriority.Subjects[0] with { Priority = 3 }, samePriority.Subjects[1]] }))
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        using (var response = await FeatureHttpTestRequests.SendAsync(ordinary, read, HttpMethod.Get, $"/api/organization/persons/{id}"))
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using (var response = await FeatureHttpTestRequests.SendAsync(admin, manage, HttpMethod.Put, endpoint, allow))
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        using (var response = await FeatureHttpTestRequests.SendAsync(ordinary,
            http.Organization("persons", "delete", "list.rowActions"), HttpMethod.Delete, $"/api/organization/persons/{id}"))
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var roleManage = fixture.FeatureCatalog.First(point => point.BehaviorKey == "authorization.role.authorize");
        var roleEndpoint = $"/api/authorization/data-scope/objects/authorization.role/{id}";
        using (var response = await FeatureHttpTestRequests.SendAsync(admin, roleManage, HttpMethod.Put, roleEndpoint,
            new DataScopeObjectDto("authorization.role", id,
                [new("RoleObjectSubject", "all", null, 0, new Dictionary<string, string> { ["read"] = "deny" })])))
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        using (var response = await FeatureHttpTestRequests.SendAsync(admin, roleManage, HttpMethod.Get, roleEndpoint))
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using (var response = await FeatureHttpTestRequests.SendAsync(ordinary, read, HttpMethod.Get, $"/api/organization/persons/{id}"))
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using (var response = await FeatureHttpTestRequests.SendAsync(ordinary, manage, HttpMethod.Get, endpoint))
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using (var response = await FeatureHttpTestRequests.SendAsync(ordinary, manage, HttpMethod.Get, endpoint + "/eligibility"))
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using (var response = await FeatureHttpTestRequests.SendAsync(ordinary, manage, HttpMethod.Get, endpoint + "/catalog"))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var catalog = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Contains(catalog.GetProperty("actions").EnumerateArray(), item => item.GetProperty("key").GetString() == "authorize");
            Assert.Contains(catalog.GetProperty("subjectTypes").EnumerateArray(), item => item.GetProperty("key").GetString() == "user");
            Assert.False(catalog.TryGetProperty("status", out _));
        }
        using (var response = await FeatureHttpTestRequests.SendAsync(ordinary, manage, HttpMethod.Get,
            endpoint + "/subjects/user?currentIds=" + actor.AccountId))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var subjects = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Contains(subjects.GetProperty("currentValues").EnumerateArray(), item => item.GetProperty("id").GetString() == actor.AccountId);
        }
        using (var response = await FeatureHttpTestRequests.SendAsync(ordinary, manage, HttpMethod.Get, endpoint + "/subjects/user?page=0"))
            await AssertInvalidConfigurationAsync(response, "主体目录页码无效。");
        using (var response = await FeatureHttpTestRequests.SendAsync(ordinary, manage, HttpMethod.Put, endpoint,
            allow with { ObjectId = "different-target" }))
            await AssertInvalidConfigurationAsync(response, "对象授权目标与请求不一致。");
        using (var response = await FeatureHttpTestRequests.SendAsync(ordinary, manage, HttpMethod.Put, endpoint,
            allow with { Subjects = [allow.Subjects[0] with
            { Actions = new Dictionary<string, string> { ["read"] = "deny", ["authorize"] = "deny",
                ["create"] = "allow", ["edit"] = "allow", ["delete"] = "allow" } }] }))
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        using (var response = await FeatureHttpTestRequests.SendAsync(ordinary, manage, HttpMethod.Get, endpoint))
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using (var response = await FeatureHttpTestRequests.SendAsync(ordinary, manage, HttpMethod.Get, endpoint + "/eligibility"))
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await AssertManagementDeniedAsync(ordinary, HttpStatusCode.NotFound);
        using (var response = await FeatureHttpTestRequests.SendAsync(ordinary, read, HttpMethod.Get, $"/api/organization/persons/{id}"))
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using (var response = await FeatureHttpTestRequests.SendAsync(admin, manage, HttpMethod.Put, endpoint, allow))
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        using (var response = await FeatureHttpTestRequests.SendAsync(admin, manage, HttpMethod.Get,
            $"/api/authorization/data-scope/objects/authorization.role/{actor.RoleId}"))
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using (var response = await FeatureHttpTestRequests.SendAsync(admin, manage, HttpMethod.Put, endpoint,
            allow with { Subjects = [allow.Subjects[0] with
            { Actions = new Dictionary<string, string> { ["invented"] = "allow" } }] }))
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var deny = allow with { Subjects = [allow.Subjects[0] with
        { Actions = new Dictionary<string, string> { ["read"] = "deny" } }] };
        foreach (var mode in new[] { "register", "save", "commit" })
        {
            fixture.Fault.Mode = mode;
            try
            {
                using var failed = await FeatureHttpTestRequests.SendAsync(admin, manage, HttpMethod.Put, endpoint, deny);
                Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
            }
            finally { fixture.Fault.Mode = ""; }
            using var kept = await FeatureHttpTestRequests.SendAsync(ordinary, read, HttpMethod.Get, $"/api/organization/persons/{id}");
            Assert.Equal(HttpStatusCode.OK, kept.StatusCode);
        }
        using (var response = await FeatureHttpTestRequests.SendAsync(admin, manage, HttpMethod.Put, endpoint, allow))
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var audit = await FeatureHttpTestRequests.ReadAsync(admin, http.Audit(),
            $"/api/audit/business-changes?TargetId=organization.person%2F{id}&EventType=dataScope.object.changed");
        Assert.Equal(9, audit.GetProperty("items").GetArrayLength());
        using (var response = await FeatureHttpTestRequests.SendAsync(admin,
            http.Organization("persons", "delete", "list.rowActions"), HttpMethod.Delete, $"/api/organization/persons/{id}"))
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
            var retained = await db.Set<Dictionary<string, object?>>(db.ResolveModelKey("DataScopeObjectSubject"))
                .CountAsync(row => EF.Property<string>(row, "ResourceKey") == "organization.person"
                    && EF.Property<string>(row, "ObjectId") == id);
            Assert.Equal(2, retained);
        }
        using (var response = await FeatureHttpTestRequests.SendAsync(admin, manage, HttpMethod.Get, endpoint))
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        async Task AssertManagementDeniedAsync(HttpClient client, HttpStatusCode expected)
        {
            foreach (var suffix in new[] { "", "/catalog", "/subjects/user?page=0", "/explanation?accountId=" + new string('a', 129) })
            {
                using var response = await FeatureHttpTestRequests.SendAsync(client, manage, HttpMethod.Get, endpoint + suffix);
                Assert.Equal(expected, response.StatusCode);
            }
            using var save = await FeatureHttpTestRequests.SendAsync(client, manage, HttpMethod.Put, endpoint,
                new DataScopeObjectDto("organization.person", "different-target", []));
            Assert.Equal(expected, save.StatusCode);
        }

        static async Task AssertInvalidConfigurationAsync(HttpResponseMessage response, string title)
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(400, problem.GetProperty("status").GetInt32());
            Assert.Equal(title, problem.GetProperty("title").GetString());
            Assert.Equal("DataScope.InvalidConfiguration", problem.GetProperty("code").GetString());
        }
    }
}
