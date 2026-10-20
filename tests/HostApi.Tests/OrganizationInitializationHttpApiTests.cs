using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Persistence.Ownership;
using ICP.Platform.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed class OrganizationInitializationHttpApiTests
{
    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "组织初始化以真实管理员维护身份为边界，普通功能授权不能提升身份")]
    public async Task Initialization_AdministratorRetriesWithoutPersonWhileOrdinaryFeatureHolderCannotInitialize()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync();
        using var admin = await fixture.LoginAdministratorAsync();
        var http = new FeatureHttpTestRequests(fixture);
        var initialize = http.Organization("companies", "initialize", "list.pageActions");
        using var allowed = await FeatureHttpTestRequests.SendAsync(admin, initialize, HttpMethod.Get, "/api/organization/initialization/eligibility");
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        Assert.True((await allowed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("allowed").GetBoolean());
        using (var missing = await FeatureHttpTestRequests.SendAsync(admin, initialize, HttpMethod.Post, "/api/organization/initialization"))
            Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        using var initialized = await FeatureHttpTestRequests.SendAsync(admin, initialize, HttpMethod.Post, "/api/organization/initialization", dataAccess: http.Declaration("organization.company.initialize", initialize));
        Assert.True(initialized.StatusCode == HttpStatusCode.OK, await initialized.Content.ReadAsStringAsync());
        var first = await initialized.Content.ReadFromJsonAsync<JsonElement>();
        var companyId = first.GetProperty("companyId").GetString()!;
        var departmentId = first.GetProperty("departmentId").GetString()!;
        Assert.True(first.GetProperty("companyCreated").GetBoolean());
        Assert.True(first.GetProperty("departmentCreated").GetBoolean());
        using var retry = await FeatureHttpTestRequests.SendAsync(admin, initialize, HttpMethod.Post, "/api/organization/initialization", dataAccess: http.Declaration("organization.company.initialize", initialize));
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        var second = await retry.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(companyId, second.GetProperty("companyId").GetString());
        Assert.Equal(departmentId, second.GetProperty("departmentId").GetString());
        Assert.False(second.GetProperty("companyCreated").GetBoolean());
        Assert.False(second.GetProperty("departmentCreated").GetBoolean());

        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "InitViewer", "初始化功能持有者", "init-viewer", "普通初始化功能持有者", initialize);
        using var ordinary = actor.Client;
        using var denied = await FeatureHttpTestRequests.SendAsync(ordinary, initialize, HttpMethod.Get, "/api/organization/initialization/eligibility");
        Assert.Equal(HttpStatusCode.OK, denied.StatusCode);
        Assert.False((await denied.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("allowed").GetBoolean());
        using var write = await FeatureHttpTestRequests.SendAsync(ordinary, initialize, HttpMethod.Post, "/api/organization/initialization", dataAccess: http.Declaration("organization.company.initialize", initialize));
        Assert.Equal(HttpStatusCode.Forbidden, write.StatusCode);

        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
        Assert.False(await db.Set<Dictionary<string, object?>>("Person").AnyAsync(row => EF.Property<string?>(row, "AccountEID") == fixture.ActorId));
        var directory = scope.ServiceProvider.GetRequiredService<IAccountOwnershipDirectory>();
        Assert.Equal(departmentId, (await directory.FindManyAsync([fixture.ActorId]))[fixture.ActorId].DepartmentId);
        Assert.Equal(2, await db.Set<Dictionary<string, object?>>("BusinessChange")
            .CountAsync(row => EF.Property<string>(row, "EventType") == "organization.company.initialized"
                || EF.Property<string>(row, "EventType") == "organization.department.initialized"));
    }
}
