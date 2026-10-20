using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Modules.Authorization.DTOs;
using ICP.Platform.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace ICP.Platform.HostApi.Tests;

public sealed class DataScopeWorkspaceRootSqlServerTests(ITestOutputHelper output)
{
    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "真实空间根不因Folder共表而开放普通对象授权、解释或配置")]
    public async Task SpaceRoots_CannotUseGenericObjectManagementEvenWithAdministratorFeature()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        var initialize = fixture.FeatureCatalog.First(point => point.BehaviorKey == "organization.company.initialize");
        using var initialized = await FeatureHttpTestRequests.SendAsync(admin, initialize, HttpMethod.Post, "/api/organization/initialization");
        Assert.Equal(HttpStatusCode.OK, initialized.StatusCode);
        var entry = fixture.FeatureCatalog.Single(point => point.BehaviorKey == "workspace.spaces.access");
        var manage = fixture.FeatureCatalog.First(point => point.BehaviorKey == "workspace.folder.authorize");
        var source = Assert.Single(fixture.Services.GetRequiredService<CanonicalModulePlan>().ActionDataAccessCatalog,
            declaration => declaration.Execution == "workspace.content.authorization.manage" && declaration.FeaturePositions.Contains(manage.Position));
        var results = new Dictionary<string, HttpStatusCode>();
        foreach (var space in new[] { "public/initialize", "personal/ensure" })
        {
            using var initializedRoot = await FeatureHttpTestRequests.SendAsync(admin, entry, HttpMethod.Post, "/api/workspace/spaces/" + space);
            Assert.Equal(HttpStatusCode.OK, initializedRoot.StatusCode);
            var id = (await initializedRoot.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("rootFolderId").GetString()!;
            var path = "/api/authorization/data-scope/objects/workspace.folder/" + id;
            foreach (var suffix in new[] { "", "/explanation" })
            {
                using var response = await FeatureHttpTestRequests.SendAsync(admin, manage, HttpMethod.Get, path + suffix, dataAccess: source);
                results[space + ":GET" + suffix] = response.StatusCode;
            }
            using var saved = await FeatureHttpTestRequests.SendAsync(admin, manage, HttpMethod.Put, path,
                new DataScopeObjectDto("workspace.folder", id, []), source);
            results[space + ":PUT"] = saved.StatusCode;
        }
        output.WriteLine(JsonSerializer.Serialize(results));
        Assert.All(results, result => Assert.Equal(HttpStatusCode.NotFound, result.Value));
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "通用Folder管理消费登记能力，保原authorize资格和完整read下限")]
    public async Task GenericFolderManagement_PreservesQualificationAndRejectsBorrowedSources()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        await EntityOwnershipBackfillSqlServerHttpApiTests.InitializeAsync(fixture, admin);
        var position = await EntityOwnershipBackfillSqlServerHttpApiTests.ContainerAsync(fixture, admin);
        var target = await EntityOwnershipBackfillSqlServerHttpApiTests.CreateAsync(fixture, admin, position,
            "WorkspaceFolder", "folders", "create", "Auth318GenericFolder");
        var manage = fixture.FeatureCatalog.First(point => point.BehaviorKey == "workspace.folder.authorize");
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "Auth318GenericFolderRole", "通用目录授权角色",
            "auth318.generic.folder", "通用目录授权主体", manage);
        using var ordinary = actor.Client;
        var plan = fixture.Services.GetRequiredService<CanonicalModulePlan>();
        var source = Assert.Single(plan.ActionDataAccessCatalog, item => item.Execution == "workspace.content.authorization.manage"
            && item.FeaturePositions.Contains(manage.Position));
        var foreign = Assert.Single(plan.ActionDataAccessCatalog, item => item.Execution == "part.part.authorization.manage" && item.Surface == "rowActions");
        var path = "/api/authorization/data-scope/objects/workspace.folder/" + target.Id;
        using (var missing = await FeatureHttpTestRequests.SendAsync(ordinary, manage, HttpMethod.Get, path))
            Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        using (var crossed = await FeatureHttpTestRequests.SendAsync(ordinary, manage, HttpMethod.Get, path, dataAccess: foreign))
            Assert.Equal(HttpStatusCode.BadRequest, crossed.StatusCode);
        using (var allowed = await FeatureHttpTestRequests.SendAsync(ordinary, manage, HttpMethod.Get, path, dataAccess: source))
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        var input = new DataScopeObjectDto("workspace.folder", target.Id, [new("Auth318GenericAll", "all", null, 1,
            new Dictionary<string, string> { ["read"] = "allow", ["authorize"] = "deny" })]);
        using (var saved = await FeatureHttpTestRequests.SendAsync(ordinary, manage, HttpMethod.Put, path, input, source))
            Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);
        foreach (var suffix in new[] { "", "/catalog", "/subjects/user", "/explanation" })
        {
            using var denied = await FeatureHttpTestRequests.SendAsync(ordinary, manage, HttpMethod.Get, path + suffix, dataAccess: source);
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        }
        var unreadable = input with { Subjects = [new("Auth318GenericHidden", "all", null, 1,
            new Dictionary<string, string> { ["read"] = "deny" })] };
        using (var saved = await FeatureHttpTestRequests.SendAsync(admin, manage, HttpMethod.Put, path, unreadable, source))
            Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);
        using (var hidden = await FeatureHttpTestRequests.SendAsync(ordinary, manage, HttpMethod.Get, path, dataAccess: source))
            Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
    }
}
