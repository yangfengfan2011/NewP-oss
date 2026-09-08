using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using ICP.Platform.Authorization;
using ICP.Platform.Authorization.Abstractions;
using ICP.Platform.Authorization.Extensions;
using ICP.Platform.Crud.Extensions;
using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Modules.Organization.Controllers;
using ICP.Platform.Modules.Organization.Services;
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
            builder.Services.AddAuthorization(OrganizationTreeAccess.Configure);
            builder.Services.AddSingleton<IPermissionDecisionSource, GrantedPermissions>();
            builder.Services.AddStandardCrudKernel();
            builder.Services.AddStandardCrudResource(CompanyCrudDescriptorFactory.Create());
            builder.Services.AddStandardCrudResource(DepartmentCrudDescriptorFactory.Create());
            builder.Services.AddScoped<IStandardCrudValidator, CompanyCrudValidator>();
            builder.Services.AddScoped<IStandardCrudValidator, DepartmentCrudValidator>();
            builder.Services.AddScoped<IStandardCrudQueryCustomizer, DepartmentCrudQueryCustomizer>();
            builder.Services.AddScoped<IStandardCrudDisplayValueResolver, DepartmentCrudDisplayValueResolver>();
            builder.Services.AddScoped<ICompanyDirectory, EfCompanyDirectory>();
            builder.Services.AddScoped<IDepartmentDirectory, EfDepartmentDirectory>();
            builder.Services.AddScoped<OrganizationTreeReader>();
            builder.Services.AddControllers().AddApplicationPart(typeof(OrganizationTreeController).Assembly);
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
            app.MapControllers();
            await using (var scope = app.Services.CreateAsyncScope())
            {
                await scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>().Database.EnsureCreatedAsync();
            }
            await app.StartAsync();
            using var client = app.GetTestClient();
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
            using var deniedTree = new HttpRequestMessage(HttpMethod.Get, "/api/organization/tree/roots?scope=global"); deniedTree.Headers.Add("X-Deny", "1");
            using var treeRejected = await client.SendAsync(deniedTree); Assert.Equal(HttpStatusCode.Forbidden, treeRejected.StatusCode);
            foreach (var permission in new[] { "Organization.Company.View", "Organization.Department.View" })
            {
                recorder.Commands.Clear();
                using var partialRequest = new HttpRequestMessage(HttpMethod.Get, "/api/organization/tree/roots?scope=global");
                partialRequest.Headers.Add("X-Deny", permission);
                using var partialResponse = await client.SendAsync(partialRequest);
                Assert.Equal(HttpStatusCode.Forbidden, partialResponse.StatusCode);
                Assert.Empty(recorder.Commands);
            }
            var roots = await client.GetFromJsonAsync<JsonElement>("/api/organization/tree/roots?scope=global");
            Assert.Equal(2, roots.GetArrayLength()); Assert.True(roots[0].TryGetProperty("label", out _));
            var children = await client.GetFromJsonAsync<JsonElement>("/api/organization/tree/children?scope=global&nodeKey=" + Uri.EscapeDataString("company:" + company));
            Assert.Equal(2, children.GetArrayLength());
            var path = await client.GetFromJsonAsync<JsonElement>("/api/organization/tree/path?scope=global&nodeKey=" + Uri.EscapeDataString("department:" + second));
            Assert.Equal(2, path.GetArrayLength());
            var search = await client.GetFromJsonAsync<JsonElement>("/api/organization/tree/search?scope=global&keyword=Department&page=2&pageSize=1");
            Assert.Equal(2, search.GetProperty("total").GetInt32()); Assert.Single(search.GetProperty("items").EnumerateArray());
            using var absent = await client.GetAsync("/api/organization/tree/path?scope=global&nodeKey=department:missing"); Assert.Equal(HttpStatusCode.NotFound, absent.StatusCode);
            using var invalid = await client.GetAsync("/api/organization/tree/roots?scope=missing"); Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            var invalidProblem = await invalid.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("缺少或无效组织树范围。", invalidProblem.GetProperty("errors").GetProperty("scope")[0].GetString());

        }
        finally
        {
            SqlConnection.ClearAllPools();
            command.CommandText = $"ALTER DATABASE [{catalog}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{catalog}]";
            await command.ExecuteNonQueryAsync();
        }
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
    private sealed class GrantedPermissions : IPermissionDecisionSource
    {
        public ValueTask<PermissionDecision> EvaluateAsync(string subjectId, string permissionCode, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(subjectId == "1" || subjectId == permissionCode ? PermissionDecision.Denied : PermissionDecision.Granted);
    }
}
