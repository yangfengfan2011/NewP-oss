using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ICP.Platform.Organization.Contracts;
using ICP.Platform.Persistence.EfCore;
using Microsoft.EntityFrameworkCore;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Modules.Organization.Crud;
using ICP.Platform.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed class OrganizationWriteExecutionSqlServerTests
{
    [Fact]
    [Trait("Category", "ModuleMetadataConsumer")]
    public async Task FourResourceButtons_UseTheCompiledIdentityAndTheExistingRequestHelper()
    {
        var result = new CanonicalModulePlanCompiler().Compile("organization-buttons", new ModuleCapsuleCandidateReader()
            .ReadBuiltIn(Path.Combine(AppContext.BaseDirectory, "modules")));
        Assert.True(result.IsSuccess, System.Text.Json.JsonSerializer.Serialize(result.Diagnostics));
        var plan = result.Plan!;
        foreach (var descriptor in new[] { CompanyCrudDescriptorFactory.Create(), DepartmentCrudDescriptorFactory.Create(),
            PositionCrudDescriptorFactory.Create(), PersonCrudDescriptorFactory.Create() })
        {
            Assert.True(descriptor.DataExecutions.ContainsKey("list"));
            Assert.True(descriptor.DataExecutions.ContainsKey("read"));
            foreach (var (operation, action) in new[] { ("create", "create"), ("update", "edit"), ("delete", "delete") })
            {
                Assert.False(descriptor.DataExecutions.ContainsKey(operation));
                Assert.DoesNotContain(plan.Modules.SelectMany(module => module.DataExecutions), item => item.Key == descriptor.ResourceKey + "." + operation);
                var buttons = plan.ActionDataAccessCatalog.Where(item => item.BusinessBinding == descriptor.ResourceKey + "." + operation).ToArray();
                Assert.NotEmpty(buttons);
                foreach (var button in buttons)
                {
                    Assert.Equal(descriptor.ModelName, button.Object);
                    Assert.Equal(new[] { action }, button.Actions);
                    Assert.Empty(button.Execution);
                    var point = plan.FeatureCatalog.Single(item => item.Position == button.FeaturePositions[0]);
                    using var capture = new HeaderCapture();
                    using var client = new HttpClient(capture) { BaseAddress = new Uri("http://metadata-test") };
                    using var response = await FeatureHttpTestRequests.SendAsync(client, point, HttpMethod.Post, "/source", dataAccess: button);
                    Assert.Equal(button.Id, capture.Button);
                    Assert.Equal(point.Position, capture.Feature);
                    Assert.False(capture.Legacy);
                }
            }
        }
    }

    private sealed class HeaderCapture : HttpMessageHandler
    {
        internal string? Button;
        internal string? Feature;
        internal bool Legacy;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Button = request.Headers.GetValues("X-NewP-Button").Single();
            Feature = request.Headers.GetValues("X-NewP-Feature").Single();
            Legacy = request.Headers.Contains("X-NewP-Data-Declaration");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        }
    }

    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "四资源真实标准创建先核唯一按钮身份，管理资格与读取声明不能替代写来源")]
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
            var binding = http.Declaration(resource + ".create", feature);
            using var created = await FeatureHttpTestRequests.SendAsync(admin, feature, HttpMethod.Post, endpoint, body, binding);
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            if (tab == "companies") company = BusinessAuditSqlServerHttpApiTests.Id(await created.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>());
        }
    }

    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "四资源更新删除保持各自按钮及原条件删除，读取或创建身份不能替代写来源")]
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
                _ => new { PersonNo = "CHANGE310-P", ENAME = "更新删除人员", EmploymentStatus = "FullTime", IsEnabled = true,
                    CompanyId = company!, DepartmentId = targets.Single(item => item.Tab == "departments").Id,
                    PositionId = targets.Single(item => item.Tab == "positions").Id }
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
        var query = fixture.Services.GetRequiredService<IOrganizationQuery>();
        var beforeBlocked = await SnapshotAsync(fixture);
        foreach (var (tab, resource, id) in targets.Where(item => item.Tab != "persons"))
        {
            var delete = http.Organization(tab, "delete", "list.rowActions");
            using var blocked = await FeatureHttpTestRequests.SendAsync(admin, delete, HttpMethod.Delete,
                "/api/organization/" + tab + "/" + id, dataAccess: http.Declaration(resource + ".delete", delete));
            Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
            Assert.Equal(beforeBlocked, await SnapshotAsync(fixture));
            Assert.Equal("正式更新-" + tab, (await query.ReadAsync()).Find(Kind(resource), id)!["ENAME"]);
        }
        // 真实关联移除后才允许逆序删除，每次成功都更新同一组织缓存。
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
            Assert.Null((await query.ReadAsync()).Find(Kind(resource), id));
        }
    }

    private static OrganizationRecordKind Kind(string resource) => Enum.Parse<OrganizationRecordKind>(resource.Split('.')[1], true);

    private static async Task<string> SnapshotAsync(BusinessAuditHostFixture fixture)
    {
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
        var result = new Dictionary<string, object>();
        foreach (var model in new[] { "Company", "Department", "Position", "Person", "BusinessChange" })
            result[model] = await db.Set<Dictionary<string, object?>>(db.ResolveModelKey(model)).AsNoTracking()
                .OrderBy(row => EF.Property<string>(row, "EID")).ToListAsync();
        return JsonSerializer.Serialize(result);
    }

}