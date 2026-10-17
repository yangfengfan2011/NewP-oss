using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ICP.Platform.Modules.Authorization.DTOs;
using ICP.Platform.Testing;
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
        var results = new Dictionary<string, HttpStatusCode>();
        foreach (var space in new[] { "public/initialize", "personal/ensure" })
        {
            using var initializedRoot = await FeatureHttpTestRequests.SendAsync(admin, entry, HttpMethod.Post, "/api/workspace/spaces/" + space);
            Assert.Equal(HttpStatusCode.OK, initializedRoot.StatusCode);
            var id = (await initializedRoot.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("rootFolderId").GetString()!;
            var path = "/api/authorization/data-scope/objects/workspace.folder/" + id;
            foreach (var suffix in new[] { "", "/explanation" })
            {
                using var response = await FeatureHttpTestRequests.SendAsync(admin, manage, HttpMethod.Get, path + suffix);
                results[space + ":GET" + suffix] = response.StatusCode;
            }
            using var saved = await FeatureHttpTestRequests.SendAsync(admin, manage, HttpMethod.Put, path,
                new DataScopeObjectDto("workspace.folder", id, []));
            results[space + ":PUT"] = saved.StatusCode;
        }
        output.WriteLine(JsonSerializer.Serialize(results));
        Assert.All(results, result => Assert.Equal(HttpStatusCode.NotFound, result.Value));
    }
}
