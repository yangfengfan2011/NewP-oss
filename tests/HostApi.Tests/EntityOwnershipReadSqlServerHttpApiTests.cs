using System.Net;
using System.Text.Json;
using ICP.Platform.Testing;
using Xunit;
using static ICP.Platform.HostApi.Tests.EntityOwnershipBackfillSqlServerHttpApiTests;

namespace ICP.Platform.HostApi.Tests;

public sealed class EntityOwnershipReadSqlServerHttpApiTests
{
    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "纯GET归属状态和待补齐分页验证完整位置，私人或失效来源不能降级为裸ID")]
    public async Task GetStateAndPending_BindQueryScopeAndPaging_RejectPrivateOrStaleSources()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        var position = await ContainerAsync(fixture, admin);
        var targets = new List<Target>();
        var point = Point(fixture, "workspace.folder.initializeOwnership");
        foreach (var (model, kind, action) in Kinds)
        {
            var target = await CreateAsync(fixture, admin, position, model, kind, action, "只读归属-" + model);
            targets.Add(target);
            using var response = await FeatureHttpTestRequests.SendAsync(admin, point, HttpMethod.Get, StatusUrl(target.LinkId, position), dataAccess: OwnershipDeclaration(fixture, "workspace.folder.initializeOwnership", point));
            var state = await ReadOkAsync(response);
            Assert.False(state.GetProperty("missing").GetBoolean());
            Assert.Equal(fixture.ActorId, state.GetProperty("ownerId").GetString());
            if (model == "WorkspaceFolder") continue;
            var slug = model.ToLowerInvariant();
            using var owner = await FeatureHttpTestRequests.SendAsync(admin, Point(fixture, $"{slug}.{slug}.initializeOwnership"), HttpMethod.Get,
                $"/api/{slug}/{(model == "Part" ? "parts" : "documents")}/ownership/{target.Id}/status", dataAccess: OwnershipDeclaration(fixture, $"{slug}.{slug}.initializeOwnership", Point(fixture, $"{slug}.{slug}.initializeOwnership")));
            Assert.False((await ReadOkAsync(owner)).GetProperty("missing").GetBoolean());
        }
        await EntityOwnershipFailureSqlServerHttpApiTests.ClearOwnershipAsync(fixture, targets[0]);
        using var missing = await FeatureHttpTestRequests.SendAsync(admin, point, HttpMethod.Get, StatusUrl(targets[0].LinkId, position), dataAccess: OwnershipDeclaration(fixture, "workspace.folder.initializeOwnership", point));
        Assert.True((await ReadOkAsync(missing)).GetProperty("missing").GetBoolean());
        using var page = await FeatureHttpTestRequests.SendAsync(admin, point, HttpMethod.Get, PendingUrl(position, pageSize: 1), dataAccess: OwnershipDeclaration(fixture, "workspace.folder.initializeOwnership", point));
        var pending = await ReadOkAsync(page);
        Assert.Equal(1, pending.GetProperty("total").GetInt32());
        Assert.Equal(targets[0].LinkId, Assert.Single(pending.GetProperty("items").EnumerateArray()).GetProperty("id").GetString());
        using var bare = await FeatureHttpTestRequests.SendAsync(admin, point, HttpMethod.Get, StatusUrl(targets[0].Id, position), dataAccess: OwnershipDeclaration(fixture, "workspace.folder.initializeOwnership", point));
        Assert.Equal(HttpStatusCode.NotFound, bare.StatusCode);
        foreach (var invalid in new[] { "/api/workspace/ownership/pending?queryScope=%7B%7D", PendingUrl(position) + "&unexpected=true",
            StatusUrl(targets[0].LinkId, position) + "&page=1" })
        {
            using var response = await FeatureHttpTestRequests.SendAsync(admin, point, HttpMethod.Get, invalid, dataAccess: OwnershipDeclaration(fixture, "workspace.folder.initializeOwnership", point));
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "OwnershipGet", "归属读取主体", "ownership-get", "普通读取主体",
            Point(fixture, "workspace.spaces.access"), point);
        using var ordinary = actor.Client;
        using var forbidden = await FeatureHttpTestRequests.SendAsync(ordinary, point, HttpMethod.Get, StatusUrl(targets[0].LinkId, position), dataAccess: OwnershipDeclaration(fixture, "workspace.folder.initializeOwnership", point));
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        using var personal = await FeatureHttpTestRequests.SendAsync(ordinary, Point(fixture, "workspace.spaces.access"), HttpMethod.Post, "/api/workspace/spaces/personal/ensure");
        var root = await ReadOkAsync(personal);
        var privatePosition = new Position(root.GetProperty("spaceId").GetString()!, root.GetProperty("rootFolderId").GetString()!, []);
        using var privateRead = await FeatureHttpTestRequests.SendAsync(admin, point, HttpMethod.Get, PendingUrl(privatePosition), dataAccess: OwnershipDeclaration(fixture, "workspace.folder.initializeOwnership", point));
        Assert.Equal(HttpStatusCode.NotFound, privateRead.StatusCode);
        using var privateState = await FeatureHttpTestRequests.SendAsync(admin, point, HttpMethod.Get, StatusUrl(targets[0].LinkId, privatePosition), dataAccess: OwnershipDeclaration(fixture, "workspace.folder.initializeOwnership", point));
        Assert.Equal(HttpStatusCode.NotFound, privateState.StatusCode);
        using var removed = await FeatureHttpTestRequests.SendAsync(admin, Point(fixture, "workspace.folder.removeSelected"), HttpMethod.Post,
            "/api/workspace/content/references/remove", new { position = position with { LinkIds = [] }, linkIds = position.LinkIds }, WorkspaceClipboardSqlServerHttpApiTests.ClipboardDeclaration(fixture, Point(fixture, "workspace.folder.removeSelected")));
        await ReadOkAsync(removed);
        using var stale = await FeatureHttpTestRequests.SendAsync(admin, point, HttpMethod.Get, PendingUrl(position), dataAccess: OwnershipDeclaration(fixture, "workspace.folder.initializeOwnership", point));
        Assert.Equal(HttpStatusCode.NotFound, stale.StatusCode);
        using var staleState = await FeatureHttpTestRequests.SendAsync(admin, point, HttpMethod.Get, StatusUrl(targets[0].LinkId, position), dataAccess: OwnershipDeclaration(fixture, "workspace.folder.initializeOwnership", point));
        Assert.Equal(HttpStatusCode.NotFound, staleState.StatusCode);
        Assert.Null((await RowAsync(fixture, targets[0]))["OwnerId"]);
    }

    private static string StatusUrl(string linkId, Position position) => $"/api/workspace/content/{linkId}/ownership/status?queryScope="
        + Uri.EscapeDataString(JsonSerializer.Serialize(position, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
}
