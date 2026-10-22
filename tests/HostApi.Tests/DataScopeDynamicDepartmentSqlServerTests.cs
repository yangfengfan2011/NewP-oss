using System.Net;
using System.Security.Claims;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Modules.Authorization.DTOs;
using ICP.Platform.Modules.Authorization.Services;
using ICP.Platform.Organization.Contracts;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Testing;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static ICP.Platform.HostApi.Tests.EntityOwnershipBackfillSqlServerHttpApiTests;

namespace ICP.Platform.HostApi.Tests;

public sealed class DataScopeDynamicDepartmentSqlServerTests
{
    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "动态所属部门只按真实目标及已发布组织祖先匹配，不能由客户端指定相对主体身份")]
    public async Task OwnerDepartment_RequiresExplicitDescendants_AndRejectsClientClaimedRelativeIdentities()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        await InitializeAsync(fixture, admin);
        var position = await ContainerAsync(fixture, admin);
        var part = await CreateAsync(fixture, admin, position, "Part", "parts", "createPart", "Dynamic295AncestorPart");
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "Dynamic295Ancestor", "动态部门后代",
            "dynamic295-ancestor", "下级部门账号", Point(fixture, "workspace.spaces.access"));
        using var ordinary = actor.Client;
        var organization = (await fixture.Services.GetRequiredService<IOrganizationQuery>().ReadAsync()).FindByAccount(actor.AccountId)!;
        var department = organization.Department!;
        var http = new FeatureHttpTestRequests(fixture);
        var parent = await http.CreateButtonAsync(admin, http.Organization("departments", "create", "list.pageActions"),
            "/api/organization/departments", new { ECODE = "Dynamic295Parent", ENAME = "动态匹配上级部门", CompanyId = organization.Company!["EID"], SortOrder = "0001" });
        var parentId = BusinessAuditSqlServerHttpApiTests.Id(parent);
        using (var changed = await http.SendButtonAsync(admin, http.Organization("departments", "edit", "list.rowActions"), HttpMethod.Put,
            "/api/organization/departments/" + department["EID"], new { ENAME = department["ENAME"], ParentDepartmentId = parentId,
                SortOrder = department["SortOrder"], IsEnabled = true }))
            Assert.True(changed.StatusCode == HttpStatusCode.OK, await changed.Content.ReadAsStringAsync());
        Assert.Contains((await fixture.Services.GetRequiredService<IOrganizationQuery>().ReadAsync()).FindByAccount(actor.AccountId)!.Ancestors,
            row => Equals(row["EID"], parentId));
        using (var setup = fixture.Services.CreateScope())
            await setup.ServiceProvider.GetRequiredService<EfPlatformDbContext>().Set<Dictionary<string, object?>>("Part")
                .Where(row => EF.Property<string>(row, "EID") == part.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => EF.Property<string?>(row, "OwnerDepartmentId"), parentId));
        var manage = fixture.FeatureCatalog.Single(point => point.ModuleId == "Part" && point.Key == "authorize" && point.ActionScope == "list.rowActions");
        var path = "/api/part/parts/" + part.Id + "/permissions";
        foreach (var type in new[] { "creator", "owner", "ownerDepartment" })
        {
            using var forged = await FeatureHttpTestRequests.SendAsync(admin, manage, HttpMethod.Put, path,
                new DataScopeObjectDto("part.part", part.Id, [new("Forged295" + type, type, actor.AccountId, 1,
                    new Dictionary<string, string> { ["read"] = "allow" })]));
            Assert.Equal(HttpStatusCode.BadRequest, forged.StatusCode);
        }
        var empty = await FeatureHttpTestRequests.ReadAsync(admin, manage, path);
        Assert.Empty(empty.GetProperty("subjects").EnumerateArray());
        var input = new DataScopeObjectDto("part.part", part.Id,
            [new("Ancestor295All", "all", null, 0, new Dictionary<string, string> { ["read"] = "deny" }),
             new("Ancestor295Department", "ownerDepartment", null, 1, new Dictionary<string, string> { ["read"] = "allow" })]);
        await SaveAsync(input);
        await AssertReadAsync(false);
        await SaveAsync(input with { Subjects = [input.Subjects[0], input.Subjects[1] with { MatchOptions = new(true) }] });
        await AssertReadAsync(true);

        async Task SaveAsync(DataScopeObjectDto configuration)
        {
            using var result = await FeatureHttpTestRequests.SendAsync(admin, manage, HttpMethod.Put, path, configuration);
            Assert.True(result.StatusCode == HttpStatusCode.NoContent, await result.Content.ReadAsStringAsync());
        }
        async Task AssertReadAsync(bool expected)
        {
            using var scope = fixture.Services.CreateScope();
            var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
            var previous = accessor.HttpContext;
            accessor.HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, actor.AccountId)], "test-trusted-account")) };
            try
            {
                Assert.Equal(expected, await scope.ServiceProvider.GetRequiredService<IDataScopeObjectDecisionProvider>().IsAllowedAsync("part.part", part.Id, "read"));
                var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
                var plans = scope.ServiceProvider.GetRequiredService<IDataScopeReadPlanProvider>();
                var plan = await plans.GetReadPlanAsync("part.part");
                var query = db.Set<Dictionary<string, object?>>("Part").AsNoTracking().Where(row => EF.Property<string>(row, "EID") == part.Id);
                if (plan.Predicate is not null) query = query.Where(plan.Predicate);
                Assert.Equal(expected ? 1 : 0, await query.CountAsync());
                var explanation = await ((DataScopeReadPlans)plans).ExplainAsync("part.part", part.Id, actor.AccountId, default);
                Assert.Equal(expected, explanation.Single(action => action.Key == "read").Allowed);
            }
            finally { accessor.HttpContext = previous; }
        }
    }
}
