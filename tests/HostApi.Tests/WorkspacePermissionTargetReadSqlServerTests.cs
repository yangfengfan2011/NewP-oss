using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Modules.Authorization.DTOs;
using ICP.Platform.Modules.Authorization.Services;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed class WorkspacePermissionTargetReadSqlServerTests
{
    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "权限来源保持Link读取404之后独立data-read403，完整read角色不增加原读取")]
    public async Task PermissionSource_PreservesTheTwoOriginalReadsAndTheirDifferentFailures()
    {
        var state = new ReadState();
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(services =>
            services.AddScoped<IDataScopeObjectDecisionProvider>(provider => new SourceDecisions(
                (DataScopeReadPlans)provider.GetRequiredService<IDataScopeReadPlanProvider>(), state)), includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        var entry = Assert.Single(fixture.FeatureCatalog, point => point.BehaviorKey == "workspace.spaces.access");
        using var initialized = await FeatureHttpTestRequests.SendAsync(admin, entry, HttpMethod.Post, "/api/workspace/spaces/public/initialize");
        Assert.Equal(HttpStatusCode.OK, initialized.StatusCode);
        var root = await initialized.Content.ReadFromJsonAsync<JsonElement>();
        var position = new { spaceId = root.GetProperty("spaceId").GetString(), rootFolderId = root.GetProperty("rootFolderId").GetString(), linkIds = Array.Empty<string>() };
        var create = Assert.Single(fixture.FeatureCatalog, point => point.BehaviorKey == "workspace.folder.create" && point.ActionScope == "list.pageActions");
        using var created = await FeatureHttpTestRequests.SendAsync(admin, create, HttpMethod.Post, "/api/workspace/content/folders",
            new { position, values = new { ENAME = "完整来源读取" } }, new FeatureHttpTestRequests(fixture).Declaration("workspace.folder.create", create));
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;
        using var scope = fixture.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
        var linkId = await db.Set<Dictionary<string, object?>>("WorkspaceFolderContent").AsNoTracking()
            .Where(row => EF.Property<string>(row, "CEID") == id).Select(row => EF.Property<string>(row, "LID")).SingleAsync();
        var view = Assert.Single(fixture.Services.GetRequiredService<CanonicalModulePlan>().ActionDataAccessCatalog,
            item => item.Execution == "workspace.content.authorization.view" && item.Surface == "read");
        var path = "/api/workspace/content/" + linkId + "/permissions/catalog?queryScope=" + Uri.EscapeDataString(JsonSerializer.Serialize(position));
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "SourceRead", "来源读取", "source-read", "来源读取", entry);
        using var ordinary = actor.Client;
        using (var success = await FeatureHttpTestRequests.SendAsync(ordinary, entry, HttpMethod.Get, path, dataAccess: view))
            Assert.Equal(HttpStatusCode.OK, success.StatusCode);
        // 只在原独立data-read判定点注入拒绝；原Link SQL仍必须完整执行，不能把读过解释为已允许。
        state.Target = id; state.Deny = true;
        using (var denied = await FeatureHttpTestRequests.SendAsync(ordinary, entry, HttpMethod.Get, path, dataAccess: view))
        {
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            Assert.Equal("Workspace.ObjectWriteDenied", (await denied.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        }
        Assert.Equal(1, state.Calls);
        var rule = new DataScopeRuleDto("SourceUnreadable", "SourceUnreadable", null, "SourceUnreadable", true, "workspace.folder",
            new("attribute", "EID", "equal", JsonSerializer.SerializeToElement(id)),
            [new("SourceUnreadableSubject", "all", null, 0, new Dictionary<string, string> { ["read"] = "deny" })], []);
        using var saved = await new FeatureHttpTestRequests(fixture).SendButtonAsync(admin, Assert.Single(fixture.FeatureCatalog, point => point.BehaviorKey == "authorization.dataScope.save"),
            HttpMethod.Put, "/api/authorization/data-scope/rules/SourceUnreadable", rule);
        Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);
        state.Calls = 0;
        using (var hidden = await FeatureHttpTestRequests.SendAsync(ordinary, entry, HttpMethod.Get, path, dataAccess: view))
            Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        Assert.Equal(0, state.Calls);
    }

    private sealed class ReadState { internal string? Target; internal bool Deny; internal int Calls; }
    private sealed class SourceDecisions(IDataScopeObjectDecisionProvider inner, ReadState state) : IDataScopeObjectDecisionProvider
    {
        public Task<bool> IsAllowedAsync(string resourceKey, string objectId, string actionKey, CancellationToken token = default)
        {
            if (resourceKey == "workspace.folder" && objectId == state.Target && actionKey == "read")
            { state.Calls++; if (state.Deny) return Task.FromResult(false); }
            return inner.IsAllowedAsync(resourceKey, objectId, actionKey, token);
        }
        public Task<bool> IsCandidateAllowedAsync(string resourceKey, IReadOnlyDictionary<string, object?> candidate, string actionKey, CancellationToken token = default) =>
            inner.IsCandidateAllowedAsync(resourceKey, candidate, actionKey, token);
    }
}