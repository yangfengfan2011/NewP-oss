using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using ICP.Platform.Authorization;
using ICP.Platform.Authorization.Abstractions;
using ICP.Platform.Authorization.Extensions;
using ICP.Platform.Crud.Extensions;
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

public sealed class CompanyAuditSqlServerHttpApiTests
{
    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "Company 正式 CRUD HTTP 与真实 SQL Server 审计投影")]
    public async Task CompanyApi_PreservesSqlPrecisionAndProjectsExistingReadAndWriteResponses()
    {
        var admin = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("NEWP_ORGANIZATION_SQLSERVER_ADMIN_CONNECTION")
            ?? "Server=(localdb)\\MSSQLLocalDB;Database=master;Integrated Security=true;TrustServerCertificate=true") { InitialCatalog = "master" };
        var catalog = $"NewPCompanyAudit_{Guid.NewGuid():N}";
        await using var connection = new SqlConnection(admin.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE DATABASE [{catalog}]";
        await command.ExecuteNonQueryAsync();
        try
        {
            var model = OrganizationModelDefinitions.All.Single(model => model.Name == "Company") with { OwnerModuleId = "Organization" };
            var plan = new CompiledPersistencePlan("candidate", "snapshot", "hash", default, "digest", [], [model], []);
            var stamp = new Stamp();
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            builder.Services.AddPlatformAuthorization();
            builder.Services.AddSingleton<IPermissionDecisionSource, GrantedPermissions>();
            builder.Services.AddStandardCrudKernel();
            builder.Services.AddStandardCrudResource(CompanyCrudDescriptorFactory.Create());
            builder.Services.AddSingleton(plan);
            builder.Services.AddSingleton<IEntityAuditStampProvider>(stamp);
            builder.Services.AddSingleton<IRequestPresentationContextAccessor, Presentation>();
            builder.Services.AddSingleton<ITemporalDisplayFormatter, TemporalDisplayFormatter>();
            builder.Services.AddSingleton<ITemporalDisplayProjectionExecutor, TemporalDisplayProjectionExecutor>();
            var database = new SqlConnectionStringBuilder(admin.ConnectionString) { InitialCatalog = catalog }.ConnectionString;
            builder.Services.AddScoped(_ => new EfPlatformDbContext(new DbContextOptionsBuilder<EfPlatformDbContext>()
                .UseSqlServer(database).ReplaceService<IModelCacheKeyFactory, EfPlatformModelCacheKeyFactory>().Options, plan, stamp));
            builder.Services.AddScoped<IPlatformQueryExecutor, EfPlatformQueryExecutor>();
            await using var app = builder.Build();
            app.Use(async (context, next) =>
            {
                context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "admin")], "Test"));
                await next();
            });
            app.UseAuthorization();
            app.MapStandardCrudResources();
            await using (var scope = app.Services.CreateAsyncScope())
            {
                await scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>().Database.EnsureCreatedAsync();
            }
            await app.StartAsync();
            using var client = app.GetTestClient();
            const string route = "/api/organization/companies";
            using var created = await client.PostAsJsonAsync(route, new Dictionary<string, object?> { ["ECODE"] = "AUDIT", ["ENAME"] = "Audit company", ["SortOrder"] = "0100" });
            Assert.True(created.StatusCode == HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
            var fields = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("fields");
            var id = fields.GetProperty("EID").GetString();
            AssertAudit(fields, stamp.Instant);
            var detail = await client.GetFromJsonAsync<JsonElement>($"{route}/{id}");
            AssertAudit(detail.GetProperty("fields"), stamp.Instant);
            var list = await client.GetFromJsonAsync<JsonElement>(route);
            var row = Assert.Single(list.GetProperty("items").EnumerateArray());
            Assert.Equal(stamp.Instant, row.GetProperty("UpdatedAt").GetDateTime());
            Assert.Equal("2026/9/4 15:24:26", row.GetProperty("UpdatedAt_DisplayValue").GetString());
            Assert.False(row.TryGetProperty("CreatedBy_DisplayValue", out _));
            var first = stamp.Instant;
            stamp.Instant = stamp.Instant.AddSeconds(1);
            using var updated = await client.PutAsJsonAsync($"{route}/{id}", new Dictionary<string, object?> { ["ENAME"] = "Updated company" });
            Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
            var changed = (await updated.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("fields");
            Assert.Equal(first, changed.GetProperty("CreatedAt").GetDateTime());
            Assert.Equal(stamp.Instant, changed.GetProperty("UpdatedAt").GetDateTime());
            Assert.Equal("2026/9/4 15:24:27", changed.GetProperty("UpdatedAt_DisplayValue").GetString());
        }
        finally
        {
            SqlConnection.ClearAllPools();
            command.CommandText = $"ALTER DATABASE [{catalog}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{catalog}]";
            await command.ExecuteNonQueryAsync();
        }
    }

    private static void AssertAudit(JsonElement fields, DateTime instant)
    {
        foreach (var field in new[] { "CreatedAt", "UpdatedAt" })
        {
            Assert.Equal(instant, fields.GetProperty(field).GetDateTime());
            Assert.EndsWith(".5023291Z", fields.GetProperty(field).GetString());
            Assert.Equal("2026/9/4 15:24:26", fields.GetProperty(field + "_DisplayValue").GetString());
        }
        foreach (var field in new[] { "CreatedBy", "UpdatedBy" })
        {
            Assert.Equal("system", fields.GetProperty(field).GetString());
            Assert.Equal("系统", fields.GetProperty(field + "_DisplayValue").GetString());
        }
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
            ValueTask.FromResult(PermissionDecision.Granted);
    }
}
