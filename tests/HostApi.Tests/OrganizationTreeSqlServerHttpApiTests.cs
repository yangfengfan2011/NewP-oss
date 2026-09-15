using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Authorization;
using ICP.Platform.Authorization.Extensions;
using ICP.Platform.Crud.Extensions;
using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Runtime;
using ICP.Platform.Modules.Organization.Directories;
using ICP.Platform.Modules.Organization.Persistence.Ef;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Data.Common;
using ICP.Platform.Modules.Organization.Crud;
using ICP.Platform.Modules.Organization.Persistence.Descriptors;
using ICP.Platform.Persistence.Auditing;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Persistence.EfCore.Projection;
using ICP.Platform.Persistence.Planning;
using ICP.Platform.Presentation;
using ICP.Platform.Query.Runtime;
using ICP.Platform.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed class OrganizationTreeSqlServerHttpApiTests
{
    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "Organization 范围列表与树端点在正式权限流水线和真实 SQL Server 上贯通")]
    public async Task ScopedDepartmentApi_IntersectsBeforeCountAndPagingAndTreeReadsUsePermissions()
    {
        var admin = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("NEWP_ORGANIZATION_SQLSERVER_ADMIN_CONNECTION")
            ?? "Server=(localdb)\\MSSQLLocalDB;Database=master;Integrated Security=true;TrustServerCertificate=true") { InitialCatalog = "master" };
        var catalog = $"NewPOrganizationTree_{Guid.NewGuid():N}";
        var treeArtifacts = Directory.CreateTempSubdirectory("NewPRelationTree_");
        await using var connection = new SqlConnection(admin.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE DATABASE [{catalog}]";
        await command.ExecuteNonQueryAsync();
        try
        {
            var models = OrganizationModelDefinitions.All.Select(model => model with { OwnerModuleId = "Organization" }).ToArray();
            var plan = new CompiledPersistencePlan("candidate", "snapshot", "hash", default, "digest", [], models, []);
            var stamp = new Stamp();
            var recorder = new QueryRecorder();
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            builder.Services.AddAuthentication("Test").AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, TestAuthentication>("Test", _ => { });
            builder.Services.AddPlatformAuthorization();
            builder.Services.AddSingleton<ICP.Platform.Authorization.Http.IFeatureDecisionSource, GrantedPermissions>();
            builder.Services.AddStandardCrudKernel();
            builder.Services.AddStandardCrudResource(CompanyCrudDescriptorFactory.Create());
            builder.Services.AddStandardCrudResource(DepartmentCrudDescriptorFactory.Create());
            // 组织结构树视图含人员节点，树 SQL 在启动期需要能解析全部节点资源。
            builder.Services.AddStandardCrudResource(PersonCrudDescriptorFactory.Create());
            var treePlan = ReadTreePlan(treeArtifacts.FullName);
            builder.Services.AddRelationTrees(treePlan);
            builder.Services.AddScoped<IStandardCrudValidator, CompanyCrudValidator>();
            builder.Services.AddScoped<IStandardCrudValidator, DepartmentCrudValidator>();
            builder.Services.AddScoped<IStandardCrudQueryCustomizer, DepartmentCrudQueryCustomizer>();
            builder.Services.AddScoped<IStandardCrudDisplayValueResolver, DepartmentCrudDisplayValueResolver>();
            builder.Services.AddScoped<ICompanyDirectory, EfCompanyDirectory>();
            builder.Services.AddScoped<IDepartmentDirectory, EfDepartmentDirectory>();
            builder.Services.AddControllers();
            builder.Services.AddSingleton(plan);
            builder.Services.AddSingleton<IEntityAuditStampProvider>(stamp);
            builder.Services.AddSingleton<IRequestPresentationContextAccessor, Presentation>();
            builder.Services.AddSingleton<ITemporalDisplayFormatter, TemporalDisplayFormatter>();
            builder.Services.AddSingleton<ITemporalDisplayProjectionExecutor, TemporalDisplayProjectionExecutor>();
            var database = new SqlConnectionStringBuilder(admin.ConnectionString) { InitialCatalog = catalog }.ConnectionString;
            builder.Services.AddScoped(_ => new EfPlatformDbContext(new DbContextOptionsBuilder<EfPlatformDbContext>()
                .UseSqlServer(database).AddInterceptors(recorder).ReplaceService<IModelCacheKeyFactory, EfPlatformModelCacheKeyFactory>().Options, plan, stamp));
            builder.Services.AddScoped<IPlatformQueryExecutor, EfPlatformQueryExecutor>();
            await using var app = builder.Build();
            app.Use(async (context, next) =>
            {
                context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, context.Request.Headers["X-Deny"].FirstOrDefault() ?? "admin")], "Test"));
                await next();
            });
            app.UseAuthorization();
            app.MapStandardCrudResources();
            app.MapRelationTrees();
            app.MapControllers();
            await using (var scope = app.Services.CreateAsyncScope())
            {
                await scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>().Database.EnsureCreatedAsync();
            }
            await app.StartAsync();
            using var client = app.GetTestClient();
            client.DefaultRequestHeaders.Add("X-NewP-Feature", "fixture");
            async Task<string> Create(string resource, Dictionary<string, object?> values)
            {
                using var response = await client.PostAsJsonAsync("/api/organization/" + resource, values);
                Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
                return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("fields").GetProperty("EID").GetString()!;
            }
            var company = await Create("companies", new() { ["ECODE"] = "C1", ["ENAME"] = "Company 1", ["SortOrder"] = "01" });
            var other = await Create("companies", new() { ["ECODE"] = "C2", ["ENAME"] = "Company 2", ["SortOrder"] = "02" });
            var first = await Create("departments", new() { ["ECODE"] = "D1", ["ENAME"] = "Department 1", ["CompanyId"] = company, ["SortOrder"] = "01" });
            var second = await Create("departments", new() { ["ECODE"] = "D2", ["ENAME"] = "Department 2", ["CompanyId"] = company, ["SortOrder"] = "02" });
            await Create("departments", new() { ["ECODE"] = "CH", ["ENAME"] = "Child", ["CompanyId"] = company, ["ParentDepartmentId"] = first, ["SortOrder"] = "00" });
            await Create("departments", new() { ["ECODE"] = "OTHER", ["ENAME"] = "Other", ["CompanyId"] = other, ["SortOrder"] = "00" });
            var globalRoute = "/api/organization/departments?queryScope=" + Uri.EscapeDataString("{\"kind\":\"global\"}")
                + "&pageSize=1&pageIndex=2&fields=EID&sortBy=SortOrder&sortDirection=asc";
            recorder.Commands.Clear();
            using var globalResponse = await client.GetAsync(globalRoute);
            Assert.Equal(HttpStatusCode.OK, globalResponse.StatusCode);
            var globalList = await globalResponse.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(4, globalList.GetProperty("total").GetInt32());
            Assert.Single(globalList.GetProperty("items").EnumerateArray());
            Assert.Equal(2, recorder.Commands.Count);
            Assert.Contains(recorder.Commands, sql => sql.Contains("COUNT("));
            Assert.Contains(recorder.Commands, sql => sql.Contains("OFFSET") && sql.Contains("FETCH NEXT"));
            recorder.Commands.Clear();
            var globalFiltered = await client.GetFromJsonAsync<JsonElement>(globalRoute + "&companyId=" + Uri.EscapeDataString(company));
            Assert.Equal(3, globalFiltered.GetProperty("total").GetInt32());
            Assert.Equal(first, Assert.Single(globalFiltered.GetProperty("items").EnumerateArray()).GetProperty("EID").GetString());
            Assert.Equal(2, recorder.Commands.Count);
            Assert.All(recorder.Commands, sql => Assert.Contains("CompanyId] =", sql));
            Assert.Contains(recorder.Commands, sql => sql.Contains("OFFSET") && sql.Contains("FETCH NEXT"));
            foreach (var invalidScope in new[] { "{}", "{\"CompanyId\":\"missing-parent\"}", "{\"kind\":\"global\",\"ParentDepartmentId\":null}" })
            {
                recorder.Commands.Clear();
                // TestServer 直接传播 Kestrel 会转为 HTTP 400 的 BadHttpRequestException。
                var invalidScopeError = await Assert.ThrowsAsync<Microsoft.AspNetCore.Http.BadHttpRequestException>(
                    () => client.GetAsync("/api/organization/departments?queryScope=" + Uri.EscapeDataString(invalidScope)));
                Assert.Equal(400, invalidScopeError.StatusCode);
                Assert.Empty(recorder.Commands);
            }
            var scopeQuery = Uri.EscapeDataString(JsonSerializer.Serialize(new { CompanyId = company, ParentDepartmentId = (string?)null }));
            var route = $"/api/organization/departments?queryScope={scopeQuery}&pageSize=1&pageIndex=2&fields=EID,CompanyId,ParentDepartmentId";
            recorder.Commands.Clear();
            var list = await client.GetFromJsonAsync<JsonElement>(route);
            Assert.Equal(2, list.GetProperty("total").GetInt32());
            Assert.Equal(second, Assert.Single(list.GetProperty("items").EnumerateArray()).GetProperty("EID").GetString());
            Assert.Equal(3, recorder.Commands.Count); // count + page + canonical company display batch
            var scopedReads = recorder.Commands.Where(sql => sql.Contains("[Department]")).ToArray();
            Assert.Equal(2, scopedReads.Length);
            Assert.All(scopedReads, sql => { Assert.Contains("CompanyId", sql); Assert.Contains("ParentDepartmentId] IS NULL", sql); });
            Assert.Contains(recorder.Commands, sql => sql.Contains("OFFSET") && sql.Contains("FETCH NEXT"));
            var conflict = await client.GetFromJsonAsync<JsonElement>(route + "&companyId=" + Uri.EscapeDataString(other));
            Assert.Equal(0, conflict.GetProperty("total").GetInt32());
            Assert.Empty(conflict.GetProperty("items").EnumerateArray());
            using var denied = new HttpRequestMessage(HttpMethod.Get, route); denied.Headers.Add("X-Deny", "1");
            recorder.Commands.Clear();
            using var rejected = await client.SendAsync(denied);
            Assert.Equal(HttpStatusCode.Forbidden, rejected.StatusCode); Assert.Empty(recorder.Commands);

            const string standard = "/api/trees/organization.structure/";
            // 树读取使用其声明的功能行为；拒绝发生在执行 SQL 之前。
            foreach (var deny in new[] { "1", "tree:organization.structure" })
            {
                recorder.Commands.Clear();
                using var deniedTree = new HttpRequestMessage(HttpMethod.Get, standard + "roots?scope=global");
                deniedTree.Headers.Add("X-Deny", deny);
                using var treeRejected = await client.SendAsync(deniedTree);
                Assert.Equal(HttpStatusCode.Forbidden, treeRejected.StatusCode);
                Assert.Empty(recorder.Commands);
            }
            var standardRoots = await client.GetFromJsonAsync<JsonElement>(standard + "roots?scope=global");
            Assert.Equal(2, standardRoots.GetArrayLength()); Assert.True(standardRoots[0].TryGetProperty("label", out _));
            var standardChildren = await client.GetFromJsonAsync<JsonElement>(standard + "children?scope=global&nodeKey=" + Uri.EscapeDataString("company:" + company));
            Assert.Equal(2, standardChildren.GetArrayLength());
            Assert.Equal(company, standardChildren[0].GetProperty("context").GetProperty("companyId").GetString());
            recorder.Commands.Clear();
            var standardSearch = await client.GetFromJsonAsync<JsonElement>(standard + "search?scope=global&keyword=Department&page=2&pageSize=1");
            Assert.Equal(2, standardSearch.GetProperty("total").GetInt32());
            var hit = Assert.Single(standardSearch.GetProperty("items").EnumerateArray());
            Assert.Equal("department:" + second, hit.GetProperty("node").GetProperty("nodeKey").GetString());
            Assert.Equal(2, hit.GetProperty("path").GetArrayLength());
            // 原生 DbCommand 沿用连接直接执行；SQL 形状由执行器测试证明，结果由真实数据库证明。
            var standardPath = await client.GetFromJsonAsync<JsonElement>(standard + "path?scope=global&nodeKey=" + Uri.EscapeDataString("department:" + second));
            Assert.Equal(2, standardPath.GetArrayLength());
            foreach (var permission in new[] { "tree:organization.structure" })
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, standard + "search?scope=global&keyword=Department&page=1&pageSize=20");
                request.Headers.Add("X-Deny", permission);
                using var response = await client.SendAsync(request);
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            }
            using var injection = await client.GetAsync(standard + "roots?scope=global&model=Person");
            Assert.Equal(HttpStatusCode.BadRequest, injection.StatusCode);
            Assert.True((await injection.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors").TryGetProperty("query", out _));
            using var badStandardPage = await client.GetAsync(standard + "search?scope=global&keyword=Department&page=0&pageSize=20");
            Assert.Equal(HttpStatusCode.BadRequest, badStandardPage.StatusCode);
            Assert.True((await badStandardPage.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors").TryGetProperty("search", out _));
            using var missingStandard = await client.GetAsync(standard + "path?scope=global&nodeKey=department:missing");
            Assert.Equal(HttpStatusCode.NotFound, missingStandard.StatusCode);
            await using (var treeScope = app.Services.CreateAsyncScope())
            {
                var reader = treeScope.ServiceProvider.GetRequiredService<RelationTreeReader>();
                var definition = Assert.Single(RelationTreeMetadata.Project(treePlan));
                var departmentNode = definition.Source.Nodes.Single(node => node.Key == "department");
                var filtered = definition with { Source = new() { Nodes = [definition.Source.Nodes[0], departmentNode with
                { Filters = [new() { Field = "ECODE", Operator = "=", Value = JsonSerializer.SerializeToElement("D2") }] }] } };
                Assert.Single((await reader.ChildrenAsync(filtered, "company:" + company, default))!);
                Assert.Equal(1, (await reader.SearchAsync(filtered, "Department", 1, 20, default)).Total);
                var sql = treeScope.ServiceProvider.GetRequiredService<RelationTreeSql>().Build(filtered);
                Assert.DoesNotContain("D2", sql.Sql);
                Assert.Contains("D2", sql.Parameters.Values);
                var constrained = new RelationTreeReader(treeScope.ServiceProvider.GetRequiredService<EfPlatformDbContext>(),
                    new RelationTreeSql(treeScope.ServiceProvider.GetRequiredService<EfPlatformDbContext>(),
                        treeScope.ServiceProvider.GetRequiredService<IStandardCrudResourceRegistry>(), [new NarrowDepartmentQuery()]));
                Assert.Equal(0, (await constrained.SearchAsync(filtered, "Department", 1, 20, default)).Total);
                // 可选空值条件须沿用普通 CRUD 的忽略语义，非空条件和 isNull 仍须执行。
                var optional = new RelationTreeReader(treeScope.ServiceProvider.GetRequiredService<EfPlatformDbContext>(),
                    new RelationTreeSql(treeScope.ServiceProvider.GetRequiredService<EfPlatformDbContext>(),
                        treeScope.ServiceProvider.GetRequiredService<IStandardCrudResourceRegistry>(), [new OptionalDepartmentQuery()]));
                Assert.Equal(1, (await optional.SearchAsync(definition, "Department", 1, 20, default)).Total);
                Assert.Single((await optional.ChildrenAsync(definition, "company:" + company, default))!);
                var staticNull = definition with { Source = new() { Nodes = [definition.Source.Nodes[0], departmentNode with
                { Filters = [new() { Field = "ECODE", Operator = "=", Value = JsonSerializer.SerializeToElement<object?>(null) }] }] } };
                Assert.Equal(0, (await optional.SearchAsync(staticNull, "Department", 1, 20, default)).Total);
                var selfTree = definition with { Source = new() { Nodes = [departmentNode with
                { RootWhenParentNull = true, Parents = [departmentNode.Parents[0]] }] } };
                Assert.Equal(3, (await reader.RootsAsync(selfTree, default)).Count);
                Assert.Single((await reader.PathAsync(selfTree, "department:" + second, default))!);
                var databaseContext = treeScope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
                await using var transaction = await databaseContext.Database.BeginTransactionAsync();
                await databaseContext.Database.ExecuteSqlInterpolatedAsync($"UPDATE Department SET ParentDepartmentId={second} WHERE EID={first}");
                await databaseContext.Database.ExecuteSqlInterpolatedAsync($"UPDATE Department SET ParentDepartmentId={first} WHERE EID={second}");
                await Assert.ThrowsAsync<InvalidOperationException>(() => reader.PathAsync(definition, "department:" + first, default));
                await transaction.RollbackAsync();
            }

        }
        finally
        {
            SqlConnection.ClearAllPools();
            command.CommandText = $"ALTER DATABASE [{catalog}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{catalog}]";
            await command.ExecuteNonQueryAsync();
            treeArtifacts.Delete(true);
        }
    }

    private static CanonicalModulePlan ReadTreePlan(string root)
    {
        var repository = new DirectoryInfo(AppContext.BaseDirectory);
        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "modules", "Organization", "newp.plugin.json"))) repository = repository.Parent;
        var source = Path.Combine(repository!.FullName, "modules", "Organization");
        var module = Path.Combine(root, "modules", "Organization");
        foreach (var path in Directory.GetFiles(Path.Combine(source, "metadata"), "*.json", SearchOption.AllDirectories))
        {
            var target = Path.Combine(module, Path.GetRelativePath(source, path));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(path, target);
        }
        var manifest = JsonNode.Parse(File.ReadAllText(Path.Combine(source, "newp.plugin.json")))!;
        manifest["dependencies"] = new JsonArray();
        File.WriteAllText(Path.Combine(module, "plugin.json"), manifest.ToJsonString());
        File.WriteAllText(Path.Combine(root, "OrganizationModule.dll"), "fixture assembly");
        var trust = TrustedBuiltInModuleSource.CreateForHost(root, Path.Combine(root, "modules"));
        var candidate = Assert.Single(new ModuleCapsuleCandidateReader().ReadTrustedBuiltIn(trust));
        var plan = Assert.IsType<CanonicalModulePlan>(new CanonicalModulePlanCompiler().Compile("relation-tree", [candidate]).Plan);
        // 签发后改动磁盘不能改变本次启动消费的树定义。
        File.WriteAllText(Path.Combine(module, "metadata/views/OrganizationStructure.view.json"), "{}");
        return plan;
    }

    private sealed class NarrowDepartmentQuery : IStandardCrudQueryCustomizer
    {
        public string ResourceKey => "organization.department";
        public IReadOnlyList<ICP.Platform.Crud.Models.StandardCrudQueryCondition> BuildConditions(
            ICP.Platform.Crud.Descriptors.StandardCrudDescriptor descriptor, ICP.Platform.Crud.Models.StandardCrudListQuery query) =>
            [new("ECODE", "=", "D1")];
    }

    private sealed class OptionalDepartmentQuery : IStandardCrudQueryCustomizer
    {
        public string ResourceKey => "organization.department";
        public IReadOnlyList<ICP.Platform.Crud.Models.StandardCrudQueryCondition> BuildConditions(
            ICP.Platform.Crud.Descriptors.StandardCrudDescriptor descriptor, ICP.Platform.Crud.Models.StandardCrudListQuery query) =>
            [new("ENAME", "=", null), new("ENAME", "contains", null, "optional", "or"),
                new("ECODE", "=", "D2", "optional", "or"), new("ParentDepartmentId", "isNull", null)];
    }

    private sealed class TestAuthentication(Microsoft.Extensions.Options.IOptionsMonitor<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions> options,
        Microsoft.Extensions.Logging.ILoggerFactory logger, System.Text.Encodings.Web.UrlEncoder encoder)
        : Microsoft.AspNetCore.Authentication.AuthenticationHandler<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<Microsoft.AspNetCore.Authentication.AuthenticateResult> HandleAuthenticateAsync() =>
            Task.FromResult(Microsoft.AspNetCore.Authentication.AuthenticateResult.NoResult());
    }
    private sealed class QueryRecorder : DbCommandInterceptor
    {
        public List<string> Commands { get; } = [];
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        { Commands.Add(command.CommandText); return ValueTask.FromResult(result); }
    }
    private sealed class Stamp : IEntityAuditStampProvider
    {
        public DateTime Instant { get; set; } = new DateTime(2026, 9, 4, 7, 24, 26, DateTimeKind.Utc).AddTicks(5023291);
        public EntityAuditStamp GetCurrent() => new("system", Instant);
    }
    private sealed class Presentation : IRequestPresentationContextAccessor
    {
        public RequestPresentationContext? Current => new("zh-CN", "Asia/Taipei");
    }
    private sealed class GrantedPermissions : ICP.Platform.Authorization.Http.IFeatureDecisionSource
    {
        public ValueTask<PermissionDecision> EvaluateAsync(string subjectId, string position, string moduleId, string behaviorKey, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(subjectId == "1" || subjectId == behaviorKey ? PermissionDecision.Denied : PermissionDecision.Granted);
    }
}
