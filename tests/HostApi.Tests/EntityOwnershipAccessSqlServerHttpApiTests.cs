using System.Net;
using System.Text.Json;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static ICP.Platform.HostApi.Tests.EntityOwnershipBackfillSqlServerHttpApiTests;

namespace ICP.Platform.HostApi.Tests;

public sealed class EntityOwnershipAccessSqlServerHttpApiTests
{
    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "批次端点SQL读取交集和每对象edit拒绝发生在任何归属写入前")]
    public async Task Batch_IntersectsEveryEndpointReadScopeAndRetainsPerObjectEditDecisions()
    {
        var access = new BatchAccess();
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(services =>
        {
            services.AddSingleton<IDataScopeReadPlanProvider>(access);
            services.AddSingleton<IDataScopeReadableIds>(access);
            services.AddSingleton<IDataScopeObjectDecisionProvider>(access);
        }, includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        var position = await ContainerAsync(fixture, admin);
        var targets = new List<Target>();
        foreach (var (model, kind, action) in Kinds)
        {
            var target = await CreateAsync(fixture, admin, position, model, kind, action, "批次资格-" + model);
            await EntityOwnershipFailureSqlServerHttpApiTests.ClearOwnershipAsync(fixture, target);
            targets.Add(target);
        }
        var point = Point(fixture, "workspace.folder.initializeOwnership");
        var input = new { position, linkIds = targets.Select(target => target.LinkId).ToArray() };
        access.HiddenId = targets[1].Id;
        using var pending = await FeatureHttpTestRequests.SendAsync(admin, point, HttpMethod.Get, PendingUrl(position));
        Assert.Equal(2, (await ReadOkAsync(pending)).GetProperty("total").GetInt32());
        using var unreadable = await FeatureHttpTestRequests.SendAsync(admin, point, HttpMethod.Post, "/api/workspace/ownership/initialize", input);
        Assert.Equal(HttpStatusCode.NotFound, unreadable.StatusCode);
        await AssertMissingAsync();
        access.HiddenId = null;
        access.EditDeniedId = targets[2].Id;
        using var denied = await FeatureHttpTestRequests.SendAsync(admin, point, HttpMethod.Post, "/api/workspace/ownership/initialize", input);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        await AssertMissingAsync();
        Assert.Contains(targets[2].Id, access.EditedIds);
        access.EditDeniedId = null;
        using var allowed = await FeatureHttpTestRequests.SendAsync(admin, point, HttpMethod.Post, "/api/workspace/ownership/initialize", input);
        Assert.Equal(3, (await ReadOkAsync(allowed)).GetProperty("changed").GetInt32());

        async Task AssertMissingAsync()
        {
            foreach (var target in targets) Assert.Null((await RowAsync(fixture, target))["OwnerId"]);
            using var scope = fixture.Services.CreateScope();
            Assert.False(await scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>().Set<Dictionary<string, object?>>("BusinessChange")
                .AnyAsync(row => EF.Property<string>(row, "EventType") == "entity.ownership.backfilled"));
        }
    }

    // 只替换稳定裁决契约，实际Link端点SQL、来源约束、批次写入和审计仍走正式实现。
    private sealed class BatchAccess : IDataScopeReadPlanProvider, IDataScopeObjectDecisionProvider, IDataScopeReadableIds
    {
        internal string? HiddenId { get; set; }
        internal string? EditDeniedId { get; set; }
        internal List<string> EditedIds { get; } = [];
        public Task<IReadOnlySet<string>> GetReadableIdsAsync(string resourceKey, IReadOnlyCollection<string> objectIds, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlySet<string>>(objectIds.Where(id => id != HiddenId).ToHashSet(StringComparer.Ordinal));
        public Task<DataScopeReadPlan> GetReadPlanAsync(string resourceKey, CancellationToken cancellationToken = default)
        {
            var hidden = HiddenId;
            return Task.FromResult(resourceKey is "workspace.folder" or "part.part" or "document.document"
                ? new DataScopeReadPlan(row => EF.Property<string>(row, "EID") != hidden) : new(null));
        }
        public Task<bool> IsAllowedAsync(string resourceKey, string objectId, string actionKey, CancellationToken cancellationToken = default)
        {
            if (actionKey == "edit") EditedIds.Add(objectId);
            return Task.FromResult(actionKey != "edit" || objectId != EditDeniedId);
        }
        public Task<bool> IsCandidateAllowedAsync(string resourceKey, IReadOnlyDictionary<string, object?> candidate, string actionKey, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "真实普通创建账号归属及私人来源、管理员身份、失效LID和裸EID边界")]
    public async Task ThreeModels_PrivateSourcesAndOrdinaryFeatureHoldersCannotBypassOwnershipMaintenance()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        var publicPosition = await ContainerAsync(fixture, admin);
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "OwnershipActor", "归属普通主体", "ownership-actor", "真实归属人",
            fixture.FeatureCatalog.Where(point => point.ModuleId is "Workspace" or "Part" or "Document").ToArray());
        using var ordinary = actor.Client;
        using var ensured = await FeatureHttpTestRequests.SendAsync(ordinary, Point(fixture, "workspace.spaces.access"), HttpMethod.Post, "/api/workspace/spaces/personal/ensure");
        var personal = await ReadOkAsync(ensured);
        var position = new Position(personal.GetProperty("spaceId").GetString()!, personal.GetProperty("rootFolderId").GetString()!, []);
        var container = await CreateAsync(fixture, ordinary, position, "WorkspaceFolder", "folders", "create", "私人维护目录");
        position = position with { LinkIds = [container.LinkId] };
        var targets = new List<Target>();
        foreach (var (model, kind, action) in Kinds)
        {
            var target = await CreateAsync(fixture, ordinary, position, model, kind, action, "私人归属-" + model);
            var row = await RowAsync(fixture, target);
            Assert.Equal(actor.AccountId, row["OwnerId"]);
            Assert.NotEqual(fixture.ActorId, row["OwnerId"]);
            Assert.NotNull(row["OwnerDepartmentId"]);
            await EntityOwnershipFailureSqlServerHttpApiTests.ClearOwnershipAsync(fixture, target);
            targets.Add(target);
        }
        // 同一普通账号已持有正式维护功能；功能授权不能将普通主体提升为管理员。
        var maintenance = Point(fixture, "workspace.folder.initializeOwnership");
        foreach (var target in targets)
        {
            using var forbidden = await FeatureHttpTestRequests.SendAsync(ordinary, maintenance, HttpMethod.Post,
                $"/api/workspace/content/{target.LinkId}/ownership", new { position, accountId = fixture.ActorId });
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
            using var privateSource = await FeatureHttpTestRequests.SendAsync(admin, maintenance, HttpMethod.Post,
                $"/api/workspace/content/{target.LinkId}/ownership", new { position });
            Assert.Equal(HttpStatusCode.NotFound, privateSource.StatusCode);
            Assert.DoesNotContain(target.Name, await privateSource.Content.ReadAsStringAsync(), StringComparison.Ordinal);
            using var fakePublicSource = await FeatureHttpTestRequests.SendAsync(admin, maintenance, HttpMethod.Post,
                $"/api/workspace/content/{target.LinkId}/ownership", new { position = publicPosition });
            Assert.Equal(HttpStatusCode.NotFound, fakePublicSource.StatusCode);
            using var bareId = await FeatureHttpTestRequests.SendAsync(admin, maintenance, HttpMethod.Post,
                $"/api/workspace/content/{target.Id}/ownership", new { position = publicPosition });
            Assert.Equal(HttpStatusCode.NotFound, bareId.StatusCode);
            if (target.Model == "WorkspaceFolder") continue;
            var slug = target.Model.ToLowerInvariant();
            var route = $"/api/{slug}/{(target.Model == "Part" ? "parts" : "documents")}/ownership";
            var point = Point(fixture, $"{slug}.{slug}.initializeOwnership");
            using var ownerForbidden = await FeatureHttpTestRequests.SendAsync(ordinary, point, HttpMethod.Post, $"{route}/{target.Id}", new { });
            Assert.Equal(HttpStatusCode.Forbidden, ownerForbidden.StatusCode);
        }
        var visible = await CreateAsync(fixture, admin, publicPosition, "WorkspaceFolder", "folders", "create", "已移除来源");
        await EntityOwnershipFailureSqlServerHttpApiTests.ClearOwnershipAsync(fixture, visible);
        using var removed = await FeatureHttpTestRequests.SendAsync(admin, Point(fixture, "workspace.folder.removeSelected"), HttpMethod.Post,
            "/api/workspace/content/references/remove", new { position = publicPosition, linkIds = new[] { visible.LinkId } });
        await ReadOkAsync(removed);
        using var stale = await FeatureHttpTestRequests.SendAsync(admin, maintenance, HttpMethod.Post,
            $"/api/workspace/content/{visible.LinkId}/ownership", new { position = publicPosition });
        Assert.Equal(HttpStatusCode.NotFound, stale.StatusCode);
        foreach (var target in targets.Append(visible))
        {
            var current = await RowAsync(fixture, target);
            Assert.Null(current["OwnerId"]);
            Assert.Null(current["OwnerDepartmentId"]);
        }
        using var scope = fixture.Services.CreateScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>().Set<Dictionary<string, object?>>("BusinessChange")
            .AnyAsync(row => EF.Property<string>(row, "EventType") == "entity.ownership.backfilled"));
    }
}
