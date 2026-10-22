using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using ICP.Platform.Crud.LinkQueries;
using ICP.Platform.Crud.Models;
using Microsoft.AspNetCore.Http;
using System.Text.Json;
using ICP.Platform.Modules.Authorization.DTOs;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static ICP.Platform.HostApi.Tests.EntityOwnershipBackfillSqlServerHttpApiTests;

namespace ICP.Platform.HostApi.Tests;

public sealed class DataScopeBatchCollectionSqlServerTests
{
    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "a real shared content graph supplies every readable candidate, safe unmanaged feedback and complete-set filters")]
    public async Task Collection_TraversesTheActualGraphAndFiltersTheCompleteReadableSet()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        await InitializeAsync(fixture, admin);
        var container = await ContainerAsync(fixture, admin);
        var root = await CreateAsync(fixture, admin, container, "WorkspaceFolder", "folders", "create", "BatchGraph297");
        var rootPosition = container with { LinkIds = [.. container.LinkIds, root.LinkId] };
        var hidden = await CreateAsync(fixture, admin, rootPosition, "WorkspaceFolder", "folders", "create", "DoNotDiscloseFolder297");
        var visible = await CreateAsync(fixture, admin, rootPosition, "WorkspaceFolder", "folders", "create", "VisibleGraph297");
        var hiddenPosition = rootPosition with { LinkIds = [.. rootPosition.LinkIds, hidden.LinkId] };
        var visiblePosition = rootPosition with { LinkIds = [.. rootPosition.LinkIds, visible.LinkId] };
        var shared = await CreateAsync(fixture, admin, hiddenPosition, "Part", "parts", "createPart", "SharedGraphPart297");
        var document = await CreateAsync(fixture, admin, visiblePosition, "Document", "documents", "createDocument", "VisibleGraphDoc297");
        var hiddenOnly = await CreateAsync(fixture, admin, hiddenPosition, "Document", "documents", "createDocument", "DoNotDiscloseDoc297");
        var unmanaged = await CreateAsync(fixture, admin, rootPosition, "Part", "parts", "createPart", "UnmanagedGraphPart297");
        foreach (var position in new[] { visiblePosition, rootPosition })
        {
            using var added = await FeatureHttpTestRequests.SendAsync(admin, Point(fixture, "workspace.folder.add"), HttpMethod.Post,
                "/api/workspace/content/references", new { position, target = new { modelKey = "Part", id = shared.Id } });
            await ReadOkAsync(added);
        }
        var manage = fixture.FeatureCatalog.First(point => point.BehaviorKey == "workspace.folder.authorize"
            && point.Key == "authorizeContent" && point.ActionScope == "list.rowActions");
        var entry = Point(fixture, "workspace.spaces.access");
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "Graph297", "Batch graph", "graph297", "Batch graph", entry, manage);
        using var ordinary = actor.Client;
        foreach (var (target, position, resource, action) in new[] {
            (hidden, rootPosition, "workspace.folder", "read"), (unmanaged, rootPosition, "part.part", "authorize") })
        {
            var path = "/api/workspace/content/" + target.LinkId + "/permissions?queryScope="
                + Uri.EscapeDataString(JsonSerializer.Serialize(position, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            using var saved = await FeatureHttpTestRequests.SendAsync(admin, manage, HttpMethod.Put, path,
                new DataScopeObjectDto(resource, target.Id, [new(target.Id + "-deny", "user", actor.AccountId, 10,
                    new Dictionary<string, string> { [action] = "deny" })]),
                OwnedResourceReadHttpRequests.Button(fixture, manage, "workspace.folder.authorize"));
            Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);
        }
        // Only this first page is loaded; no descendant page or expanded tree node prepares the batch.
        string anchor;
        using (var scope = fixture.Services.CreateScope())
            anchor = await scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>()
                .Set<Dictionary<string, object?>>("WorkspaceFolderContent").AsNoTracking()
                .Where(row => EF.Property<string>(row, "LID") == root.LinkId)
                .Select(row => EF.Property<string>(row, "PEID")).SingleAsync();
        var queryScope = new Dictionary<string, string?> { ["spaceId"] = container.SpaceId,
            ["rootFolderId"] = container.RootFolderId, ["linkIds"] = JsonSerializer.Serialize(container.LinkIds) };
        var firstPage = await FeatureHttpTestRequests.ReadAsync(ordinary, entry,
            "/api/links/workspace.content?direction=forward&anchorModelKey=WorkspaceFolder&anchorId=" + anchor
            + "&queryScope=" + Uri.EscapeDataString(JsonSerializer.Serialize(queryScope)) + "&page=1&pageSize=1");
        Assert.Single(firstPage.GetProperty("items").EnumerateArray());
        object Input(object? filters = null) => new { includeDescendants = true,
            sources = new[] { new { position = container, linkIds = new[] { root.LinkId } } }, filters };
        var all = await PrepareAsync(Input());
        var items = all.GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(5, all.GetProperty("total").GetInt32());
        Assert.Equal(5, items.Length);
        Assert.Equal(new[] { root.Id, visible.Id, shared.Id, document.Id, unmanaged.Id }.Order(),
            items.Select(item => item.GetProperty("objectId").GetString()!).Order());
        Assert.Single(items.Where(item => item.GetProperty("objectId").GetString() == shared.Id));
        Assert.False(items.Single(item => item.GetProperty("objectId").GetString() == unmanaged.Id).GetProperty("manageable").GetBoolean());
        var raw = all.GetRawText();
        foreach (var secret in new[] { hidden.Id, hidden.Name, hiddenOnly.Id, hiddenOnly.Name, shared.LinkId }) Assert.DoesNotContain(secret, raw);
        Assert.Equal(new[] { "document.document", "part.part", "workspace.folder" }, all.GetProperty("catalogs")
            .EnumerateArray().Select(item => item.GetProperty("resourceKey").GetString()!).Order().ToArray());
        var parts = await PrepareAsync(Input(new { resourceKeys = new[] { "part.part" }, createdBy = fixture.ActorId, ownerId = fixture.ActorId }));
        Assert.Equal(2, parts.GetProperty("total").GetInt32());
        Assert.All(parts.GetProperty("items").EnumerateArray(), item => Assert.Equal("part.part", item.GetProperty("resourceKey").GetString()));
        var otherCreator = await PrepareAsync(Input(new { createdBy = actor.AccountId }));
        Assert.Equal(0, otherCreator.GetProperty("total").GetInt32());
        var otherOwner = await PrepareAsync(Input(new { ownerId = actor.AccountId }));
        Assert.Equal(0, otherOwner.GetProperty("total").GetInt32());
        // Returned locators are valid source appearances, never the inaccessible occurrence of the shared object.
        var sharedItem = items.Single(item => item.GetProperty("objectId").GetString() == shared.Id);
        Assert.NotEqual(shared.LinkId, sharedItem.GetProperty("source").GetProperty("linkIds")[0].GetString());
        using var forbidden = await FeatureHttpTestRequests.SendAsync(ordinary, manage, HttpMethod.Post,
            "/api/workspace/content/permissions/batch/prepare", new { includeDescendants = true,
                sources = new[] { new { position = rootPosition, linkIds = new[] { hidden.LinkId } } } }, dataAccess: InputRead(fixture, manage));
        Assert.Equal(HttpStatusCode.NotFound, forbidden.StatusCode);

        async Task<JsonElement> PrepareAsync(object input)
        {
            using var response = await FeatureHttpTestRequests.SendAsync(ordinary, manage, HttpMethod.Post,
                "/api/workspace/content/permissions/batch/prepare", input, dataAccess: InputRead(fixture, manage));
            return await ReadOkAsync(response);
        }
    }
    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "selected Link reads keep source/private/read intersections, exact Count/page and an explicitly empty set")]
    public async Task SelectedLinkQuery_IntersectsActualReadAndSource_WithExactCountAndPages()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        await InitializeAsync(fixture, admin);
        var position = await ContainerAsync(fixture, admin);
        var part = await CreateAsync(fixture, admin, position, "Part", "parts", "createPart", "SelectedPart297");
        var document = await CreateAsync(fixture, admin, position, "Document", "documents", "createDocument", "SelectedDoc297");
        var hidden = await CreateAsync(fixture, admin, position, "Part", "parts", "createPart", "SelectedHidden297");
        var manage = fixture.FeatureCatalog.First(point => point.BehaviorKey == "workspace.folder.authorize"
            && point.Key == "authorizeContent" && point.ActionScope == "list.rowActions");
        var entry = Point(fixture, "workspace.spaces.access");
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "Selected297", "Selected reader", "selected297", "Selected reader", entry);
        using var ordinary = actor.Client;
        using (var denied = await FeatureHttpTestRequests.SendAsync(admin, manage, HttpMethod.Put,
            "/api/workspace/content/" + hidden.LinkId + "/permissions?queryScope="
                + Uri.EscapeDataString(JsonSerializer.Serialize(position, new JsonSerializerOptions(JsonSerializerDefaults.Web))),
            new DataScopeObjectDto("part.part", hidden.Id, [new("SelectedHidden297Deny", "user", actor.AccountId, 1,
                new Dictionary<string, string> { ["read"] = "deny" })]),
            OwnedResourceReadHttpRequests.Button(fixture, manage, "workspace.folder.authorize"))) Assert.Equal(HttpStatusCode.NoContent, denied.StatusCode);
        using var personalResponse = await FeatureHttpTestRequests.SendAsync(admin, entry, HttpMethod.Post, "/api/workspace/spaces/personal/ensure");
        var personalRoot = await ReadOkAsync(personalResponse);
        var personal = new Position(personalRoot.GetProperty("spaceId").GetString()!, personalRoot.GetProperty("rootFolderId").GetString()!, []);
        var personalFolder = await CreateAsync(fixture, admin, personal, "WorkspaceFolder", "folders", "create", "SelectedPrivateFolder297");
        personal = personal with { LinkIds = [personalFolder.LinkId] };
        var privatePart = await CreateAsync(fixture, admin, personal, "Part", "parts", "createPart", "SelectedPrivatePart297");
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
        var parent = await db.Set<Dictionary<string, object?>>("WorkspaceFolderContent").AsNoTracking()
            .Where(row => EF.Property<string>(row, "LID") == part.LinkId).Select(row => EF.Property<string>(row, "PEID")).SingleAsync();
        var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        var previous = accessor.HttpContext;
        accessor.HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, actor.AccountId), new Claim(ClaimTypes.Name, "selected297")], "actual-test-account")) };
        try
        {
            var reader = scope.ServiceProvider.GetRequiredService<LinkQueryReader>();
            var query = new LinkQueryRequest("forward", new("WorkspaceFolder", parent),
                new(PageSize: 1, Fields: ["LID", "Name"], QueryScope: Scope(position)));
            string[] selected = [part.LinkId, document.LinkId, hidden.LinkId, privatePart.LinkId, "missing-link-297"];
            var first = await reader.QuerySelectedAsync("workspace.content", query, selected);
            var second = await reader.QuerySelectedAsync("workspace.content", query with { Query = query.Query with { Page = 2 } }, selected);
            Assert.Equal(2, first.Total);
            Assert.Equal(2, second.Total);
            Assert.Single(first.Items);
            Assert.Single(second.Items);
            Assert.Equal(new[] { part.Id, document.Id }.Order(), first.Items.Concat(second.Items).Select(row => row.AssociatedObject.Id).Order());
            var empty = await reader.QuerySelectedAsync("workspace.content", query, []);
            Assert.Equal(0, empty.Total);
            Assert.Empty(empty.Items);
            var legacy = await reader.QueryAsync("workspace.content", query);
            Assert.Equal(2, legacy.Total);
            Assert.Single(legacy.Items);
            var wrongAnchor = await Assert.ThrowsAsync<LinkQueryException>(() => reader.QuerySelectedAsync("workspace.content",
                query with { Anchor = new("WorkspaceFolder", personalFolder.Id) }, selected));
            Assert.Equal(404, wrongAnchor.Status);
            var privateSource = await Assert.ThrowsAsync<LinkQueryException>(() => reader.QuerySelectedAsync("workspace.content",
                query with { Anchor = new("WorkspaceFolder", personalFolder.Id), Query = query.Query with { QueryScope = Scope(personal) } }, [privatePart.LinkId]));
            Assert.Equal(404, privateSource.Status);
        }
        finally { accessor.HttpContext = previous; }
    }

    private static Dictionary<string, string?> Scope(Position position) => new() { ["spaceId"] = position.SpaceId,
        ["rootFolderId"] = position.RootFolderId, ["linkIds"] = JsonSerializer.Serialize(position.LinkIds) };

    private static ICP.Platform.Kernel.ModulePlanning.ActionDataAccessRequirement InputRead(BusinessAuditHostFixture fixture, ICP.Platform.Kernel.ModulePlanning.FeaturePoint point) =>
        OwnedResourceReadHttpRequests.ForExecution(fixture, point, "workspace.content.authorization.view", "authorization.collection.view",
            "workspace.folder", "part.part", "document.document");

}
