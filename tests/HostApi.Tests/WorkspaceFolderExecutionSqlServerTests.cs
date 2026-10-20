using System.Net;
using System.Data.Common;
using System.Text.Json;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Modules.Authorization.Services;
using ICP.Platform.Modules.Authorization.DTOs;
using ICP.Platform.Modules.Workspace;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;
using static ICP.Platform.HostApi.Tests.EntityOwnershipBackfillSqlServerHttpApiTests;

namespace ICP.Platform.HostApi.Tests;

public sealed class WorkspaceFolderExecutionSqlServerTests
{
    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "真实文件夹创建缺正式声明时在候选、父授权初始化和持久化之前拒绝")]
    public async Task FolderCreate_WithoutDeclarationRejectsBeforeFolderReferenceAndInitialization()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        var position = await ContainerAsync(fixture, admin);
        using var beforeScope = fixture.Services.CreateScope();
        var db = beforeScope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
        var folders = await db.Set<Dictionary<string, object?>>("WorkspaceFolder").CountAsync();
        var references = await db.Set<Dictionary<string, object?>>("WorkspaceFolderContent").CountAsync();
        var configurations = await db.Set<Dictionary<string, object?>>("DataScopeObjectSubject").CountAsync();
        // 直接构造缺声明请求；正向fixture日后透传声明也不能遮住此反例。
        using var response = await FeatureHttpTestRequests.SendAsync(admin, Point(fixture, "workspace.folder.create"), HttpMethod.Post,
            "/api/workspace/content/folders", new { position, values = new { ENAME = "NoBinding314" }, copyParentAuthorization = true });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(folders, await db.Set<Dictionary<string, object?>>("WorkspaceFolder").CountAsync());
        Assert.Equal(references, await db.Set<Dictionary<string, object?>>("WorkspaceFolderContent").CountAsync());
        Assert.Equal(configurations, await db.Set<Dictionary<string, object?>>("DataScopeObjectSubject").CountAsync());
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "真实创建的初始化缺/串用来源在任何配置读取和克隆前拒绝；合法来源沿原事务执行")]
    public async Task FolderCreate_InitializationRejectsMissingOrMismatchedSourceBeforeAnyConfigurationSql()
    {
        var state = new InitializationProbe();
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(services =>
        {
            services.AddDbContext<EfPlatformDbContext>((_, options) => options.AddInterceptors(state));
            services.Replace(ServiceDescriptor.Scoped<IDataScopeObjectInitializer>(provider =>
                new SourceProbeInitializer(ActivatorUtilities.CreateInstance<DataScopeObjectInitializer>(provider), state)));
        }, includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        var position = await ContainerAsync(fixture, admin);
        var feature = Point(fixture, "workspace.folder.create");
        var declaration = Assert.Single(fixture.Services.GetRequiredService<CanonicalModulePlan>().ActionDataAccessCatalog,
            binding => binding.Execution == "workspace.folder.create" && binding.FeaturePositions.Contains(feature.Position));
        var before = await Snapshot();
        foreach (var mismatch in new[] { "missing", "parents", "target", "values", "caller", "binding", "unregistered-shape" })
        {
            state.Mismatch = mismatch; state.ConfigurationCommands = 0;
            using var rejected = await FeatureHttpTestRequests.SendAsync(admin, feature, HttpMethod.Post, "/api/workspace/content/folders",
                new { position, values = new { ENAME = "RejectedSource314-" + mismatch }, copyParentAuthorization = true }, declaration);
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
            Assert.Equal(0, state.ConfigurationCommands);
            Assert.Equal(before, await Snapshot());
        }
        state.Mismatch = null; state.ConfigurationCommands = 0;
        using var accepted = await FeatureHttpTestRequests.SendAsync(admin, feature, HttpMethod.Post, "/api/workspace/content/folders",
            new { position, values = new { ENAME = "AcceptedSource314" }, copyParentAuthorization = true }, declaration);
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        Assert.True(state.ConfigurationCommands > 0);

        async Task<string> Snapshot()
        {
            using var scope = fixture.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
            var counts = new List<int>();
            foreach (var model in new[] { "WorkspaceFolder", "WorkspaceFolderContent", "DataScopeObjectSubject", "DataScopeObjectAction", "BusinessChange" })
                counts.Add(await db.Set<Dictionary<string, object?>>(model).CountAsync());
            return JsonSerializer.Serialize(counts);
        }
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "纯候选API使用自身固定来源，不借用view声明，原功能资格仍独立执行")]
    public async Task FolderCandidates_UsesFixedApiSourceAndRejectsEntryOrInternalDeclaration()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        await ContainerAsync(fixture, admin);
        var feature = Point(fixture, "workspace.spaces.access");
        const string path = "/api/workspace/folders/candidates?pageSize=2";
        using (var accepted = await FeatureHttpTestRequests.SendAsync(admin, feature, HttpMethod.Get, path))
        {
            Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
            var body = await JsonSerializer.DeserializeAsync<JsonElement>(await accepted.Content.ReadAsStreamAsync());
            Assert.NotEmpty(body.GetProperty("items").EnumerateArray());
        }
        var createFeature = Point(fixture, "workspace.folder.create");
        var declaration = Assert.Single(fixture.Services.GetRequiredService<CanonicalModulePlan>().ActionDataAccessCatalog,
            binding => binding.Execution == WorkspaceFolderExecutions.Create && binding.FeaturePositions.Contains(createFeature.Position));
        using (var entry = await FeatureHttpTestRequests.SendAsync(admin, feature, HttpMethod.Get, path, dataAccess: declaration))
            Assert.Equal(HttpStatusCode.BadRequest, entry.StatusCode);
        // internal合同没有客户端声明ID；任意自报该身份也不能选择纯API的执行来源。
        using (var request = new HttpRequestMessage(HttpMethod.Get, path))
        {
            request.Headers.Add("X-NewP-Feature", feature.Position);
            request.Headers.Add("X-NewP-Data-Declaration", "workspace.properties.detail");
            using var rejected = await admin.SendAsync(request);
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        }
        using var missingFeature = await admin.GetAsync(path);
        Assert.Equal(HttpStatusCode.Forbidden, missingFeature.StatusCode);
        using var wrongFeature = await FeatureHttpTestRequests.SendAsync(admin, Point(fixture, "authorization.workbench.save"), HttpMethod.Get, path);
        Assert.Equal(HttpStatusCode.Forbidden, wrongFeature.StatusCode);
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "正式出现树必须携带其真实view读取声明；私有位置读取不借用该HTTP头")]
    public async Task FolderTree_RequiresItsActualViewReadDeclaration()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        await ContainerAsync(fixture, admin);
        var feature = Point(fixture, "workspace.spaces.access");
        const string path = "/api/trees/workspace.structure/roots?scope=currentUser";
        using (var missing = await FeatureHttpTestRequests.SendAsync(admin, feature, HttpMethod.Get, path))
            Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        using (var missingSearch = await FeatureHttpTestRequests.SendAsync(admin, feature, HttpMethod.Get,
            "/api/trees/workspace.structure/search?scope=currentUser&keyword="))
            Assert.Equal(HttpStatusCode.BadRequest, missingSearch.StatusCode);
        var declarations = fixture.Services.GetRequiredService<CanonicalModulePlan>().ActionDataAccessCatalog;
        var read = Assert.Single(declarations, item => item.ModuleId == "Workspace" && item.ViewKey == "workspace.structure"
            && item.Surface == "read" && item.FeaturePositions.Contains(feature.Position));
        using (var accepted = await FeatureHttpTestRequests.SendAsync(admin, feature, HttpMethod.Get, path, dataAccess: read))
        {
            Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
            var roots = await JsonSerializer.DeserializeAsync<JsonElement>(await accepted.Content.ReadAsStreamAsync());
            Assert.NotEmpty(roots.EnumerateArray());
        }
        var create = Point(fixture, "workspace.folder.create");
        var foreign = Assert.Single(declarations, item => item.Execution == WorkspaceFolderExecutions.Create && item.FeaturePositions.Contains(create.Position));
        using var rejected = await FeatureHttpTestRequests.SendAsync(admin, feature, HttpMethod.Get, path, dataAccess: foreign);
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "编辑原位消费父正式绑定与Folder内部目标；父拒绝不提前解析未知LID")]
    public async Task FolderEdit_RequiresParentDeclarationAndKeepsTargetLookupAfterParentDecision()
    {
        var probe = new EditTargetLookupProbe();
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(services =>
            services.AddDbContext<EfPlatformDbContext>((_, options) => options.AddInterceptors(probe)), includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        var position = await ContainerAsync(fixture, admin);
        var child = await CreateAsync(fixture, admin, position, "WorkspaceFolder", "folders", "create", "EditSource314");
        var feature = Point(fixture, "workspace.folder.editObject");
        var path = "/api/workspace/content/" + child.LinkId;
        using (var missing = await FeatureHttpTestRequests.SendAsync(admin, feature, HttpMethod.Post, path + "/edit-values", new { position }))
            Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        using (var missing = await FeatureHttpTestRequests.SendAsync(admin, feature, HttpMethod.Put, path,
            new { position, values = new { ENAME = "MustNotWrite314" } }))
            Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Equal("EditSource314", (await RowAsync(fixture, child))["ENAME"]);
        var declaration = Assert.Single(fixture.Services.GetRequiredService<CanonicalModulePlan>().ActionDataAccessCatalog,
            item => item.ActionKey == "edit" && item.Surface == "rowActions" && item.FeaturePositions.Contains(feature.Position));
        using (var prepared = await SendButton(admin, feature, HttpMethod.Post, path + "/edit-values", new { position }, declaration))
            Assert.Equal(HttpStatusCode.OK, prepared.StatusCode);
        using (var submitted = await SendButton(admin, feature, HttpMethod.Put, path,
            new { position, values = new { ENAME = "EditedSource314" } }, declaration))
            Assert.Equal(HttpStatusCode.OK, submitted.StatusCode);
        Assert.Equal("EditedSource314", (await RowAsync(fixture, child))["ENAME"]);
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "EditOrder314", "编辑顺序", "edit-order314", "编辑顺序账号",
            Point(fixture, "workspace.spaces.access"), feature);
        using var ordinary = actor.Client;
        var parentLinkId = position.LinkIds[^1];
        string parentId;
        using (var scope = fixture.Services.CreateScope())
            parentId = (string)(await scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>()
                .Set<Dictionary<string, object?>>("WorkspaceFolderContent").AsNoTracking()
                .SingleAsync(row => EF.Property<string>(row, "LID") == parentLinkId))["CEID"]!;
        var authorization = new FeatureHttpTestRequests(fixture).Declaration("workspace.content.authorization.manage", Point(fixture, "workspace.folder.authorize"));
        foreach (var unreadable in new[] { false, true })
        {
            var subjects = new[] { new DataScopeDirectSubjectDto("EditOrderSubject314", "user", actor.AccountId, 1,
                new Dictionary<string, string> { ["read"] = unreadable ? "deny" : "allow", ["edit"] = "deny" }) };
            var parentScope = new { spaceId = position.SpaceId, rootFolderId = position.RootFolderId, linkIds = position.LinkIds[..^1] };
            using (var saved = await FeatureHttpTestRequests.SendAsync(admin, Point(fixture, "workspace.folder.authorize"), HttpMethod.Put,
                "/api/workspace/content/" + position.LinkIds[^1] + "/permissions?queryScope=" + Uri.EscapeDataString(JsonSerializer.Serialize(parentScope)),
                new DataScopeObjectConfiguration("workspace.folder", parentId, subjects), authorization))
                Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);
            foreach (var preparing in new[] { true, false })
            {
                probe.TargetCommands = 0; probe.Capturing = true;
                try
                {
                    using var rejected = await SendButton(ordinary, feature, preparing ? HttpMethod.Post : HttpMethod.Put,
                        "/api/workspace/content/" + EditTargetLookupProbe.MissingTarget + (preparing ? "/edit-values" : ""),
                        preparing ? new { position } : (object)new { position, values = new { ENAME = "NeverWrite314" } }, declaration);
                    Assert.Equal(unreadable ? HttpStatusCode.NotFound : HttpStatusCode.Forbidden, rejected.StatusCode);
                    var body = await JsonSerializer.DeserializeAsync<JsonElement>(await rejected.Content.ReadAsStreamAsync());
                    Assert.Equal(unreadable ? "Workspace.PositionUnavailable" : "Workspace.ParentWriteDenied", body.GetProperty("code").GetString());
                    Assert.Equal(0, probe.TargetCommands);
                }
                finally { probe.Capturing = false; }
            }
        }
        Assert.Equal("EditedSource314", (await RowAsync(fixture, child))["ENAME"]);

        static Task<HttpResponseMessage> SendButton(HttpClient client, FeaturePoint point, HttpMethod method, string url,
            object value, ActionDataAccessRequirement source)
        {
            var request = new HttpRequestMessage(method, url);
            request.Headers.Add("X-NewP-Feature", point.Position);
            request.Headers.Add("X-NewP-Button", source.Id);
            request.Content = System.Net.Http.Json.JsonContent.Create(value, options: new JsonSerializerOptions());
            return client.SendAsync(request);
        }
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "正式内容列表消费实际link view读取声明，不借树声明且保留位置、SQL过滤和分页")]
    public async Task FolderContent_RequiresItsActualLinkViewReadDeclaration()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        var position = await ContainerAsync(fixture, admin);
        await CreateAsync(fixture, admin, position, "WorkspaceFolder", "folders", "create", "ContentRead314");
        var parentLink = position.LinkIds[^1];
        string parentId;
        using (var scope = fixture.Services.CreateScope())
            parentId = (string)(await scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>()
                .Set<Dictionary<string, object?>>("WorkspaceFolderContent").AsNoTracking()
                .SingleAsync(row => EF.Property<string>(row, "LID") == parentLink))["CEID"]!;
        var queryScope = new Dictionary<string, string?> { ["spaceId"] = position.SpaceId, ["rootFolderId"] = position.RootFolderId,
            ["linkIds"] = JsonSerializer.Serialize(position.LinkIds) };
        var path = "/api/links/workspace.content?direction=forward&anchorModelKey=WorkspaceFolder&anchorId=" + Uri.EscapeDataString(parentId)
            + "&queryScope=" + Uri.EscapeDataString(JsonSerializer.Serialize(queryScope)) + "&pageSize=2";
        var feature = Point(fixture, "workspace.spaces.access");
        using (var missing = await FeatureHttpTestRequests.SendAsync(admin, feature, HttpMethod.Get, path))
            Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        var declarations = fixture.Services.GetRequiredService<CanonicalModulePlan>().ActionDataAccessCatalog;
        var read = Assert.Single(declarations, item => item.ModuleId == "Workspace" && item.ViewKey == "workspace.folder.list"
            && item.Surface == "read" && item.FeaturePositions.Contains(feature.Position));
        using (var accepted = await FeatureHttpTestRequests.SendAsync(admin, feature, HttpMethod.Get, path, dataAccess: read))
        {
            Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
            var page = await JsonSerializer.DeserializeAsync<JsonElement>(await accepted.Content.ReadAsStreamAsync());
            Assert.Equal(1, page.GetProperty("total").GetInt32());
            Assert.Single(page.GetProperty("items").EnumerateArray());
            Assert.Equal(2, page.GetProperty("pageSize").GetInt32());
        }
        var tree = Assert.Single(declarations, item => item.ViewKey == "workspace.structure" && item.Surface == "read");
        using var rejected = await FeatureHttpTestRequests.SendAsync(admin, feature, HttpMethod.Get, path, dataAccess: tree);
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "实际Folder详情只消费其正式view读取声明；完整来源属性与裸EID详情共用原字段投影")]
    public async Task FolderDetail_RequiresItsActualReadDeclarationAndKeepsTheOriginalSource()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        var position = await ContainerAsync(fixture, admin);
        var child = await CreateAsync(fixture, admin, position, "WorkspaceFolder", "folders", "create", "DetailSource314");
        var feature = Point(fixture, "workspace.spaces.access");
        var path = "/api/workspace/folders/" + child.Id;
        using (var missing = await FeatureHttpTestRequests.SendAsync(admin, feature, HttpMethod.Get, path))
            Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        var declarations = fixture.Services.GetRequiredService<CanonicalModulePlan>().ActionDataAccessCatalog;
        var read = Assert.Single(declarations, item => item.ViewKey == "workspace.folder.detail" && item.Surface == "read");
        using (var accepted = await FeatureHttpTestRequests.SendAsync(admin, feature, HttpMethod.Get, path, dataAccess: read))
        {
            Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
            var body = await JsonSerializer.DeserializeAsync<JsonElement>(await accepted.Content.ReadAsStreamAsync());
            Assert.Equal(child.Id, body.GetProperty("fields").GetProperty("EID").GetString());
        }
        var scope = new { spaceId = position.SpaceId, rootFolderId = position.RootFolderId, linkIds = position.LinkIds };
        var propertiesPath = "/api/workspace/content/" + child.LinkId + "/properties?queryScope="
            + Uri.EscapeDataString(JsonSerializer.Serialize(scope));
        using (var properties = await FeatureHttpTestRequests.SendAsync(admin, feature, HttpMethod.Get, propertiesPath, dataAccess: read))
        {
            Assert.Equal(HttpStatusCode.OK, properties.StatusCode);
            var body = await JsonSerializer.DeserializeAsync<JsonElement>(await properties.Content.ReadAsStreamAsync());
            Assert.Equal(child.Id, body.GetProperty("objectId").GetString());
        }
        var tree = Assert.Single(declarations, item => item.ViewKey == "workspace.structure" && item.Surface == "read");
        using var wrongSource = await FeatureHttpTestRequests.SendAsync(admin, feature, HttpMethod.Get, path, dataAccess: tree);
        Assert.Equal(HttpStatusCode.BadRequest, wrongSource.StatusCode);
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "无UI根详情使用自身固定API来源；借用view头拒绝而真实根配对和原功能资格保留")]
    public async Task SpaceRootDetails_UseTheirOwnFixedApiSourceWithoutBorrowingAView()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        var position = await ContainerAsync(fixture, admin);
        var feature = Point(fixture, "workspace.spaces.access");
        var create = Point(fixture, "workspace.folder.create");
        var foreign = Assert.Single(fixture.Services.GetRequiredService<CanonicalModulePlan>().ActionDataAccessCatalog,
            item => item.Execution == WorkspaceFolderExecutions.Create && item.FeaturePositions.Contains(create.Position));
        foreach (var path in new[] { "/api/workspace/spaces/" + position.SpaceId + "/root", "/api/workspace/spaces/roots/" + position.RootFolderId })
        {
            using (var accepted = await FeatureHttpTestRequests.SendAsync(admin, feature, HttpMethod.Get, path))
            {
                Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
                var body = await JsonSerializer.DeserializeAsync<JsonElement>(await accepted.Content.ReadAsStreamAsync());
                Assert.Equal(position.RootFolderId, body.GetProperty("fields").GetProperty("EID").GetString());
            }
            using var rejected = await FeatureHttpTestRequests.SendAsync(admin, feature, HttpMethod.Get, path, dataAccess: foreign);
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        }
        using (var nonRoot = await FeatureHttpTestRequests.SendAsync(admin, feature, HttpMethod.Get,
            "/api/workspace/spaces/roots/" + "not-a-root314"))
            Assert.Equal(HttpStatusCode.NotFound, nonRoot.StatusCode);
        using var noFeature = await admin.GetAsync("/api/workspace/spaces/roots/" + position.RootFolderId);
        Assert.Equal(HttpStatusCode.Forbidden, noFeature.StatusCode);
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "正式重排缺声明先于目标查询与写入拒绝；合法声明仍只消费父edit和目标read")]
    public async Task FolderReorder_RequiresItsActualParentDeclarationBeforeSelectingAnyTarget()
    {
        var probe = new EditTargetLookupProbe();
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(services =>
            services.AddDbContext<EfPlatformDbContext>((_, options) => options.AddInterceptors(probe)), includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        var position = await ContainerAsync(fixture, admin);
        await CreateAsync(fixture, admin, position, "WorkspaceFolder", "folders", "create", "OrderFirst314");
        var selected = await CreateAsync(fixture, admin, position, "WorkspaceFolder", "folders", "create", "OrderSelected314");
        var feature = Assert.Single(fixture.FeatureCatalog, point => point.ActionScope == "contextMenu" && point.Key == "moveUp");
        object Body(string linkId) => new { parentPosition = new { spaceId = position.SpaceId, rootFolderId = position.RootFolderId,
            linkIds = position.LinkIds }, linkId, direction = "up" };
        using (var missing = await FeatureHttpTestRequests.SendAsync(admin, feature, HttpMethod.Post,
            "/api/workspace/content/tree-order", Body(selected.LinkId)))
            Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        probe.TargetCommands = 0; probe.Capturing = true;
        try
        {
            using var unknownMissing = await FeatureHttpTestRequests.SendAsync(admin, feature, HttpMethod.Post,
                "/api/workspace/content/tree-order", Body(EditTargetLookupProbe.MissingTarget));
            Assert.Equal(HttpStatusCode.BadRequest, unknownMissing.StatusCode);
            Assert.Equal(0, probe.TargetCommands);
        }
        finally { probe.Capturing = false; }
        var declaration = Assert.Single(fixture.Services.GetRequiredService<CanonicalModulePlan>().ActionDataAccessCatalog,
            item => item.ViewKey == "workspace.folder.list" && item.Surface == "featureActions" && item.ActionKey == "moveUp");
        using var accepted = await FeatureHttpTestRequests.SendAsync(admin, feature, HttpMethod.Post,
            "/api/workspace/content/tree-order", Body(selected.LinkId), declaration);
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        var receipt = await JsonSerializer.DeserializeAsync<JsonElement>(await accepted.Content.ReadAsStreamAsync());
        Assert.True(receipt.GetProperty("moved").GetBoolean());
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "原空间列表和兼容直属/路径API使用各自固定来源，不借view头且保留原根与位置校验")]
    public async Task SpaceListAndPositionApis_UseTheirFixedSourcesAndPreserveRootPairing()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        var position = await ContainerAsync(fixture, admin);
        var child = await CreateAsync(fixture, admin, position, "WorkspaceFolder", "folders", "create", "PositionApi314");
        var feature = Point(fixture, "workspace.spaces.access");
        var foreign = Assert.Single(fixture.Services.GetRequiredService<CanonicalModulePlan>().ActionDataAccessCatalog,
            item => item.ViewKey == "workspace.structure" && item.Surface == "read");
        var query = "?spaceId=" + Uri.EscapeDataString(position.SpaceId) + "&rootFolderId=" + Uri.EscapeDataString(position.RootFolderId)
            + "&linkIds=" + Uri.EscapeDataString(JsonSerializer.Serialize(position.LinkIds));
        foreach (var path in new[] { "/api/workspace/spaces", "/api/workspace/content/children" + query, "/api/workspace/content/path" + query })
        {
            using (var accepted = await FeatureHttpTestRequests.SendAsync(admin, feature, HttpMethod.Get, path))
            {
                Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
                var body = await JsonSerializer.DeserializeAsync<JsonElement>(await accepted.Content.ReadAsStreamAsync());
                if (path == "/api/workspace/spaces") Assert.Contains(body.EnumerateArray(), root => root.GetProperty("rootFolderId").GetString() == position.RootFolderId);
                else if (path.StartsWith("/api/workspace/content/children", StringComparison.Ordinal))
                    Assert.Equal(child.Id, Assert.Single(body.GetProperty("items").EnumerateArray()).GetProperty("associatedObject").GetProperty("id").GetString());
                else Assert.Equal(2, body.GetProperty("folderIds").GetArrayLength());
            }
            using (var rejected = await FeatureHttpTestRequests.SendAsync(admin, feature, HttpMethod.Get, path, dataAccess: foreign))
                Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
            using var noFeature = await admin.GetAsync(path);
            Assert.Equal(HttpStatusCode.Forbidden, noFeature.StatusCode);
        }
        foreach (var kind in new[] { "children", "path" })
        {
            var wrongRoot = "/api/workspace/content/" + kind + "?spaceId=" + Uri.EscapeDataString(position.SpaceId)
                + "&rootFolderId=" + Uri.EscapeDataString(child.Id) + "&linkIds=%5B%5D";
            using var rejected = await FeatureHttpTestRequests.SendAsync(admin, feature, HttpMethod.Get, wrongRoot);
            Assert.Equal(HttpStatusCode.NotFound, rejected.StatusCode);
        }
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "重排父拒绝保持在所选LID查询之前；不可读父与不可写父保留原错误顺序")]
    public async Task FolderReorder_ParentDenialDoesNotResolveTheSelectedReference()
    {
        var probe = new EditTargetLookupProbe();
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(services =>
            services.AddDbContext<EfPlatformDbContext>((_, options) => options.AddInterceptors(probe)), includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        var position = await ContainerAsync(fixture, admin);
        var feature = Assert.Single(fixture.FeatureCatalog, point => point.ActionScope == "contextMenu" && point.Key == "moveUp");
        var declaration = Assert.Single(fixture.Services.GetRequiredService<CanonicalModulePlan>().ActionDataAccessCatalog,
            item => item.ViewKey == "workspace.folder.list" && item.Surface == "featureActions" && item.ActionKey == "moveUp");
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "ReorderParent314", "重排父规则", "reorder-parent314", "重排父账号",
            Point(fixture, "workspace.spaces.access"), feature);
        using var ordinary = actor.Client;
        var parentLink = position.LinkIds[^1];
        string parentId;
        using (var scope = fixture.Services.CreateScope())
            parentId = (string)(await scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>()
                .Set<Dictionary<string, object?>>("WorkspaceFolderContent").AsNoTracking()
                .SingleAsync(row => EF.Property<string>(row, "LID") == parentLink))["CEID"]!;
        foreach (var unreadable in new[] { false, true })
        {
            var subjects = new[] { new DataScopeDirectSubjectDto("ReorderParentSubject314", "user", actor.AccountId, 1,
                new Dictionary<string, string> { ["read"] = unreadable ? "deny" : "allow", ["edit"] = "deny" }) };
            var parentScope = new { spaceId = position.SpaceId, rootFolderId = position.RootFolderId, linkIds = position.LinkIds[..^1] };
            using (var saved = await FeatureHttpTestRequests.SendAsync(admin, Point(fixture, "workspace.folder.authorize"), HttpMethod.Put,
                "/api/workspace/content/" + parentLink + "/permissions?queryScope=" + Uri.EscapeDataString(JsonSerializer.Serialize(parentScope)),
                new DataScopeObjectConfiguration("workspace.folder", parentId, subjects),
                new FeatureHttpTestRequests(fixture).Declaration("workspace.content.authorization.manage", Point(fixture, "workspace.folder.authorize"))))
                Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);
            probe.TargetCommands = 0; probe.Capturing = true;
            try
            {
                using var rejected = await FeatureHttpTestRequests.SendAsync(ordinary, feature, HttpMethod.Post,
                    "/api/workspace/content/tree-order", new { parentPosition = new { spaceId = position.SpaceId,
                        rootFolderId = position.RootFolderId, linkIds = position.LinkIds }, linkId = EditTargetLookupProbe.MissingTarget, direction = "up" }, declaration);
                Assert.Equal(unreadable ? HttpStatusCode.NotFound : HttpStatusCode.Forbidden, rejected.StatusCode);
                var body = await JsonSerializer.DeserializeAsync<JsonElement>(await rejected.Content.ReadAsStreamAsync());
                Assert.Equal(unreadable ? "Workspace.PositionUnavailable" : "Workspace.ParentWriteDenied", body.GetProperty("code").GetString());
                Assert.Equal(0, probe.TargetCommands);
            }
            finally { probe.Capturing = false; }
        }
    }

    private sealed class EditTargetLookupProbe : DbCommandInterceptor
    {
        internal const string MissingTarget = "unknown-edit-target314";
        internal bool Capturing;
        internal int TargetCommands;
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken token = default)
        {
            if (Capturing && command.CommandText.Contains("WorkspaceFolderContent", StringComparison.Ordinal)
                && command.Parameters.Cast<DbParameter>().Any(parameter => parameter.Value is string value && value.Contains(MissingTarget, StringComparison.Ordinal)))
                TargetCommands++;
            return ValueTask.FromResult(result);
        }
    }

    private sealed class InitializationProbe : DbCommandInterceptor
    {
        internal string? Mismatch;
        internal bool Capturing;
        internal int ConfigurationCommands;
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken token = default)
        {
            if (Capturing && command.CommandText.Contains("DataScopeObject", StringComparison.Ordinal)) ConfigurationCommands++;
            return ValueTask.FromResult(result);
        }
    }

    // 只在实际创建已形成真实父/候选后破坏来源事实；从不伪造权限允许或替换真实初始化器。
    private sealed class SourceProbeInitializer(DataScopeObjectInitializer inner, InitializationProbe state) : IDataScopeObjectInitializer
    {
        public Task<DataScopeObjectInitialization> InitializeFromParentsAsync(string resource, IReadOnlyList<string> parents, string target,
            DataScopeObjectInitializationSource source, CancellationToken token = default) => inner.InitializeFromParentsAsync(resource, parents, target, source, token);
        public async Task<bool> InitializeFromParentAsync(string resource, string parent, string target,
            DataScopeObjectInitializationSource source, CancellationToken token = default)
        {
            state.Capturing = true;
            try
            {
                if (state.Mismatch == "missing") return await inner.InitializeFromParentAsync(resource, parent, target, null!, token);
                var declared = Assert.IsType<DataScopeObjectInitializationSource.Declared>(source);
                source = state.Mismatch switch
                {
                    "parents" => declared with { ParentObjectIds = ["different-parent"] },
                    "target" => declared with { Target = declared.Target with { ObjectId = "different-target" } },
                    "values" => declared with { Target = declared.Target with { Values = new Dictionary<string, object?> { ["LID"] = target } } },
                    "caller" => declared with { Caller = declared.Caller with { Caller = typeof(WorkspaceFolderExecutionSqlServerTests) } },
                    "binding" => declared with { Binding = declared.Binding with { Execution = WorkspaceClipboardExecutions.Copy } },
                    "unregistered-shape" => new UnregisteredInitializationSource(declared.Caller, declared.ParentObjectIds, declared.Target),
                    _ => source
                };
                return await inner.InitializeFromParentAsync(resource, parent, target, source, token);
            }
            finally { state.Capturing = false; }
        }
    }
    private sealed record UnregisteredInitializationSource(DataExecutionInternalBinding Caller, IReadOnlyList<string> Parents,
        DataExecutionWriteFacts Target) : DataScopeObjectInitializationSource(Caller, Parents, Target);

}
