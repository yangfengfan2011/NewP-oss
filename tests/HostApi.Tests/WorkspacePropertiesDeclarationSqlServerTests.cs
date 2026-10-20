using System.Net;
using ICP.Platform.Modules.Authorization.DTOs;
using System.Text.Json;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static ICP.Platform.HostApi.Tests.EntityOwnershipBackfillSqlServerHttpApiTests;

namespace ICP.Platform.HostApi.Tests;

public sealed class WorkspacePropertiesDeclarationSqlServerTests
{
    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "Workspace属性HTTP外层缺少声明必须拒绝")]
    public Task Properties_RejectsMissingOuterReadEntry() => VerifyRejectedEntryAsync("Part", "parts", "createPart", "part.part", false);

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "Workspace属性HTTP不能串用独立对象的合法声明")]
    public Task Properties_RejectsForeignLegalReadEntry() => VerifyRejectedEntryAsync("Document", "documents", "createDocument", "document.document", true);

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "Folder属性与混合属性使用同一真实外层读取声明")]
    public Task Properties_FolderUsesTheSameOuterReadEntry() => VerifyRejectedEntryAsync("WorkspaceFolder", "folders", "create", "workspace.folder", false);

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "普通主体有原Workspace读但无authorize仍能读来源属性；目标read拒绝保持404")]
    public async Task Properties_OrdinaryReaderNeedsNoAuthorizationManagement()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        await InitializeAsync(fixture, admin);
        var position = await ContainerAsync(fixture, admin);
        var target = await CreateAsync(fixture, admin, position, "Part", "parts", "createPart", "PropertiesOrdinary");
        var read = Point(fixture, "workspace.spaces.access");
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "PropertiesRead", "属性读取", "properties-reader", "属性读取", read);
        using var ordinary = actor.Client;
        var path = "/api/workspace/content/" + target.LinkId + "/properties?queryScope="
            + Uri.EscapeDataString(JsonSerializer.Serialize(position, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var plan = fixture.Services.GetRequiredService<CanonicalModulePlan>();
        foreach (var denyRead in new[] { false, true })
        {
            var rule = new DataScopeRuleDto("PropertiesRead", "PropertiesRead", null, "PropertiesRead", true, "part.part",
                new("attribute", "EID", "equal", JsonSerializer.SerializeToElement(target.Id)),
                [new("PropertiesReadSubject", "all", null, 0, new Dictionary<string, string> { ["read"] = denyRead ? "deny" : "allow", ["authorize"] = "deny" })], []);
            using var saved = await FeatureHttpTestRequests.SendAsync(admin, Point(fixture, "authorization.dataScope.save"), HttpMethod.Put,
                "/api/authorization/data-scope/rules/PropertiesRead", rule);
            Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);
            foreach (var source in new[] { "workspace.content.properties", "workspace.content.receivedProperties" })
            {
                var declaration = Assert.Single(plan.ActionDataAccessCatalog, item => item.ViewKey == source && item.Execution == "workspace.properties.read");
                using var response = await FeatureHttpTestRequests.SendAsync(ordinary, read, HttpMethod.Get, path, dataAccess: declaration);
                Assert.Equal(denyRead ? HttpStatusCode.NotFound : HttpStatusCode.OK, response.StatusCode);
            }
        }
    }

    private static async Task VerifyRejectedEntryAsync(string model, string kind, string action, string resource, bool foreign)
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        await InitializeAsync(fixture, admin);
        var position = await ContainerAsync(fixture, admin);
        var target = await CreateAsync(fixture, admin, position, model, kind, action, "PropertiesSource" + model);
        var read = Point(fixture, "workspace.spaces.access");
        var declaration = foreign ? Assert.Single(fixture.Services.GetRequiredService<CanonicalModulePlan>().ActionDataAccessCatalog,
            item => item.Execution == resource + ".read" && item.Surface == "read") : null;
        var url = "/api/workspace/content/" + target.LinkId + "/properties?queryScope="
            + Uri.EscapeDataString(JsonSerializer.Serialize(position, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        using var response = await FeatureHttpTestRequests.SendAsync(admin, read, HttpMethod.Get, url, dataAccess: declaration);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var plan = fixture.Services.GetRequiredService<CanonicalModulePlan>();
        foreach (var source in new[] { "workspace.content.properties", "workspace.content.receivedProperties" })
        {
            var allowed = Assert.Single(plan.ActionDataAccessCatalog, item => item.Execution == "workspace.properties.read" && item.ViewKey == source);
            using var success = await FeatureHttpTestRequests.SendAsync(admin, read, HttpMethod.Get, url, dataAccess: allowed);
            Assert.Equal(HttpStatusCode.OK, success.StatusCode);
            using var document = JsonDocument.Parse(await success.Content.ReadAsStringAsync());
            var result = document.RootElement;
            Assert.Equal(resource, result.GetProperty("resourceKey").GetString());
            Assert.Equal(target.Id, result.GetProperty("objectId").GetString());
            using var wrongFeature = await FeatureHttpTestRequests.SendAsync(admin, Point(fixture, "workspace.folder.createPart"), HttpMethod.Get, url, dataAccess: allowed);
            Assert.Equal(HttpStatusCode.Forbidden, wrongFeature.StatusCode);
            using var missingTarget = await FeatureHttpTestRequests.SendAsync(admin, read, HttpMethod.Get,
                url.Replace(target.LinkId, "missing-source-lid", StringComparison.Ordinal), dataAccess: allowed);
            Assert.Equal(HttpStatusCode.NotFound, missingTarget.StatusCode);
        }
    }
}