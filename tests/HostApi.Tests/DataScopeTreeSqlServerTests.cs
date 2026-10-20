using System.Net;
using System.Security.Claims;
using System.Text.Json;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Modules.Authorization.DTOs;
using ICP.Platform.Organization.Contracts;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Testing;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed class DataScopeTreeSqlServerTests
{
    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "无读取贡献祖先保留后代，读取祖先条件限定后代，可信当前值与嵌套条件组合")]
    public async Task PersonRead_AncestorProjectionAndNestedContextCondition()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync();
        var http = new FeatureHttpTestRequests(fixture);
        using var admin = await fixture.LoginAdministratorAsync();
        var people = http.Organization("persons");
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "TreeContext", "树上下文", "scope-tree-context", "树上下文读取者", people);
        using var ordinary = actor.Client;
        var organization = (await fixture.Services.GetRequiredService<IOrganizationQuery>().ReadAsync()).FindByAccount(actor.AccountId)!;
        var personId = (string)organization.Person["EID"]!;
        var currentPerson = new DataScopeConditionDto("attribute", "EID", "equal",
            JsonSerializer.SerializeToElement((object?)null), "current.person");
        var nested = new DataScopeConditionDto("group", "", "and", JsonSerializer.SerializeToElement((object?)null), Children:
            [new("group", "", "or", JsonSerializer.SerializeToElement((object?)null), Children:
                [currentPerson, Condition("PersonNo", "equal", "no-such-person")]),
             new("attribute", "ENAME", "notNull", JsonSerializer.SerializeToElement((object?)null))]);
        var grandchild = new DataScopeRuleDto("Tree_Context_Allow", "Tree_Context", "Tree_Context_Deny", "孙同意", true,
            "organization.person", currentPerson, [Subject("Tree_Context_Allow_Subject", "allow", 0)], []);
        var child = new DataScopeRuleDto("Tree_Context_Deny", "Tree_Context", "Tree_Context", "子拒绝", true,
            "organization.person", nested, [Subject("Tree_Context_Deny_Subject", "deny", 0)], [grandchild]);
        var root = new DataScopeRuleDto("Tree_Context", "Tree_Context", null, "无读取祖先", true, "organization.person",
            new("business", "", "", JsonSerializer.SerializeToElement((object?)null), Key: "organization.person.departmentEnabled"),
            [Subject("Tree_Context_Root_Subject", "deny", 100)], [child]);
        var save = fixture.FeatureCatalog.Single(point => point.BehaviorKey == "authorization.dataScope.save");
        await SaveAsync(root);
        await StatusAsync(HttpStatusCode.OK);
        await SaveAsync(root with { Children = [child with { Children = [grandchild with { Enabled = false }] }] });
        await StatusAsync(HttpStatusCode.NotFound);
        await SaveAsync(root with { Enabled = false });
        await StatusAsync(HttpStatusCode.OK);
        await SaveAsync(root with
        {
            Condition = Condition("PersonNo", "equal", "no-such-person"),
            Enabled = true,
            Subjects = [],
            Children = [child with { Children = [] }]
        });
        await StatusAsync(HttpStatusCode.OK);
        await SaveAsync(root with
        {
            Condition = new("attribute", "EID", "notNull", JsonSerializer.SerializeToElement((object?)null)),
            Subjects = [],
            Children = [child with { Children = [] }]
        });
        await StatusAsync(HttpStatusCode.NotFound);

        async Task SaveAsync(DataScopeRuleDto value)
        {
            using var response = await new FeatureHttpTestRequests(fixture).SendButtonAsync(admin, save, HttpMethod.Put,
                "/api/authorization/data-scope/rules/Tree_Context", value);
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }
        async Task StatusAsync(HttpStatusCode expected)
        {
            using var response = await FeatureHttpTestRequests.SendAsync(ordinary, people, HttpMethod.Get,
                $"/api/organization/persons/{personId}");
            Assert.Equal(expected, response.StatusCode);
        }
    }

    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "父子覆盖、兄弟冲突与整树保存经正式人员读取生效")]
    public async Task PersonRead_TreeBranchesOverrideOnlyTheirAncestors()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync();
        var http = new FeatureHttpTestRequests(fixture);
        using var admin = await fixture.LoginAdministratorAsync();
        var people = http.Organization("persons");
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "Tree", "树裁决", "scope-tree", "树裁决者", people);
        using var ordinary = actor.Client;
        var create = http.Organization("persons", "create", "list.pageActions");
        var allowedId = BusinessAuditSqlServerHttpApiTests.Id(await new FeatureHttpTestRequests(fixture).CreateButtonAsync(admin, create,
            "/api/organization/persons", new { PersonNo = "TREE-ALLOW", ENAME = "Tree Allowed", EmploymentStatus = "Active", IsEnabled = true }));
        var deniedId = BusinessAuditSqlServerHttpApiTests.Id(await new FeatureHttpTestRequests(fixture).CreateButtonAsync(admin, create,
            "/api/organization/persons", new { PersonNo = "TREE-DENY", ENAME = "Tree Denied", EmploymentStatus = "Active", IsEnabled = true }));
        var save = fixture.FeatureCatalog.Single(point => point.BehaviorKey == "authorization.dataScope.save");
        var read = fixture.FeatureCatalog.Single(point => point.BehaviorKey == "authorization.dataScope.read");
        var root = new DataScopeRuleDto("Scope_Tree", "Scope_Tree", null, "父拒绝", true, "organization.person",
            Condition("ENAME", "startsWith", "Tree"), [Subject("Tree_Root", "deny", 100)],
            [new("Scope_Tree_Allow", "Scope_Tree", "Scope_Tree", "子同意", true, "organization.person",
                Condition("PersonNo", "equal", "TREE-ALLOW"), [Subject("Tree_Allow", "allow", 0)], []),
             new("Scope_Tree_Unset", "Scope_Tree", "Scope_Tree", "子不选", true, "organization.person",
                Condition("PersonNo", "equal", "TREE-ALLOW"), [Subject("Tree_Unset", "unset", 200)], [])]);
        await SaveAsync(root);
        await StatusAsync(allowedId, HttpStatusCode.OK);
        await StatusAsync(deniedId, HttpStatusCode.NotFound);

        var listed = await FeatureHttpTestRequests.ReadAsync(ordinary, people, "/api/organization/persons?page=1&pageSize=100");
        Assert.DoesNotContain(listed.GetProperty("items").EnumerateArray(), item => item.GetProperty("EID").GetString() == deniedId);
        Assert.Contains(listed.GetProperty("items").EnumerateArray(), item => item.GetProperty("EID").GetString() == allowedId);
        var filtered = await FeatureHttpTestRequests.ReadAsync(ordinary, people,
            "/api/organization/persons?keyword=Tree&page=1&pageSize=1&sortBy=PersonNo&sortDirection=asc");
        Assert.Equal(1, filtered.GetProperty("total").GetInt32());
        Assert.Equal(allowedId, filtered.GetProperty("items")[0].GetProperty("EID").GetString());
        using (var scope = fixture.Services.CreateScope())
        {
            var services = scope.ServiceProvider;
            var context = services.GetRequiredService<IHttpContextAccessor>();
            context.HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, actor.AccountId)], "test"))
            };
            try
            {
                var predicate = (await services.GetRequiredService<IDataScopeReadPlanProvider>()
                    .GetReadPlanAsync("organization.person")).Predicate!;
                var db = services.GetRequiredService<EfPlatformDbContext>();
                var sameBusinessFilter = db.Set<Dictionary<string, object?>>(db.ResolveModelKey("Person"))
                    .AsNoTracking().Where(row => EF.Property<string>(row, "ENAME").StartsWith("Tree")).Where(predicate);
                Assert.Equal(filtered.GetProperty("total").GetInt32(), await sameBusinessFilter.CountAsync());
                var aggregate = await sameBusinessFilter.GroupBy(row => EF.Property<bool>(row, "IsEnabled"))
                    .Select(group => new { group.Key, Count = group.Count() }).ToArrayAsync();
                Assert.Single(aggregate);
                Assert.True(aggregate[0].Key);
                Assert.Equal(1, aggregate[0].Count);
            }
            finally { context.HttpContext = null; }
        }
        var stored = await FeatureHttpTestRequests.ReadAsync(admin, read, "/api/authorization/data-scope/rules");
        Assert.Equal(2, stored[0].GetProperty("children").GetArrayLength());

        var conflicting = root with
        {
            Children = [..root.Children,
            new DataScopeRuleDto("Scope_Tree_Deny", "Scope_Tree", "Scope_Tree", "兄弟拒绝", true, "organization.person",
                Condition("PersonNo", "equal", "TREE-ALLOW"), [Subject("Tree_Deny", "deny", -100)], [])]
        };
        await SaveAsync(conflicting);
        await StatusAsync(allowedId, HttpStatusCode.NotFound);
        var manage = fixture.FeatureCatalog.First(point => point.BehaviorKey == "organization.person.authorize");
        var objectEndpoint = $"/api/authorization/data-scope/objects/organization.person/{allowedId}";
        using (var response = await FeatureHttpTestRequests.SendAsync(admin, manage, HttpMethod.Put, objectEndpoint,
            new DataScopeObjectDto("organization.person", allowedId,
                [new("Tree_Object", "all", null, 0, new Dictionary<string, string> { ["read"] = "allow" })])))
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await StatusAsync(allowedId, HttpStatusCode.OK);
        using (var response = await FeatureHttpTestRequests.SendAsync(admin, manage, HttpMethod.Put, objectEndpoint,
            new DataScopeObjectDto("organization.person", allowedId, [])))
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await StatusAsync(allowedId, HttpStatusCode.NotFound);
        await SaveAsync(conflicting with { Children = [root.Children[0], root.Children[1]] });
        await StatusAsync(allowedId, HttpStatusCode.OK);
        using (var response = await FeatureHttpTestRequests.SendAsync(admin, manage, HttpMethod.Put, objectEndpoint,
            new DataScopeObjectDto("organization.person", allowedId,
                [new("Tree_Object", "all", null, 0, new Dictionary<string, string> { ["read"] = "deny" })])))
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await StatusAsync(allowedId, HttpStatusCode.NotFound);
        using (var response = await FeatureHttpTestRequests.SendAsync(admin, manage, HttpMethod.Put, objectEndpoint,
            new DataScopeObjectDto("organization.person", allowedId, [])))
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await StatusAsync(allowedId, HttpStatusCode.OK);

        var independent = root with
        {
            Id = "Scope_Independent",
            RootId = "Scope_Independent",
            ParentId = null,
            Name = "独立拒绝",
            Condition = Condition("PersonNo", "equal", "TREE-ALLOW"),
            Subjects = [Subject("Tree_Independent", "deny", -100)],
            Children = []
        };
        using (var response = await new FeatureHttpTestRequests(fixture).SendButtonAsync(admin, save, HttpMethod.Put,
            "/api/authorization/data-scope/rules/Scope_Independent", independent))
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await StatusAsync(allowedId, HttpStatusCode.NotFound);
        using (var response = await new FeatureHttpTestRequests(fixture).SendButtonAsync(admin, save, HttpMethod.Delete,
            "/api/authorization/data-scope/rules/Scope_Independent"))
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await StatusAsync(allowedId, HttpStatusCode.OK);

        using (var invalid = await new FeatureHttpTestRequests(fixture).SendButtonAsync(admin, save, HttpMethod.Put,
            "/api/authorization/data-scope/rules/Scope_Tree", root with { Children = [root with { ParentId = root.Id }] }))
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        using (var invalid = await new FeatureHttpTestRequests(fixture).SendButtonAsync(admin, save, HttpMethod.Put,
            "/api/authorization/data-scope/rules/Scope_Tree", root with
            {
                Children = [root.Children[0],
                root.Children[0] with { Id = "scope_tree_allow" }]
            }))
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        await StatusAsync(deniedId, HttpStatusCode.NotFound);

        await SaveAsync(root with { Enabled = false });
        await StatusAsync(deniedId, HttpStatusCode.OK);
        foreach (var mode in new[] { "register", "save", "commit" })
        {
            fixture.Fault.Mode = mode;
            try
            {
                using var failed = await new FeatureHttpTestRequests(fixture).SendButtonAsync(admin, save, HttpMethod.Delete,
                    "/api/authorization/data-scope/rules/Scope_Tree");
                Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
            }
            finally { fixture.Fault.Mode = ""; }
            var kept = await FeatureHttpTestRequests.ReadAsync(admin, read, "/api/authorization/data-scope/rules");
            Assert.Contains(kept.EnumerateArray(), item => item.GetProperty("id").GetString() == root.Id
                && item.GetProperty("children").GetArrayLength() == 2);
        }
        using (var response = await new FeatureHttpTestRequests(fixture).SendButtonAsync(admin, save, HttpMethod.Delete,
            "/api/authorization/data-scope/rules/Scope_Tree"))
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var afterDelete = await FeatureHttpTestRequests.ReadAsync(admin, read, "/api/authorization/data-scope/rules");
        Assert.DoesNotContain(afterDelete.EnumerateArray(), item => item.GetProperty("id").GetString() == root.Id);
        var audit = await FeatureHttpTestRequests.ReadAsync(admin, http.Audit(),
            "/api/audit/business-changes?TargetId=Scope_Tree&EventType=dataScope.rule.changed");
        Assert.Equal(5, audit.GetProperty("items").GetArrayLength());
        var changes = audit.GetProperty("items").EnumerateArray().SelectMany(item => item.GetProperty("Changes").EnumerateArray()).ToArray();
        Assert.Contains(changes, change => change.GetProperty("before").ValueKind == JsonValueKind.Object
            && change.GetProperty("before").GetProperty("value").GetString()?.Contains("Scope_Tree_Deny") == true
            && change.GetProperty("after").GetProperty("value").GetString()?.Contains("Scope_Tree_Deny") == false);
        Assert.Contains(changes, change => change.GetProperty("before").ValueKind == JsonValueKind.Object
            && change.GetProperty("before").GetProperty("value").GetString()?.Contains("Scope_Tree_Allow") == true
            && change.GetProperty("after").GetProperty("value").ValueKind == JsonValueKind.Null);

        async Task SaveAsync(DataScopeRuleDto rule)
        {
            using var response = await new FeatureHttpTestRequests(fixture).SendButtonAsync(admin, save, HttpMethod.Put,
                "/api/authorization/data-scope/rules/Scope_Tree", rule);
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }
        async Task StatusAsync(string id, HttpStatusCode expected)
        {
            using var response = await FeatureHttpTestRequests.SendAsync(ordinary, people, HttpMethod.Get,
                $"/api/organization/persons/{id}");
            Assert.Equal(expected, response.StatusCode);
        }
    }

    private static DataScopeConditionDto Condition(string field, string operation, string value) =>
        new("attribute", field, operation, JsonSerializer.SerializeToElement(value));

    private static DataScopeSubjectDto Subject(string id, string opinion, long priority) =>
        new(id, "all", null, priority, new Dictionary<string, string> { ["read"] = opinion });
}
