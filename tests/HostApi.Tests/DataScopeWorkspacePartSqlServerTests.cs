using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ICP.Platform.Modules.Authorization.DTOs;
using ICP.Platform.Testing;
using ICP.Platform.Persistence.EfCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static ICP.Platform.HostApi.Tests.EntityOwnershipBackfillSqlServerHttpApiTests;

namespace ICP.Platform.HostApi.Tests;

public sealed class DataScopeWorkspacePartSqlServerTests
{
    [Fact, Trait("Category", "ModuleMetadataConsumer")]
    public void WorkspacePermissionSource_CurrentHostCandidateCompilesBeforeDatabaseOrHttp()
    {
        var hostRoot = Path.GetDirectoryName(typeof(ICP.Platform.HostApi.Controllers.AuthController).Assembly.Location)!;
        var trusted = ICP.Platform.Kernel.ModulePlanning.TrustedBuiltInModuleSource.CreateForHost(hostRoot, Path.Combine(hostRoot, "modules"));
        string[] selected = ["Identity", "Authentication", "Authorization", "Audit", "Organization", "Files", "InforCenter", "Workspace", "Part", "Document"];
        var candidates = new ICP.Platform.Kernel.ModulePlanning.ModuleCapsuleCandidateReader().ReadTrustedBuiltIn(trusted)
            .Where(candidate => selected.Contains(candidate.ModuleId, StringComparer.Ordinal)).ToArray();
        var result = new ICP.Platform.Kernel.ModulePlanning.CanonicalModulePlanCompiler().Compile("workspace-permission-source", candidates);
        Assert.True(result.IsSuccess, JsonSerializer.Serialize(result.Diagnostics));
        Assert.Contains("workspace.content.properties", result.Plan!.Modules.Single(module => module.ModuleId == "Workspace").FrontendBindings);
        // 沿实际Host夹具的准备入口消费同一输出目录，本地化错误不应靠SQL旅程逐个发现。
        var localization = new ICP.Platform.Kernel.PluginPackages.LocalizationRuntimeManifestBuilder().Build(hostRoot);
        _ = ICP.Platform.HostApi.Presentation.ActiveLocalizationManifest.Parse(localization);
        _ = ICP.Platform.Presentation.RuntimeTextCatalog.Parse(localization);
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "Workspace正式功能入口与Part对象数据管理分离，来源每次重验且旧generic不接受Workspace功能头")]
    public async Task WorkspacePartManagement_UsesWorkspaceFeatureAndRealPosition_WithoutBypassingGenericOwnerFeature()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        var manage = fixture.FeatureCatalog.FirstOrDefault(point => point.BehaviorKey == "workspace.folder.authorize"
            && point.Key == "authorizeContent" && point.ActionScope == "list.rowActions");
        Assert.NotNull(manage);
        await InitializeAsync(fixture, admin);
        var position = await ContainerAsync(fixture, admin);
        var part = await CreateAsync(fixture, admin, position, "Part", "parts", "createPart", "CrossOwnerPart295");
        var read = fixture.FeatureCatalog.First(point => point.ReadBehaviors.Contains("workspace.folder.read"));
        var entry = Point(fixture, "workspace.spaces.access");
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "CrossOwner295", "工作空间授权管理", "cross-owner295", "跨Owner账号", entry, read, manage!);
        using var ordinary = actor.Client;
        var path = "/api/workspace/content/" + part.LinkId + "/permissions?queryScope="
            + Uri.EscapeDataString(JsonSerializer.Serialize(position, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        using (var generic = await FeatureHttpTestRequests.SendAsync(ordinary, manage!, HttpMethod.Get,
            "/api/authorization/data-scope/objects/part.part/" + part.Id))
            Assert.Equal(HttpStatusCode.Forbidden, generic.StatusCode);
        var empty = await FeatureHttpTestRequests.ReadAsync(ordinary, manage!, path);
        Assert.Empty(empty.GetProperty("subjects").EnumerateArray());
        var config = new DataScopeObjectDto("part.part", part.Id,
            [new("CrossOwner295Subject", "user", actor.AccountId, 1,
                new Dictionary<string, string> { ["read"] = "allow", ["authorize"] = "allow", ["edit"] = "deny" })]);
        using (var saved = await FeatureHttpTestRequests.SendAsync(ordinary, manage!, HttpMethod.Put, path, config))
            Assert.True(saved.StatusCode == HttpStatusCode.NoContent, await saved.Content.ReadAsStringAsync());
        var current = await FeatureHttpTestRequests.ReadAsync(ordinary, manage!, path);
        Assert.Single(current.GetProperty("subjects").EnumerateArray());
        using (var forgedTarget = await FeatureHttpTestRequests.SendAsync(ordinary, manage!, HttpMethod.Put, path,
            config with { ObjectId = "another-object" }))
            Assert.Equal(HttpStatusCode.BadRequest, forgedTarget.StatusCode);
        var wrongSource = "/api/workspace/content/" + part.LinkId + "/permissions?queryScope="
            + Uri.EscapeDataString(JsonSerializer.Serialize(position with { LinkIds = [] }, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        using (var invalidPosition = await FeatureHttpTestRequests.SendAsync(ordinary, manage!, HttpMethod.Get, wrongSource))
            Assert.Equal(HttpStatusCode.NotFound, invalidPosition.StatusCode);
        using (var revoked = await FeatureHttpTestRequests.SendAsync(ordinary, manage!, HttpMethod.Put, path,
            config with { Subjects = [config.Subjects[0] with { Actions = new Dictionary<string, string>
                { ["read"] = "deny", ["authorize"] = "deny", ["edit"] = "allow" } }] }))
            Assert.Equal(HttpStatusCode.NoContent, revoked.StatusCode);
        using (var noLongerReadable = await FeatureHttpTestRequests.SendAsync(ordinary, manage!, HttpMethod.Get, path))
            Assert.Equal(HttpStatusCode.NotFound, noLongerReadable.StatusCode);
        using (var removed = await FeatureHttpTestRequests.SendAsync(admin, Point(fixture, "workspace.folder.remove"), HttpMethod.Delete,
            "/api/workspace/content/references/" + part.LinkId, new { position }))
            Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
        using (var staleRead = await FeatureHttpTestRequests.SendAsync(admin, manage!, HttpMethod.Get, path))
            Assert.Equal(HttpStatusCode.NotFound, staleRead.StatusCode);
        using (var staleSave = await FeatureHttpTestRequests.SendAsync(admin, manage!, HttpMethod.Put, path, config))
            Assert.Equal(HttpStatusCode.NotFound, staleSave.StatusCode);
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "Workspace属性按真实来源读取Part而不借独立Part功能或授权管理资格")]
    public async Task WorkspacePartProperties_UsesTheActualOccurrenceAndReadFeature_WithoutIndependentOwnerFallback()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        await InitializeAsync(fixture, admin);
        var position = await ContainerAsync(fixture, admin);
        var part = await CreateAsync(fixture, admin, position, "Part", "parts", "createPart", "SourceProperties295");
        var read = fixture.FeatureCatalog.First(point => point.ReadBehaviors.Contains("workspace.folder.read"));
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "SourceProps295", "来源属性查询", "source-properties295", "来源属性账号", Point(fixture, "workspace.spaces.access"), read);
        using var ordinary = actor.Client;
        var scope = Uri.EscapeDataString(JsonSerializer.Serialize(position, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var path = "/api/workspace/content/" + part.LinkId + "/properties?queryScope=" + scope;
        using (var independent = await FeatureHttpTestRequests.SendAsync(ordinary, read, HttpMethod.Get, "/api/part/parts/" + part.Id))
            Assert.Equal(HttpStatusCode.Forbidden, independent.StatusCode);
        var actual = await FeatureHttpTestRequests.ReadAsync(ordinary, read, path);
        Assert.Equal("part.part", actual.GetProperty("resourceKey").GetString());
        Assert.Equal(part.Id, actual.GetProperty("objectId").GetString());
        Assert.Equal(part.Id, actual.GetProperty("detail").GetProperty("fields").GetProperty("EID").GetString());
        using (var wrong = await FeatureHttpTestRequests.SendAsync(ordinary, read, HttpMethod.Get,
            "/api/workspace/content/" + part.LinkId + "/properties?queryScope=" + Uri.EscapeDataString(JsonSerializer.Serialize(position with { LinkIds = [] }, new JsonSerializerOptions(JsonSerializerDefaults.Web)))))
            Assert.Equal(HttpStatusCode.NotFound, wrong.StatusCode);
        using (var bare = await FeatureHttpTestRequests.SendAsync(ordinary, read, HttpMethod.Get,
            "/api/workspace/content/" + part.Id + "/properties?queryScope=" + scope))
            Assert.Equal(HttpStatusCode.NotFound, bare.StatusCode);
        using (var removed = await FeatureHttpTestRequests.SendAsync(admin, Point(fixture, "workspace.folder.remove"), HttpMethod.Delete,
            "/api/workspace/content/references/" + part.LinkId, new { position }))
            Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
        using (var stale = await FeatureHttpTestRequests.SendAsync(admin, read, HttpMethod.Get, path))
            Assert.Equal(HttpStatusCode.NotFound, stale.StatusCode);
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "真实Workspace只读权限面与单对象管理闭合，双LID共一份EID授权，查询账号不切身份")]
    public async Task WorkspacePartPermissionSurface_BrowseProjectsOnlyMatchedNames_AndTwoOccurrencesShareOneAcl()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        await InitializeAsync(fixture, admin);
        var first = await ContainerAsync(fixture, admin);
        var part = await CreateAsync(fixture, admin, first, "Part", "parts", "createPart", "SharedAclPart295");
        var secondFolder = await CreateAsync(fixture, admin, first with { LinkIds = [] }, "WorkspaceFolder", "folders", "create", "第二来源目录295");
        var second = first with { LinkIds = [secondFolder.LinkId] };
        using (var added = await FeatureHttpTestRequests.SendAsync(admin, Point(fixture, "workspace.folder.add"), HttpMethod.Post,
            "/api/workspace/content/references", new { position = second, target = new { modelKey = "Part", id = part.Id } }))
            Assert.Equal(HttpStatusCode.OK, added.StatusCode);
        var read = fixture.FeatureCatalog.First(point => point.ReadBehaviors.Contains("workspace.folder.read"));
        var manage = fixture.FeatureCatalog.First(point => point.Key == "authorizeContent" && point.ActionScope == "list.rowActions");
        Assert.Equal("workspace.folder.authorize", manage.BehaviorKey);
        var linkScope = JsonSerializer.Serialize(new { spaceId = second.SpaceId, rootFolderId = second.RootFolderId, linkIds = JsonSerializer.Serialize(second.LinkIds) });
        var links = await FeatureHttpTestRequests.ReadAsync(admin, read, "/api/links/workspace.content?direction=forward&anchorModelKey=WorkspaceFolder&anchorId="
            + Uri.EscapeDataString(secondFolder.Id) + "&queryScope=" + Uri.EscapeDataString(linkScope));
        var otherLink = Assert.Single(links.GetProperty("items").EnumerateArray());
        Assert.Equal(part.Id, otherLink.GetProperty("associatedObject").GetProperty("id").GetString());
        var secondLid = otherLink.GetProperty("linkId").GetString()!;
        Assert.NotEqual(part.LinkId, secondLid);
        string Path(string lid, Position position, string suffix = "") => "/api/workspace/content/" + lid + "/permissions" + suffix
            + "?queryScope=" + Uri.EscapeDataString(JsonSerializer.Serialize(position, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var firstPath = Path(part.LinkId, first);
        var secondPath = Path(secondLid, second);
        var entry = Point(fixture, "workspace.spaces.access");
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "BrowseSurface295", "浏览角色295", "browse-surface295", "浏览账号295", entry, read, Point(fixture, "workspace.folder.create"));
        using var ordinary = actor.Client;
        var config = new DataScopeObjectDto("part.part", part.Id,
            [new("SharedAcl295Role", "role", actor.RoleId, 7, new Dictionary<string, string> { ["read"] = "allow", ["authorize"] = "allow", ["edit"] = "deny" })]);
        using (var saved = await FeatureHttpTestRequests.SendAsync(admin, manage, HttpMethod.Put, firstPath, config))
            Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);
        var secondConfig = await FeatureHttpTestRequests.ReadAsync(admin, manage, secondPath);
        Assert.Equal(part.Id, secondConfig.GetProperty("objectId").GetString());
        Assert.Equal("SharedAcl295Role", Assert.Single(secondConfig.GetProperty("subjects").EnumerateArray()).GetProperty("id").GetString());
        var catalog = await FeatureHttpTestRequests.ReadAsync(ordinary, read, Path(secondLid, second, "/catalog"));
        Assert.Contains(catalog.GetProperty("actions").EnumerateArray(), action => action.GetProperty("key").GetString() == "authorize");
        Assert.False(catalog.TryGetProperty("subjects", out _));
        var users = await FeatureHttpTestRequests.ReadAsync(ordinary, read, Path(secondLid, second, "/subjects/user") + "&page=1&query=" + Uri.EscapeDataString("浏览账号295"));
        Assert.Equal(actor.AccountId, Assert.Single(users.GetProperty("items").EnumerateArray()).GetProperty("id").GetString());
        using (var otherCandidates = await FeatureHttpTestRequests.SendAsync(ordinary, read, HttpMethod.Get, Path(secondLid, second, "/subjects/role") + "&page=1"))
            Assert.Equal(HttpStatusCode.Forbidden, otherCandidates.StatusCode);
        using (var forbiddenConfiguration = await FeatureHttpTestRequests.SendAsync(ordinary, manage, HttpMethod.Get, secondPath))
            Assert.Equal(HttpStatusCode.Forbidden, forbiddenConfiguration.StatusCode);
        var explanation = await FeatureHttpTestRequests.ReadAsync(ordinary, read, Path(secondLid, second, "/explanation") + "&accountId=" + Uri.EscapeDataString(actor.AccountId));
        var name = Assert.Single(explanation.GetProperty("subjectNames").EnumerateArray());
        Assert.Equal("role", name.GetProperty("type").GetString());
        Assert.Equal(actor.RoleId, name.GetProperty("id").GetString());
        Assert.Equal("浏览角色295", name.GetProperty("name").GetString());
        Assert.Equal("resolved", name.GetProperty("status").GetString());
        var other = await FeatureHttpTestRequests.ReadAsync(ordinary, read, Path(secondLid, second, "/explanation") + "&accountId=" + Uri.EscapeDataString(fixture.ActorId));
        Assert.Equal(fixture.ActorId, other.GetProperty("accountId").GetString());
        var currentUser = await ordinary.GetFromJsonAsync<JsonElement>("/api/auth/current-user");
        Assert.Equal(actor.AccountId, currentUser.GetProperty("id").GetString());
        // 相同read/data默认许可不能让管理员借另一个账号的个人来源读取或管理对象。
        using var personalResponse = await FeatureHttpTestRequests.SendAsync(ordinary, entry, HttpMethod.Post, "/api/workspace/spaces/personal/ensure");
        var personal = await ReadOkAsync(personalResponse);
        var privatePosition = new Position(personal.GetProperty("spaceId").GetString()!, personal.GetProperty("rootFolderId").GetString()!, []);
        var privateFolder = await CreateAsync(fixture, ordinary, privatePosition, "WorkspaceFolder", "folders", "create", "隐私来源295");
        foreach (var suffix in new[] { "", "/eligibility", "/catalog", "/subjects/user", "/explanation" })
        {
            using var deniedPrivate = await FeatureHttpTestRequests.SendAsync(admin, suffix is "" or "/eligibility" ? manage : read,
                HttpMethod.Get, Path(privateFolder.LinkId, privatePosition, suffix));
            Assert.Equal(HttpStatusCode.NotFound, deniedPrivate.StatusCode);
        }
        using (var privateWrite = await FeatureHttpTestRequests.SendAsync(admin, manage, HttpMethod.Put, Path(privateFolder.LinkId, privatePosition), config))
            Assert.Equal(HttpStatusCode.NotFound, privateWrite.StatusCode);
        using (var generic = await FeatureHttpTestRequests.SendAsync(admin, manage, HttpMethod.Get, "/api/authorization/data-scope/objects/part.part/" + part.Id))
            Assert.Equal(HttpStatusCode.Forbidden, generic.StatusCode);
        await new FeatureHttpTestRequests(fixture).GrantAsync(admin, actor.RoleId, entry, read, manage);
        using (var eligibility = await FeatureHttpTestRequests.SendAsync(ordinary, manage, HttpMethod.Get, Path(secondLid, second, "/eligibility")))
            Assert.Equal(HttpStatusCode.OK, eligibility.StatusCode);
        using (var revoked = await FeatureHttpTestRequests.SendAsync(ordinary, manage, HttpMethod.Put, secondPath,
            config with { Subjects = [config.Subjects[0] with { Actions = new Dictionary<string, string> { ["read"] = "deny", ["authorize"] = "deny" } }] }))
            Assert.Equal(HttpStatusCode.NoContent, revoked.StatusCode);
        using (var denied = await FeatureHttpTestRequests.SendAsync(ordinary, read, HttpMethod.Get, Path(part.LinkId, first, "/catalog")))
            Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        using var auditScope = fixture.Services.CreateScope();
        var db = auditScope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
        var changes = await db.Set<Dictionary<string, object?>>("BusinessChange").Where(row => EF.Property<string>(row, "EventType") == "dataScope.object.changed").ToArrayAsync();
        Assert.Equal(2, changes.Length);
        Assert.All(changes, row => Assert.Equal("part.part/" + part.Id, row["TargetId"]));
        Assert.Single(await db.Set<Dictionary<string, object?>>("DataScopeObjectSubject").Where(row => EF.Property<string>(row, "ResourceKey") == "part.part"
            && EF.Property<string>(row, "ObjectId") == part.Id).ToArrayAsync());
    }
}
