using System.Net;
using System.Net.Http.Json;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed class OrganizationWriteExecutionSqlServerTests
{
    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "四资源真实标准创建先核具体声明，管理资格与读取声明不能替代写来源")]
    public async Task FourCreateRoutes_RequireTheirExactWriteDeclaration()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync();
        using var admin = await fixture.LoginAdministratorAsync();
        var http = new FeatureHttpTestRequests(fixture);
        var plan = fixture.Services.GetRequiredService<CanonicalModulePlan>();
        string? company = null;
        foreach (var (tab, resource) in new[] { ("companies", "organization.company"), ("departments", "organization.department"),
            ("positions", "organization.position"), ("persons", "organization.person") })
        {
            var feature = http.Organization(tab, "create", "list.pageActions");
            object body = tab switch
            {
                "companies" => new { ECODE = "WRITE310-C", ENAME = "正式创建公司", SortOrder = "0100" },
                "departments" => new { CompanyId = company!, ECODE = "WRITE310-D", ENAME = "正式创建部门", SortOrder = "0100" },
                "positions" => new { ENAME = "正式创建岗位" },
                _ => new { PersonNo = "WRITE310-P", ENAME = "正式创建人员", EmploymentStatus = "FullTime", IsEnabled = true }
            };
            var endpoint = "/api/organization/" + tab;
            using (var missing = await FeatureHttpTestRequests.SendAsync(admin, feature, HttpMethod.Post, endpoint, body))
                Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
            var borrowed = plan.ActionDataAccessCatalog.Single(item => item.Execution == resource + ".list"
                && item.FeaturePositions.Contains(http.Organization(tab).Position));
            using (var swapped = await FeatureHttpTestRequests.SendAsync(admin, feature, HttpMethod.Post, endpoint, body, borrowed))
                Assert.Equal(HttpStatusCode.BadRequest, swapped.StatusCode);
            var binding = plan.ActionDataAccessCatalog.Single(item => item.Execution == resource + ".create" && item.FeaturePositions.Contains(feature.Position));
            using var created = await FeatureHttpTestRequests.SendAsync(admin, feature, HttpMethod.Post, endpoint, body, binding);
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            if (tab == "companies") company = BusinessAuditSqlServerHttpApiTests.Id(await created.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>());
        }
    }

    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "四资源更新删除保持各自声明及原条件删除，读取或创建声明不能替代写来源")]
    public async Task FourUpdateAndDeleteRoutes_RequireTheirExactWriteDeclarations()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync();
        using var admin = await fixture.LoginAdministratorAsync();
        var http = new FeatureHttpTestRequests(fixture);
        var targets = new List<(string Tab, string Resource, string Id)>();
        string? company = null;
        foreach (var (tab, resource) in new[] { ("companies", "organization.company"), ("departments", "organization.department"),
            ("positions", "organization.position"), ("persons", "organization.person") })
        {
            var create = http.Organization(tab, "create", "list.pageActions");
            object body = tab switch
            {
                "companies" => new { ECODE = "CHANGE310-C", ENAME = "更新删除公司", SortOrder = "0100" },
                "departments" => new { CompanyId = company!, ECODE = "CHANGE310-D", ENAME = "更新删除部门", SortOrder = "0100" },
                "positions" => new { ENAME = "更新删除岗位" },
                _ => new { PersonNo = "CHANGE310-P", ENAME = "更新删除人员", EmploymentStatus = "FullTime", IsEnabled = true }
            };
            var created = await FeatureHttpTestRequests.CreateAsync(admin, create, "/api/organization/" + tab, body,
                http.Declaration(resource + ".create", create));
            var id = BusinessAuditSqlServerHttpApiTests.Id(created);
            targets.Add((tab, resource, id));
            if (tab == "companies") company = id;
            var edit = http.Organization(tab, "edit", "list.rowActions");
            var endpoint = "/api/organization/" + tab + "/" + id;
            var values = new { ENAME = "正式更新-" + tab };
            using (var missing = await FeatureHttpTestRequests.SendAsync(admin, edit, HttpMethod.Put, endpoint, values))
                Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
            using (var swapped = await FeatureHttpTestRequests.SendAsync(admin, edit, HttpMethod.Put, endpoint, values,
                http.Declaration(resource + ".create", create)))
                Assert.Equal(HttpStatusCode.BadRequest, swapped.StatusCode);
            using (var updated = await FeatureHttpTestRequests.SendAsync(admin, edit, HttpMethod.Put, endpoint, values,
                http.Declaration(resource + ".update", edit)))
                Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        }
        // 逆序删除保留公司/部门原引用约束，不把正确声明误作业务豁免。
        foreach (var (tab, resource, id) in targets.AsEnumerable().Reverse())
        {
            var delete = http.Organization(tab, "delete", "list.rowActions");
            var endpoint = "/api/organization/" + tab + "/" + id;
            using (var missing = await FeatureHttpTestRequests.SendAsync(admin, delete, HttpMethod.Delete, endpoint))
                Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
            using (var swapped = await FeatureHttpTestRequests.SendAsync(admin, delete, HttpMethod.Delete, endpoint,
                dataAccess: http.Declaration(resource + ".list", http.Organization(tab))))
                Assert.Equal(HttpStatusCode.BadRequest, swapped.StatusCode);
            using (var deleted = await FeatureHttpTestRequests.SendAsync(admin, delete, HttpMethod.Delete, endpoint,
                dataAccess: http.Declaration(resource + ".delete", delete)))
                Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        }
    }

}