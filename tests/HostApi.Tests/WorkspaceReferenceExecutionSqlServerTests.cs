using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed class WorkspaceReferenceExecutionSqlServerTests
{
    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "纯添加引用API拒绝入口声明串用，不新增UI来源或复制动作")]
    public async Task AddReferenceApi_RejectsForeignEntryBeforeWriting()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        var entry = Point(fixture, "workspace.spaces.access");
        using var initialized = await FeatureHttpTestRequests.SendAsync(admin, entry, HttpMethod.Post, "/api/workspace/spaces/public/initialize");
        Assert.Equal(HttpStatusCode.OK, initialized.StatusCode);
        var root = await initialized.Content.ReadFromJsonAsync<JsonElement>();
        var rootPosition = new { spaceId = root.GetProperty("spaceId").GetString(), rootFolderId = root.GetProperty("rootFolderId").GetString(), linkIds = Array.Empty<string>() };
        var create = Point(fixture, "workspace.folder.create");
        var declaration = new FeatureHttpTestRequests(fixture).Declaration("workspace.folder.create", create);
        async Task<string> Folder(string name)
        {
            using var response = await FeatureHttpTestRequests.SendAsync(admin, create, HttpMethod.Post, "/api/workspace/content/folders",
                new { position = rootPosition, values = new { ENAME = name } }, declaration);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;
        }
        var parent = await Folder("固定API父");
        var target = await Folder("固定API目标");
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
        var links = db.Set<Dictionary<string, object?>>("WorkspaceFolderContent");
        var parentLink = await links.AsNoTracking().Where(row => EF.Property<string>(row, "CEID") == parent)
            .Select(row => EF.Property<string>(row, "LID")).SingleAsync();
        var before = await links.CountAsync();
        var position = new { rootPosition.spaceId, rootPosition.rootFolderId, linkIds = new[] { parentLink } };
        // 来源是实际存在的create声明，但此固定API不开放客户端声明选择。
        using var crossed = await FeatureHttpTestRequests.SendAsync(admin, Point(fixture, "workspace.folder.add"), HttpMethod.Post,
            "/api/workspace/content/references", new { position, target = new { modelKey = "WorkspaceFolder", id = target } }, declaration);
        Assert.Equal(HttpStatusCode.BadRequest, crossed.StatusCode);
        Assert.Equal(before, await links.CountAsync());
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "无头固定API保原功能门、父edit与目标read，不要求引用copy或目标edit")]
    public async Task AddReferenceApi_UsesItsOriginalFeatureParentAndTargetRequirements()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        var entry = Point(fixture, "workspace.spaces.access"); var add = Point(fixture, "workspace.folder.add");
        using var initialized = await FeatureHttpTestRequests.SendAsync(admin, entry, HttpMethod.Post, "/api/workspace/spaces/public/initialize");
        Assert.Equal(HttpStatusCode.OK, initialized.StatusCode);
        var root = await initialized.Content.ReadFromJsonAsync<JsonElement>();
        var rootPosition = new { spaceId = root.GetProperty("spaceId").GetString(), rootFolderId = root.GetProperty("rootFolderId").GetString(), linkIds = Array.Empty<string>() };
        var create = Point(fixture, "workspace.folder.create");
        var declaration = new FeatureHttpTestRequests(fixture).Declaration("workspace.folder.create", create);
        async Task<string> Folder(string name)
        {
            using var response = await FeatureHttpTestRequests.SendAsync(admin, create, HttpMethod.Post, "/api/workspace/content/folders",
                new { position = rootPosition, values = new { ENAME = name } }, declaration);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;
        }
        var parent = await Folder("API权限父"); var target = await Folder("API权限目标");
        using var scope = fixture.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
        var parentLink = await db.Set<Dictionary<string, object?>>("WorkspaceFolderContent").AsNoTracking()
            .Where(row => EF.Property<string>(row, "CEID") == parent).Select(row => EF.Property<string>(row, "LID")).SingleAsync();
        var position = new { rootPosition.spaceId, rootPosition.rootFolderId, linkIds = new[] { parentLink } };
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "ReferenceApi", "引用API",
            "reference-api", "引用API", entry, add);
        using var ordinary = actor.Client;
        async Task Rule(string id, string action, string effect)
        {
            var key = "Reference" + action;
            var rule = new ICP.Platform.Modules.Authorization.DTOs.DataScopeRuleDto(key, key, null, key, true, "workspace.folder",
                new("attribute", "EID", "equal", JsonSerializer.SerializeToElement(id)),
                [new(key + "Subject", "all", null, 0, new Dictionary<string, string> { [action] = effect })], []);
            using var saved = await new FeatureHttpTestRequests(fixture).SendButtonAsync(admin,
                Assert.Single(fixture.FeatureCatalog, point => point.BehaviorKey == "authorization.dataScope.save"), HttpMethod.Put,
                "/api/authorization/data-scope/rules/" + key, rule);
            Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);
        }
        Task<HttpResponseMessage> Add(HttpClient client, FeaturePoint feature) => FeatureHttpTestRequests.SendAsync(client, feature, HttpMethod.Post,
            "/api/workspace/content/references", new { position, target = new { modelKey = "WorkspaceFolder", id = target } });
        await Rule(target, "workspaceCopy", "deny"); await Rule(target, "edit", "deny");
        using (var success = await Add(ordinary, add)) Assert.Equal(HttpStatusCode.OK, success.StatusCode);
        using (var wrongFeature = await Add(ordinary, entry)) Assert.Equal(HttpStatusCode.Forbidden, wrongFeature.StatusCode);
        await Rule(target, "read", "deny");
        using (var noRead = await Add(ordinary, add)) Assert.Equal(HttpStatusCode.NotFound, noRead.StatusCode);
        await Rule(parent, "edit", "deny");
        using (var noParentEdit = await Add(ordinary, add))
        {
            Assert.Equal(HttpStatusCode.Forbidden, noParentEdit.StatusCode);
            Assert.Equal("Workspace.ParentWriteDenied", (await noParentEdit.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        }
    }
    private static FeaturePoint Point(BusinessAuditHostFixture fixture, string behavior) => Assert.Single(fixture.FeatureCatalog,
        point => point.ModuleId == "Workspace" && point.BehaviorKey == behavior
            && (behavior != "workspace.folder.create" || point.ActionScope == "list.pageActions"));
}