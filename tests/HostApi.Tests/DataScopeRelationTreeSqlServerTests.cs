using ICP.Platform.Authorization.Contracts;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Crud.Runtime;
using ICP.Platform.Modules.Authorization.DTOs;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Testing;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace ICP.Platform.HostApi.Tests;

public sealed class DataScopeRelationTreeSqlServerTests(ITestOutputHelper output)
{
    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "正式组织树按完整规则与对象读取裁决截断，并保持平面读取独立")]
    public async Task OrganizationTree_UsesFinalReadDecisionAndStopsAtUnreadableParent()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync();
        var http = new FeatureHttpTestRequests(fixture);
        using var admin = await fixture.LoginAdministratorAsync();
        var tree = fixture.FeatureCatalog.Single(point => point.BehaviorKey == "tree:organization.structure");
        var departmentRead = http.Organization("departments");
        var personRead = http.Organization("persons");
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "TreeScope236", "树范围",
            "tree-scope-236", "树范围人员", tree, departmentRead, personRead);
        using var ordinary = actor.Client;

        var company = await CreateAsync("companies", new { ECODE = "TREE-236-C", ENAME = "树公司", SortOrder = "0100" });
        var other = await CreateAsync("companies", new { ECODE = "TREE-236-O", ENAME = "其他公司", SortOrder = "0200" });
        var parent = await CreateAsync("departments", new { CompanyId = company, ECODE = "TREE-236-D",
            ENAME = "树父部门", SortOrder = "0100" });
        var child = await CreateAsync("departments", new { CompanyId = company, ParentDepartmentId = parent,
            ECODE = "TREE-236-G", ENAME = "树子部门", SortOrder = "0100" });
        var person = await CreateAsync("persons", new { CompanyId = company, DepartmentId = child,
            PersonNo = "TREE-236-P", ENAME = "树人员", EmploymentStatus = "Active", IsEnabled = true });
        await CreateAsync("persons", new { PersonNo = "TREE-236-U", ENAME = "无归属人员",
            EmploymentStatus = "Active", IsEnabled = true });

        // 公司规则拒绝后，规范对象允许覆盖规则，使业务根重新可达。
        await SaveRuleAsync(new("TreeCompany236", "TreeCompany236", null, "拒绝公司", true, "organization.company",
            new("attribute", "EID", "equal", JsonSerializer.SerializeToElement(company)),
            [new("CompanyDeny", "all", null, 0, new Dictionary<string, string> { ["read"] = "deny" })], []));
        Assert.DoesNotContain((await ReadTreeAsync("roots")).EnumerateArray(), node => NodeKey(node) == "company:" + company);
        await SaveObjectAsync("organization.company", company, "allow");
        Assert.Contains((await ReadTreeAsync("roots")).EnumerateArray(), node => NodeKey(node) == "company:" + company);

        // 子规则覆盖本分支祖先；对象拒绝又覆盖子规则。可读子孙仍无法越过不可读父节点。
        await SaveRuleAsync(new("TreeDepartment236", "TreeDepartment236", null, "部门父规则", true, "organization.department",
            new("attribute", "IsEnabled", "equal", JsonSerializer.SerializeToElement(true)),
            [new("DepartmentDeny", "all", null, 0, new Dictionary<string, string> { ["read"] = "deny" })],
            [new("TreeDepartmentAllow236", "TreeDepartment236", "TreeDepartment236", "子规则允许", true,
                "organization.department", new("attribute", "EID", "equal", JsonSerializer.SerializeToElement(parent)),
                [new("DepartmentAllow", "user", actor.AccountId, 10, new Dictionary<string, string> { ["read"] = "allow" })], [])]));
        await SaveObjectAsync("organization.department", parent, "deny");
        await SaveObjectAsync("organization.department", child, "allow");

        var roots = await ReadTreeAsync("roots");
        Assert.Contains(roots.EnumerateArray(), node => NodeKey(node) == "company:" + company);
        Assert.Contains(roots.EnumerateArray(), node => NodeKey(node) == "company:" + other);
        Assert.False(roots.EnumerateArray().Single(node => NodeKey(node) == "company:" + company).GetProperty("hasChildren").GetBoolean());
        Assert.Empty((await ReadTreeAsync("children&nodeKey=" + Uri.EscapeDataString("company:" + company))).EnumerateArray());
        await AssertMissingTreeAsync("children&nodeKey=" + Uri.EscapeDataString("department:" + parent));
        await AssertMissingTreeAsync("children&nodeKey=" + Uri.EscapeDataString("department:" + child));
        await AssertMissingTreeAsync("path&nodeKey=" + Uri.EscapeDataString("department:" + child));
        await AssertMissingTreeAsync("path&nodeKey=" + Uri.EscapeDataString("person:" + person));
        Assert.Equal(0, (await ReadTreeAsync("search&keyword=树子部门&page=1&pageSize=1")).GetProperty("total").GetInt32());
        Assert.Equal(0, (await ReadTreeAsync("search&keyword=树人员&page=1&pageSize=1")).GetProperty("total").GetInt32());
        Assert.Equal(child, (await FeatureHttpTestRequests.ReadAsync(ordinary, departmentRead,
            "/api/organization/departments/" + child)).GetProperty("fields").GetProperty("EID").GetString());
        Assert.Equal(person, (await FeatureHttpTestRequests.ReadAsync(ordinary, personRead,
            "/api/organization/persons/" + person)).GetProperty("fields").GetProperty("EID").GetString());

        await SaveObjectAsync("organization.department", parent, null);
        Assert.True((await ReadTreeAsync("roots")).EnumerateArray()
            .Single(node => NodeKey(node) == "company:" + company).GetProperty("hasChildren").GetBoolean());
        Assert.Equal("department:" + parent, NodeKey(Assert.Single((await ReadTreeAsync(
            "children&nodeKey=" + Uri.EscapeDataString("company:" + company))).EnumerateArray())));
        Assert.Equal("department:" + child, NodeKey(Assert.Single((await ReadTreeAsync(
            "children&nodeKey=" + Uri.EscapeDataString("department:" + parent))).EnumerateArray())));
        Assert.Equal(1, (await ReadTreeAsync("search&keyword=树人员&page=1&pageSize=1")).GetProperty("total").GetInt32());
        Assert.Equal(4, (await ReadTreeAsync("path&nodeKey=" + Uri.EscapeDataString("person:" + person))).GetArrayLength());

        await SaveObjectAsync("organization.department", parent, "deny");
        await fixture.RestartWithActionAsync("read", false);
        using var readDisabled = fixture.CreateClient();
        await BusinessAuditHostFixture.RefreshAntiforgeryAsync(readDisabled);
        using (var login = await readDisabled.PostAsJsonAsync("/api/auth/login",
            new { ECODE = "tree-scope-236", Password = "Audit-test-secure-938!" }))
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var restored = await FeatureHttpTestRequests.ReadAsync(readDisabled, tree, "/api/trees/organization.structure/roots?scope=global");
        Assert.Contains(restored.EnumerateArray(), node => NodeKey(node) == "company:" + company);
        var restoredChildren = await FeatureHttpTestRequests.ReadAsync(readDisabled, tree,
            "/api/trees/organization.structure/children?scope=global&nodeKey=" + Uri.EscapeDataString("company:" + company));
        Assert.Equal("department:" + parent, NodeKey(Assert.Single(restoredChildren.EnumerateArray())));
        var unassignedSearch = await FeatureHttpTestRequests.ReadAsync(readDisabled, tree,
            "/api/trees/organization.structure/search?scope=global&keyword=无归属人员&page=1&pageSize=20");
        Assert.Equal(0, unassignedSearch.GetProperty("total").GetInt32());

        async Task<string> CreateAsync(string tab, object payload) => BusinessAuditSqlServerHttpApiTests.Id(
            await FeatureHttpTestRequests.CreateAsync(admin, http.Organization(tab, "create", "list.pageActions"),
                "/api/organization/" + tab, payload));

        async Task SaveRuleAsync(DataScopeRuleDto rule)
        {
            using var response = await FeatureHttpTestRequests.SendAsync(admin,
                fixture.FeatureCatalog.Single(point => point.BehaviorKey == "authorization.dataScope.save"),
                HttpMethod.Put, "/api/authorization/data-scope/rules/" + rule.Id, rule);
            Assert.True(response.StatusCode == HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
        }

        async Task SaveObjectAsync(string resource, string id, string? opinion)
        {
            var subjects = opinion is null ? [] : new[]
            { new DataScopeDirectSubjectDto("TreeObject236_" + id, "user", actor.AccountId, 0,
                new Dictionary<string, string> { ["read"] = opinion }) };
            using var response = await FeatureHttpTestRequests.SendAsync(admin,
                fixture.FeatureCatalog.First(point => point.BehaviorKey == resource + ".authorize"), HttpMethod.Put,
                $"/api/authorization/data-scope/objects/{resource}/{id}", new DataScopeObjectDto(resource, id, subjects));
            Assert.True(response.StatusCode == HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
        }

        async Task<JsonElement> ReadTreeAsync(string operation) => await FeatureHttpTestRequests.ReadAsync(ordinary, tree,
            TreePath(operation));

        async Task AssertMissingTreeAsync(string operation)
        {
            using var response = await FeatureHttpTestRequests.SendAsync(ordinary, tree, HttpMethod.Get,
                TreePath(operation));
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        static string TreePath(string operation)
        {
            var query = operation.IndexOf('&');
            return "/api/trees/organization.structure/" + (query < 0 ? operation + "?scope=global" :
                operation[..query] + "?scope=global" + operation[query..]);
        }

        static string? NodeKey(JsonElement node) => node.GetProperty("nodeKey").GetString();
    }

    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "正式规则与对象裁决下量测关系树代表规模的 SQL 成本")]
    public async Task OrganizationTree_RecordsRulesAndTreeScaleSqlCost()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync();
        var http = new FeatureHttpTestRequests(fixture);
        using var admin = await fixture.LoginAdministratorAsync();
        var tree = fixture.FeatureCatalog.Single(point => point.BehaviorKey == "tree:organization.structure");
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "TreeCost236", "树成本",
            "tree-cost-236", "树成本人员", tree);
        using var ordinary = actor.Client;
        var company = BusinessAuditSqlServerHttpApiTests.Id(await FeatureHttpTestRequests.CreateAsync(admin,
            http.Organization("companies", "create", "list.pageActions"), "/api/organization/companies",
            new { ECODE = "TREE-COST-236", ENAME = "成本公司", SortOrder = "0100" }));
        var saveRule = fixture.FeatureCatalog.Single(point => point.BehaviorKey == "authorization.dataScope.save");
        for (var index = 0; index < 12; index++)
        {
            var id = "TreeCostRule236_" + index;
            using var response = await FeatureHttpTestRequests.SendAsync(admin, saveRule, HttpMethod.Put,
                "/api/authorization/data-scope/rules/" + id,
                new DataScopeRuleDto(id, id, null, id, true, "organization.department",
                    new("attribute", "ECODE", "equal", JsonSerializer.SerializeToElement("NEVER-" + index)),
                    [new("Subject_" + id, "all", null, index,
                        new Dictionary<string, string> { ["read"] = "deny" })], []));
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }
        using (var response = await FeatureHttpTestRequests.SendAsync(admin, saveRule, HttpMethod.Put,
            "/api/authorization/data-scope/rules/TreeCostCompany236",
            new DataScopeRuleDto("TreeCostCompany236", "TreeCostCompany236", null, "公司拒绝", true,
                "organization.company", new("attribute", "EID", "equal", JsonSerializer.SerializeToElement(company)),
                [new("TreeCostCompanySubject236", "all", null, 0,
                    new Dictionary<string, string> { ["read"] = "deny" })], [])))
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        using (var response = await FeatureHttpTestRequests.SendAsync(admin,
            fixture.FeatureCatalog.First(point => point.BehaviorKey == "organization.company.authorize"),
            HttpMethod.Put, $"/api/authorization/data-scope/objects/organization.company/{company}",
            new DataScopeObjectDto("organization.company", company,
                [new("TreeCostCompanyGrant236", "user", actor.AccountId, 0,
                    new Dictionary<string, string> { ["read"] = "allow" })])))
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        using var scope = fixture.Services.CreateScope();
        var services = scope.ServiceProvider;
        services.GetRequiredService<IHttpContextAccessor>().HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, actor.AccountId)], "Test"))
        };
        var db = services.GetRequiredService<EfPlatformDbContext>();
        var connection = Assert.IsType<SqlConnection>(db.Database.GetDbConnection());
        await connection.OpenAsync();
        // 仅在本测试可删除数据库启用最近实际执行计划，避免改变共享 SQL Server 实例。
        await db.Database.ExecuteSqlRawAsync("ALTER DATABASE SCOPED CONFIGURATION SET LAST_QUERY_PLAN_STATS = ON");
        connection.StatisticsEnabled = true;
        var reader = services.GetRequiredService<RelationTreeReader>();
        var definition = RelationTreeMetadata.Project(services.GetRequiredService<CanonicalModulePlan>())
            .Single(item => item.ViewKey == "organization.structure");
        var created = 0;
        foreach (var size in new[] { 128, 512 })
        {
            for (; created < size; created++)
                db.Set<Dictionary<string, object?>>(db.ResolveModelKey("Department")).Add(new()
                {
                    ["EID"] = $"Department_TreeCost_{created:D4}", ["CompanyId"] = company,
                    ["ParentDepartmentId"] = null, ["ECODE"] = $"COST-{created:D4}",
                    ["ENAME"] = $"Tree Cost Department {created:D4}", ["IsEnabled"] = true,
                    ["SortOrder"] = $"{created:D4}", ["CreatedBy"] = fixture.ActorId,
                    ["UpdatedBy"] = fixture.ActorId, ["CreatedAt"] = DateTime.UtcNow,
                    ["UpdatedAt"] = DateTime.UtcNow
                });
            await db.SaveChangesAsync();
            connection.ResetStatistics();
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var result = await reader.SearchAsync(definition, "Tree Cost Department", 1, 20, default);
            watch.Stop();
            Assert.Equal(size, result.Total);
            Assert.Equal(20, result.Items.Count);
            var statistics = connection.RetrieveStatistics();
            output.WriteLine(JsonSerializer.Serialize(new { departmentRows = size, ruleRoots = 13, objectSubjects = 1,
                operation = "search-page-1", elapsedMs = watch.Elapsed.TotalMilliseconds,
                roundTrips = statistics["ServerRoundtrips"], selectedRows = statistics["SelectRows"],
                bytesReceived = statistics["BytesReceived"] }));
        }
        await using var observer = new SqlConnection(db.Database.GetConnectionString());
        await observer.OpenAsync();
        await using var plan = observer.CreateCommand();
        plan.CommandText = """
            SELECT TOP (1) CONVERT(nvarchar(max), p.query_plan)
            FROM sys.dm_exec_query_stats s
            CROSS APPLY sys.dm_exec_sql_text(s.sql_handle) t
            CROSS APPLY sys.dm_exec_query_plan_stats(s.plan_handle) p
            WHERE t.text LIKE N'%Reachable AS%' AND t.text LIKE N'%OFFSET%'
              AND t.text LIKE N'%DataScopeObjectSubject%'
              AND t.text NOT LIKE N'%sys.dm_exec_query_stats%'
            ORDER BY s.last_execution_time DESC
            """;
        var xml = await plan.ExecuteScalarAsync() as string;
        Assert.NotNull(xml);
        var actualPlan = System.Xml.Linq.XDocument.Parse(xml);
        var operators = actualPlan.Descendants()
            .Where(node => node.Name.LocalName == "RelOp")
            .Select(node => new { physical = (string?)node.Attribute("PhysicalOp"),
                actualRows = Counter(node, "ActualRows"), logicalReads = Counter(node, "ActualLogicalReads") })
            .ToArray();
        Assert.Contains(operators, item => item.actualRows > 0);
        var sqlParameters = actualPlan.Descendants().Where(node => node.Name.LocalName == "ColumnReference"
                && node.Attribute("ParameterDataType") is not null)
            .Select(node => new { name = (string?)node.Attribute("Column"), type = (string?)node.Attribute("ParameterDataType") })
            .Distinct().ToArray();
        Assert.Contains(sqlParameters, item => item.name?.StartsWith("@treeScope", StringComparison.Ordinal) == true);
        output.WriteLine(JsonSerializer.Serialize(new { plan = "last-actual", operatorCount = operators.Length,
            parameterCount = sqlParameters.Length, parameterTypes = sqlParameters.Select(item => item.type).Distinct().ToArray(),
            operators = operators.GroupBy(item => item.physical).Select(group => new { physical = group.Key,
                count = group.Count(), maxRows = group.Max(item => item.actualRows),
                logicalReads = group.Sum(item => item.logicalReads) }).ToArray() }));

        static long Counter(System.Xml.Linq.XElement operation, string name) => operation.Elements()
            .Where(item => item.Name.LocalName == "RunTimeInformation").SelectMany(item => item.Elements())
            .Select(item => (long?)item.Attribute(name)).Where(value => value.HasValue).Sum() ?? 0;
    }
}
