using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Modules.Authorization.DTOs;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static ICP.Platform.HostApi.Tests.EntityOwnershipBackfillSqlServerHttpApiTests;

namespace ICP.Platform.HostApi.Tests;

public sealed class WorkspaceObjectAuthorizationExecutionSqlServerTests
{
    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "工作空间异构来源、合法空筛选及后续目标拒绝仍整批无变化")]
    public async Task MixedPermissions_ValidateEmptyPreparationAndKeepLaterTargetRejectionAtomic()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        await InitializeAsync(fixture, admin);
        var position = await ContainerAsync(fixture, admin);
        var targets = new[]
        {
            await CreateAsync(fixture, admin, position, "WorkspaceFolder", "folders", "create", "Auth318MixedFolder"),
            await CreateAsync(fixture, admin, position, "Part", "parts", "createPart", "Auth318MixedPart"),
            await CreateAsync(fixture, admin, position, "Document", "documents", "createDocument", "Auth318MixedDoc")
        };
        var batch = Assert.Single(fixture.FeatureCatalog, point => point.ModuleId == "Workspace" && point.Key == "batchAuthorize"
            && point.ActionScope == "list.pageActions");
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "Auth318MixedRole", "异构授权角色",
            "auth318.mixed.actor", "异构授权主体", Point(fixture, "workspace.spaces.access"), batch);
        using var ordinary = actor.Client;
        var viewer = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "Auth318MixedViewerRole", "工作空间只读权限角色",
            "auth318.mixed.viewer", "工作空间只读权限主体", Point(fixture, "workspace.spaces.access"));
        using var readOnly = viewer.Client;
        var plan = fixture.Services.GetRequiredService<CanonicalModulePlan>();
        var source = Assert.Single(plan.ActionDataAccessCatalog, item => item.Execution == "workspace.content.authorization.batch");
        var borrowed = Assert.Single(plan.ActionDataAccessCatalog, item => item.Execution == "workspace.content.authorization.manage" && item.Surface == "rowActions");
        var sources = new[] { new { position, linkIds = targets.Select(target => target.LinkId).ToArray() } };
        const string prepare = "/api/workspace/content/permissions/batch/prepare";
        var emptyInput = new { sources, filters = new { createdBy = "auth318-no-matching-creator" } };
        using (var missing = await FeatureHttpTestRequests.SendAsync(ordinary, batch, HttpMethod.Post, prepare, emptyInput))
            Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        using (var crossed = await FeatureHttpTestRequests.SendAsync(ordinary, batch, HttpMethod.Post, prepare, emptyInput, borrowed))
            Assert.Equal(HttpStatusCode.BadRequest, crossed.StatusCode);
        using (var noFeature = await FeatureHttpTestRequests.SendAsync(readOnly, batch, HttpMethod.Post, prepare, emptyInput, source))
            Assert.Equal(HttpStatusCode.Forbidden, noFeature.StatusCode);
        using (var empty = await FeatureHttpTestRequests.SendAsync(ordinary, batch, HttpMethod.Post, prepare, emptyInput, source))
        {
            Assert.Equal(HttpStatusCode.OK, empty.StatusCode);
            var body = await empty.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(0, body.GetProperty("total").GetInt32());
            Assert.Empty(body.GetProperty("items").EnumerateArray());
            Assert.Empty(body.GetProperty("catalogs").EnumerateArray());
        }
        await SaveFolderRuleAsync(new() { ["read"] = "allow", ["authorize"] = "deny" });
        var view = Assert.Single(plan.ActionDataAccessCatalog, item => item.Execution == "workspace.content.authorization.view" && item.Surface == "read");
        var queryScope = Uri.EscapeDataString(JsonSerializer.Serialize(position, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        using (var viewed = await FeatureHttpTestRequests.SendAsync(readOnly, Point(fixture, "workspace.spaces.access"), HttpMethod.Get,
            "/api/workspace/content/" + targets[0].LinkId + "/permissions/explanation?queryScope=" + queryScope, dataAccess: view))
        {
            Assert.Equal(HttpStatusCode.OK, viewed.StatusCode);
            var actions = (await viewed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("actions").EnumerateArray();
            Assert.False(Assert.Single(actions, action => action.GetProperty("key").GetString() == "authorize").GetProperty("allowed").GetBoolean());
        }
        using (var prepared = await FeatureHttpTestRequests.SendAsync(ordinary, batch, HttpMethod.Post, prepare, new { sources }, source))
        {
            Assert.Equal(HttpStatusCode.OK, prepared.StatusCode);
            var items = (await prepared.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("items").EnumerateArray().ToArray();
            Assert.Equal(3, items.Length);
            Assert.Single(items, item => !item.GetProperty("manageable").GetBoolean()
                && item.GetProperty("resourceKey").GetString() == "workspace.folder");
        }
        var input = new { sources, mode = "U", resources = new[] { "workspace.folder", "part.part", "document.document" }.Select(resource =>
            new { resourceKey = resource, subjects = new[] { new { type = "all", subjectId = (string?)null, priority = 1,
                actions = new Dictionary<string, string> { ["read"] = "allow" } } } }) };
        var before = await DirectSubjectsAsync();
        Assert.Equal("workspace.folder", new[] { "workspace.folder", "part.part", "document.document" }.Order(StringComparer.Ordinal).Last());
        using (var rejected = await FeatureHttpTestRequests.SendAsync(ordinary, batch, HttpMethod.Post,
            "/api/workspace/content/permissions/batch", input, source))
            Assert.Equal(HttpStatusCode.Forbidden, rejected.StatusCode);
        Assert.Equal(before, await DirectSubjectsAsync());
        using (var current = await FeatureHttpTestRequests.SendAsync(ordinary, batch, HttpMethod.Post,
            "/api/workspace/content/permissions/batch/current", new { sources }, source))
            Assert.Equal(HttpStatusCode.Forbidden, current.StatusCode);
        await SaveFolderRuleAsync(new());
        using (var saved = await FeatureHttpTestRequests.SendAsync(ordinary, batch, HttpMethod.Post,
            "/api/workspace/content/permissions/batch", input, source))
        {
            Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
            var receipt = await saved.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(3, receipt.GetProperty("targets").GetInt32());
            Assert.Equal(3, receipt.GetProperty("changed").GetInt32());
        }
        using (var scope = fixture.Services.CreateScope())
        {
            await Assert.ThrowsAsync<ActionDataAccessException>(() => scope.ServiceProvider.GetRequiredService<IDataScopeObjectAuthorization>()
                .CheckAsync("part.part", targets[1].Id));
            await Assert.ThrowsAsync<ActionDataAccessException>(() => scope.ServiceProvider.GetRequiredService<IDataScopeObjectPermissionQueries>()
                .CatalogAsync("part.part", targets[1].Id));
            await Assert.ThrowsAsync<ActionDataAccessException>(() => scope.ServiceProvider.GetRequiredService<IDataScopeBatchAuthorization>()
                .ReadCurrentAsync([new("part.part", targets[1].Id)]));
        }

        async Task<string> DirectSubjectsAsync()
        {
            using var scope = fixture.Services.CreateScope();
            var ids = targets.Select(target => target.Id).ToArray();
            var rows = await scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>()
                .Set<Dictionary<string, object?>>("DataScopeObjectSubject").AsNoTracking()
                .Where(row => ids.Contains(EF.Property<string>(row, "ObjectId"))).ToArrayAsync();
            return JsonSerializer.Serialize(rows.OrderBy(row => (string)row["EID"]!, StringComparer.Ordinal));
        }
        async Task SaveFolderRuleAsync(Dictionary<string, string> actions)
        {
            const string key = "Auth318MixedFolderRule";
            var rule = new DataScopeRuleDto(key, key, null, "最后Folder管理资格", true, "workspace.folder",
                new("attribute", "EID", "equal", JsonSerializer.SerializeToElement(targets[0].Id)),
                [new(key + "Subject", "all", null, 0, actions)], []);
            using var response = await FeatureHttpTestRequests.SendAsync(admin, Point(fixture, "authorization.dataScope.save"), HttpMethod.Put,
                "/api/authorization/data-scope/rules/" + key, rule);
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }
    }
}
