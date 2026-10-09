using System.Data.Common;
using System.Net;
using System.Text.Json;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed class WorkspaceObjectMutationSqlServerHttpApiTests
{
    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "真实混合内容编辑、共享引用清理和批量删除")]
    public async Task EditAndDelete_UsesOwningRulesAndDeletesAllReferences()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        var root = await RootAsync(fixture, admin);
        var folder = await CreateAsync(fixture, admin, root, "folders", "create", "folder");
        var other = await CreateAsync(fixture, admin, root, "folders", "create", "other");
        var part = await CreateAsync(fixture, admin, root, "parts", "createPart", "part");
        var document = await CreateAsync(fixture, admin, root, "documents", "createDocument", "document");
        var anotherPart = await CreateAsync(fixture, admin, root, "parts", "createPart", "other-part");
        foreach (var target in new[] { folder, part, document })
        {
            using var beforeScope = fixture.Services.CreateScope();
            var before = await beforeScope.ServiceProvider.GetRequiredService<EfPlatformDbContext>().Set<Dictionary<string, object?>>(target.Model).AsNoTracking()
                .SingleAsync(row => EF.Property<string>(row, "EID") == target.Id);
            using var editRead = await SendAsync(fixture, admin, "edit", HttpMethod.Post, $"/api/workspace/content/{target.Link}/edit-values", new { position = root });
            Assert.True(editRead.StatusCode == HttpStatusCode.OK, await editRead.Content.ReadAsStringAsync());
            var fields = JsonSerializer.Deserialize<JsonElement>(await editRead.Content.ReadAsStringAsync()).GetProperty("fields");
            Assert.False(fields.TryGetProperty("OriginSpaceId", out _));
            var values = new Dictionary<string, object?> { ["ENAME"] = "edited-" + target.Id, ["Description"] = "changed" };
            if (target.Model != "Workspace/WorkspaceFolder") values["ECODE"] = fields.GetProperty("ECODE").GetString();
            using var edited = await SendAsync(fixture, admin, "edit", HttpMethod.Put, $"/api/workspace/content/{target.Link}", new { position = root, values });
            Assert.True(edited.StatusCode == HttpStatusCode.OK, await edited.Content.ReadAsStringAsync());
            using var afterScope = fixture.Services.CreateScope();
            var after = await afterScope.ServiceProvider.GetRequiredService<EfPlatformDbContext>().Set<Dictionary<string, object?>>(target.Model).AsNoTracking()
                .SingleAsync(row => EF.Property<string>(row, "EID") == target.Id);
            Assert.Equal(values["ENAME"], after["ENAME"]);
            Assert.Equal("changed", after["Description"]);
            Assert.Equal(before["CreatedAt"], after["CreatedAt"]);
            Assert.Equal(before["CreatedBy"], after["CreatedBy"]);
            Assert.True((DateTime)after["UpdatedAt"]! >= (DateTime)before["UpdatedAt"]!);
        }
        using var duplicate = await SendAsync(fixture, admin, "edit", HttpMethod.Put, $"/api/workspace/content/{part.Link}",
            new { position = root, values = new { ECODE = "other-part", ENAME = "duplicate" } });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        using var sibling = await SendAsync(fixture, admin, "edit", HttpMethod.Put, $"/api/workspace/content/{folder.Link}", new { position = root, values = new { ENAME = "other" } });
        Assert.Equal(HttpStatusCode.Conflict, sibling.StatusCode);
        using var forged = await SendAsync(fixture, admin, "edit", HttpMethod.Put, $"/api/workspace/content/{folder.Link}", new { position = root, values = new { OriginSpaceId = "foreign" } });
        Assert.Equal(HttpStatusCode.BadRequest, forged.StatusCode);
        using var longDescription = await SendAsync(fixture, admin, "edit", HttpMethod.Put, $"/api/workspace/content/{document.Link}", new { position = root, values = new { Description = new string('x', 501) } });
        Assert.Equal(HttpStatusCode.BadRequest, longDescription.StatusCode);
        var otherPosition = root with { LinkIds = [other.Link] };
        foreach (var target in new[] { folder, part, document })
        {
            using var added = await SendAsync(fixture, admin, "add", HttpMethod.Post, "/api/workspace/content/references",
                new { position = otherPosition, target = new { modelKey = target.Model, id = target.Id } });
            Assert.Equal(HttpStatusCode.OK, added.StatusCode);
        }
        using var wrongPosition = await SendAsync(fixture, admin, "edit", HttpMethod.Put, $"/api/workspace/content/{part.Link}", new { position = otherPosition, values = new { ENAME = "wrong" } });
        Assert.Equal(HttpStatusCode.NotFound, wrongPosition.StatusCode);
        using var deleted = await SendAsync(fixture, admin, "deleteObjects", HttpMethod.Post, "/api/workspace/content/delete",
            new { position = root, linkIds = new[] { folder.Link, part.Link, document.Link } });
        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
        foreach (var target in new[] { folder, part, document })
        {
            Assert.False(await db.Set<Dictionary<string, object?>>(target.Model).AnyAsync(row => EF.Property<string>(row, "EID") == target.Id));
            Assert.False(await db.Set<Dictionary<string, object?>>("Workspace/WorkspaceFolderContent").AnyAsync(row => EF.Property<string>(row, "CEID") == target.Id));
        }
        Assert.True(await db.Set<Dictionary<string, object?>>("Part/Part").AnyAsync(row => EF.Property<string>(row, "EID") == anotherPart.Id));
        using var single = await SendAsync(fixture, admin, "delete", HttpMethod.Delete, $"/api/workspace/content/{anotherPart.Link}", new { position = root });
        Assert.Equal(HttpStatusCode.OK, single.StatusCode);
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "批量拒绝、写失败及权限边界不留下部分删除")]
    public async Task Delete_RejectsNonEmptyAndRollsBackFailureAndDeniedTargets()
    {
        var decisions = new MutationDecisions();
        var fault = new DeleteFault();
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(services =>
        {
            services.AddSingleton<IDataScopeObjectDecisionProvider>(decisions);
            services.AddDbContext<EfPlatformDbContext>((_, options) => options.AddInterceptors(fault));
        }, includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        var root = await RootAsync(fixture, admin);
        var folder = await CreateAsync(fixture, admin, root, "folders", "create", "non-empty");
        var part = await CreateAsync(fixture, admin, root, "parts", "createPart", "keep");
        var otherPart = await CreateAsync(fixture, admin, root, "parts", "createPart", "also-keep");
        using var shared = await SendAsync(fixture, admin, "add", HttpMethod.Post, "/api/workspace/content/references",
            new { position = root with { LinkIds = [folder.Link] }, target = new { modelKey = part.Model, id = part.Id } });
        Assert.Equal(HttpStatusCode.OK, shared.StatusCode);
        var body = new { position = root, linkIds = new[] { part.Link, folder.Link } };
        using var nonEmpty = await SendAsync(fixture, admin, "deleteObjects", HttpMethod.Post, "/api/workspace/content/delete", body);
        Assert.Equal(HttpStatusCode.Conflict, nonEmpty.StatusCode);
        Assert.Contains("FolderNotEmpty", await nonEmpty.Content.ReadAsStringAsync());
        decisions.DeniedId = part.Id;
        using var denied = await SendAsync(fixture, admin, "deleteObjects", HttpMethod.Post, "/api/workspace/content/delete", new { position = root, linkIds = new[] { part.Link } });
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using var editDenied = await SendAsync(fixture, admin, "edit", HttpMethod.Put, $"/api/workspace/content/{part.Link}", new { position = root, values = new { ENAME = "denied" } });
        Assert.Equal(HttpStatusCode.Forbidden, editDenied.StatusCode);
        decisions.DeniedId = null;
        using var wrongFeature = await SendAsync(fixture, admin, "create", HttpMethod.Post, "/api/workspace/content/delete", body);
        Assert.Equal(HttpStatusCode.Forbidden, wrongFeature.StatusCode);
        foreach (var ids in new[] { Array.Empty<string>(), new[] { part.Link, part.Link }, Enumerable.Range(0, 101).Select(index => "L" + index).ToArray() })
        {
            using var invalid = await SendAsync(fixture, admin, "deleteObjects", HttpMethod.Post, "/api/workspace/content/delete", new { position = root, linkIds = ids });
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        }
        fault.Enabled = true;
        using var failed = await SendAsync(fixture, admin, "deleteObjects", HttpMethod.Post, "/api/workspace/content/delete", new { position = root, linkIds = new[] { part.Link, otherPart.Link } });
        Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
        fault.Enabled = false;
        Assert.Equal(2, fault.EntityDeletes);
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
        Assert.True(await db.Set<Dictionary<string, object?>>(otherPart.Model).AnyAsync(row => EF.Property<string>(row, "EID") == otherPart.Id));
        Assert.True(await db.Set<Dictionary<string, object?>>(part.Model).AnyAsync(row => EF.Property<string>(row, "EID") == part.Id));
        Assert.True(await db.Set<Dictionary<string, object?>>("Workspace/WorkspaceFolderContent").AnyAsync(row => EF.Property<string>(row, "LID") == part.Link));
    }

    private sealed record Position(string SpaceId, string RootFolderId, string[] LinkIds);
    private sealed record Target(string Model, string Id, string Link);
    private static FeaturePoint Point(BusinessAuditHostFixture fixture, string action) => Assert.Single(fixture.FeatureCatalog, point => point.BehaviorKey == "workspace.folder." + (action == "edit" ? "editObject" : action == "delete" ? "deleteObject" : action));
    private static Task<HttpResponseMessage> SendAsync(BusinessAuditHostFixture fixture, HttpClient client, string action, HttpMethod method, string path, object body) =>
        FeatureHttpTestRequests.SendAsync(client, Point(fixture, action), method, path, body);
    private static async Task<Position> RootAsync(BusinessAuditHostFixture fixture, HttpClient admin)
    {
        using var response = await FeatureHttpTestRequests.SendAsync(admin, Assert.Single(fixture.FeatureCatalog, point => point.BehaviorKey == "workspace.spaces.access"), HttpMethod.Post, "/api/workspace/spaces/public/initialize");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var root = JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync());
        return new(root.GetProperty("spaceId").GetString()!, root.GetProperty("rootFolderId").GetString()!, []);
    }
    private static async Task<Target> CreateAsync(BusinessAuditHostFixture fixture, HttpClient admin, Position position, string kind, string action, string name)
    {
        var values = new Dictionary<string, object?> { ["ENAME"] = name };
        if (kind != "folders") values["ECODE"] = name;
        using var response = await SendAsync(fixture, admin, action, HttpMethod.Post, "/api/workspace/content/" + kind, new { position, values });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var model = kind == "folders" ? "Workspace/WorkspaceFolder" : kind == "parts" ? "Part/Part" : "Document/Document";
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
        var entity = await db.Set<Dictionary<string, object?>>(model).SingleAsync(row => EF.Property<string>(row, "ENAME") == name);
        var id = (string)entity["EID"]!;
        var link = await db.Set<Dictionary<string, object?>>("Workspace/WorkspaceFolderContent").SingleAsync(row => EF.Property<string>(row, "CEID") == id);
        return new(model, id, (string)link["LID"]!);
    }
    private sealed class MutationDecisions : IDataScopeObjectDecisionProvider
    {
        public string? DeniedId { get; set; }
        public Task<bool> IsAllowedAsync(string resourceKey, string objectId, string actionKey, CancellationToken cancellationToken = default) => Task.FromResult(objectId != DeniedId || actionKey == "read");
        public Task<bool> IsCandidateAllowedAsync(string resourceKey, IReadOnlyDictionary<string, object?> candidate, string actionKey, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }
    private sealed class DeleteFault : DbCommandInterceptor
    {
        public bool Enabled { get; set; }
        public int EntityDeletes { get; private set; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (Enabled && command.CommandText.Contains("DELETE", StringComparison.Ordinal) && command.CommandText.Contains("[Part]", StringComparison.Ordinal) && ++EntityDeletes == 2) throw new InvalidOperationException("WORKSPACE_DELETE_FAILURE");
            return ValueTask.FromResult(result);
        }
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Enabled && command.CommandText.Contains("DELETE", StringComparison.Ordinal) && command.CommandText.Contains("[Part]", StringComparison.Ordinal) && ++EntityDeletes == 2) throw new InvalidOperationException("WORKSPACE_DELETE_FAILURE");
            return ValueTask.FromResult(result);
        }
    }
}
