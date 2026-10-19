using System.Net;
using System.Text.Json;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Modules.Authorization.DTOs;
using ICP.Platform.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Extensions;
using ICP.Platform.Crud.Runtime;
using ICP.Platform.Persistence.EfCore;
using Microsoft.EntityFrameworkCore;

namespace ICP.Platform.HostApi.Tests;

public sealed class OrganizationExecutionSqlServerTests
{
    [Fact]
    public void FormalTreeRegistration_RequiresItsExecutionServiceAndConstructsWithCompleteDependencies()
    {
        var compiled = new CanonicalModulePlanCompiler().Compile("tree-composition",
            new ModuleCapsuleCandidateReader().ReadBuiltIn(Path.Combine(AppContext.BaseDirectory, "modules")));
        Assert.True(compiled.IsSuccess, JsonSerializer.Serialize(compiled.Diagnostics));
        var services = new ServiceCollection();
        services.AddSingleton(compiled.Plan!);
        services.AddScoped(_ => new EfPlatformDbContext(new DbContextOptionsBuilder<EfPlatformDbContext>().Options,
            Array.Empty<ICP.Platform.Modeling.Definitions.ModelDefinition>()));
        services.AddSingleton<IStandardCrudResourceRegistry>(new StandardCrudResourceRegistry([]));
        services.AddRelationTrees(compiled.Plan!);
        using (var incomplete = services.BuildServiceProvider())
        using (var scope = incomplete.CreateScope())
        {
            var missing = Assert.Throws<InvalidOperationException>(() => scope.ServiceProvider.GetRequiredService<RelationTreeReader>());
            Assert.Contains(nameof(IDataExecutionAccess), missing.Message);
        }
        services.AddHttpContextAccessor();
        services.AddSingleton<IDataScopeObjectAccess, ConstructionOnlyDecisions>();
        services.AddSingleton<IDataScopeObjectDecisionProvider, ConstructionOnlyDecisions>();
        services.AddScoped<IDataExecutionAccess, ICP.Platform.Modules.Authorization.Services.DataExecutionAccess>();
        using var complete = services.BuildServiceProvider();
        using var completeScope = complete.CreateScope();
        Assert.IsType<RelationTreeReader>(completeScope.ServiceProvider.GetRequiredService<RelationTreeReader>());
    }

    // 此控制仅构造真实装配，不执行或伪造一次权限通过。
    private sealed class ConstructionOnlyDecisions : IDataScopeObjectAccess, IDataScopeObjectDecisionProvider
    {
        public Task<bool> IsReadableAsync(string resourceKey, string objectId, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Construction only.");
        public Task<bool> IsAllowedAsync(string resourceKey, string objectId, string actionKey, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Construction only.");
        public Task<bool> IsCandidateAllowedAsync(string resourceKey, IReadOnlyDictionary<string, object?> candidate, string actionKey, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Construction only.");
    }

    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "人员公司候选必须使用其正式来源，当前不可读值保ID遮蔽且不借宿主CRUD声明")]
    public async Task PersonCompanyLookup_RequiresItsSourceAndRetainsMaskedCurrentValues()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync();
        using var admin = await fixture.LoginAdministratorAsync();
        var http = new FeatureHttpTestRequests(fixture);
        var read = http.Organization("persons");
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "Lookup310", "候选声明",
            "lookup310", "候选普通主体", read);
        using var ordinary = actor.Client;
        var company = BusinessAuditSqlServerHttpApiTests.Id(await FeatureHttpTestRequests.CreateAsync(admin,
            http.Organization("companies", "create", "list.pageActions"), "/api/organization/companies",
            new { ECODE = "LOOKUP310", ENAME = "声明隐藏公司", SortOrder = "0100" }));
        const string path = "/api/organization/person-lookups/companies";
        using (var missing = await FeatureHttpTestRequests.SendAsync(ordinary, read, HttpMethod.Get, path))
            Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        var plan = fixture.Services.GetRequiredService<CanonicalModulePlan>();
        var binding = plan.ActionDataAccessCatalog.Single(item => item.Execution == "organization.person.company.lookup"
            && item.FeaturePositions.Contains(read.Position));
        using (var allowed = await FeatureHttpTestRequests.SendAsync(ordinary, read, HttpMethod.Get, path, dataAccess: binding))
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        var borrowed = plan.ActionDataAccessCatalog.Single(item => item.Execution == "organization.person.list" && item.FeaturePositions.Contains(read.Position));
        using (var swapped = await FeatureHttpTestRequests.SendAsync(ordinary, read, HttpMethod.Get, path, dataAccess: borrowed))
            Assert.Equal(HttpStatusCode.BadRequest, swapped.StatusCode);
        var rule = new DataScopeRuleDto("Lookup310Hidden", "Lookup310Hidden", null, "隐藏候选", true, "organization.company",
            new("attribute", "EID", "equal", JsonSerializer.SerializeToElement(company)),
            [new("Lookup310All", "all", null, 0, new Dictionary<string, string> { ["read"] = "deny" })], []);
        using (var saved = await FeatureHttpTestRequests.SendAsync(admin,
            fixture.FeatureCatalog.Single(point => point.BehaviorKey == "authorization.dataScope.save"), HttpMethod.Put,
            "/api/authorization/data-scope/rules/Lookup310Hidden", rule))
            Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);
        using var masked = await FeatureHttpTestRequests.SendAsync(ordinary, read, HttpMethod.Get,
            path + "?keyword=声明隐藏公司&currentValueIds=" + company, dataAccess: binding);
        Assert.Equal(HttpStatusCode.OK, masked.StatusCode);
        using var body = JsonDocument.Parse(await masked.Content.ReadAsStringAsync());
        Assert.Empty(body.RootElement.GetProperty("items").EnumerateArray());
        var current = Assert.Single(body.RootElement.GetProperty("currentItems").EnumerateArray());
        Assert.Equal(company, current.GetProperty("value").GetString());
        Assert.Equal("***", current.GetProperty("label").GetString());
    }
    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "专属候选按真实operation隔离声明并保留各自当前值读取")]
    public async Task OtherLookups_RequireTheirExactSources()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync();
        using var admin = await fixture.LoginAdministratorAsync();
        var http = new FeatureHttpTestRequests(fixture);
        var personRead = http.Organization("persons"); var departmentRead = http.Organization("departments");
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "OtherLookup310", "专属候选",
            "other-lookup310", "专属候选主体", personRead, departmentRead);
        using var ordinary = actor.Client;
        var company = BusinessAuditSqlServerHttpApiTests.Id(await FeatureHttpTestRequests.CreateAsync(admin,
            http.Organization("companies", "create", "list.pageActions"), "/api/organization/companies",
            new { ECODE = "OTHER310", ENAME = "来源公司", SortOrder = "0100" }));
        var department = BusinessAuditSqlServerHttpApiTests.Id(await FeatureHttpTestRequests.CreateAsync(admin,
            http.Organization("departments", "create", "list.pageActions"), "/api/organization/departments",
            new { CompanyId = company, ECODE = "OTHER310-D", ENAME = "来源部门", SortOrder = "0100" }));
        foreach (var source in new[] { "person-department", "department-company", "department-parent" })
        {
            var point = source == "person-department" ? personRead : departmentRead;
            var execution = source switch { "person-department" => "organization.person.department.lookup",
                "department-company" => "organization.department.company.lookup", _ => "organization.department.parent.lookup" };
            var path = source switch { "person-department" => "/api/organization/person-lookups/departments?companyId=" + company + "&",
                "department-company" => "/api/organization/department-lookups/companies?", _ => "/api/organization/department-lookups/parents?companyId=" + company + "&" };
            var target = source == "department-company" ? company : department;
            path += "currentValueIds=" + target;
            var catalog = fixture.Services.GetRequiredService<CanonicalModulePlan>().ActionDataAccessCatalog;
            var binding = catalog.Single(item => item.Execution == execution && item.FeaturePositions.Contains(point.Position));
            using (var missing = await FeatureHttpTestRequests.SendAsync(ordinary, point, HttpMethod.Get, path))
                Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
            using (var valid = await FeatureHttpTestRequests.SendAsync(ordinary, point, HttpMethod.Get, path, dataAccess: binding))
            {
                Assert.Equal(HttpStatusCode.OK, valid.StatusCode);
                using var body = JsonDocument.Parse(await valid.Content.ReadAsStringAsync());
                Assert.Equal(target, Assert.Single(body.RootElement.GetProperty("currentItems").EnumerateArray()).GetProperty("value").GetString());
            }
            var borrowed = catalog.Single(item => item.Execution == (source == "person-department" ? "organization.person.list" : "organization.department.list")
                && item.FeaturePositions.Contains(point.Position));
            using var swapped = await FeatureHttpTestRequests.SendAsync(ordinary, point, HttpMethod.Get, path, dataAccess: borrowed);
            Assert.Equal(HttpStatusCode.BadRequest, swapped.StatusCode);
        }
    }

    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "完整关系树来源不能借平面列表，父节点读取拒绝仍在SQL中截断")]
    public async Task Tree_UsesItsDeclaredQueryAndStopsAtUnreadableParent()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync();
        using var admin = await fixture.LoginAdministratorAsync();
        var http = new FeatureHttpTestRequests(fixture);
        var tree = fixture.FeatureCatalog.Single(point => point.BehaviorKey == "tree:organization.structure");
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "Tree310", "树声明",
            "tree310", "树主体", tree);
        using var ordinary = actor.Client;
        var company = BusinessAuditSqlServerHttpApiTests.Id(await FeatureHttpTestRequests.CreateAsync(admin,
            http.Organization("companies", "create", "list.pageActions"), "/api/organization/companies",
            new { ECODE = "TREE310", ENAME = "树来源公司", SortOrder = "0100" }));
        var department = BusinessAuditSqlServerHttpApiTests.Id(await FeatureHttpTestRequests.CreateAsync(admin,
            http.Organization("departments", "create", "list.pageActions"), "/api/organization/departments",
            new { CompanyId = company, ECODE = "TREE310-D", ENAME = "树来源部门", SortOrder = "0100" }));
        var path = "/api/trees/organization.structure/children?scope=global&nodeKey=company:" + company;
        var catalog = fixture.Services.GetRequiredService<CanonicalModulePlan>().ActionDataAccessCatalog;
        var binding = catalog.Single(item => item.Execution == "organization.structure.read" && item.FeaturePositions.Contains(tree.Position));
        using (var missing = await FeatureHttpTestRequests.SendAsync(ordinary, tree, HttpMethod.Get, path))
            Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        using (var valid = await FeatureHttpTestRequests.SendAsync(ordinary, tree, HttpMethod.Get, path, dataAccess: binding))
        {
            Assert.Equal(HttpStatusCode.OK, valid.StatusCode);
            using var body = JsonDocument.Parse(await valid.Content.ReadAsStringAsync());
            Assert.Equal("department:" + department, Assert.Single(body.RootElement.EnumerateArray()).GetProperty("nodeKey").GetString());
        }
        var borrowed = catalog.Single(item => item.Execution == "organization.company.list" && item.FeaturePositions.Contains(http.Organization("companies").Position));
        using (var swapped = await FeatureHttpTestRequests.SendAsync(ordinary, tree, HttpMethod.Get, path, dataAccess: borrowed))
            Assert.Equal(HttpStatusCode.BadRequest, swapped.StatusCode);
        var rule = new DataScopeRuleDto("Tree310Hidden", "Tree310Hidden", null, "隐藏父", true, "organization.company",
            new("attribute", "EID", "equal", JsonSerializer.SerializeToElement(company)),
            [new("Tree310All", "all", null, 0, new Dictionary<string, string> { ["read"] = "deny" })], []);
        using (var saved = await FeatureHttpTestRequests.SendAsync(admin,
            fixture.FeatureCatalog.Single(point => point.BehaviorKey == "authorization.dataScope.save"), HttpMethod.Put,
            "/api/authorization/data-scope/rules/Tree310Hidden", rule)) Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);
        using var hidden = await FeatureHttpTestRequests.SendAsync(ordinary, tree, HttpMethod.Get, path, dataAccess: binding);
        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
    }

}
