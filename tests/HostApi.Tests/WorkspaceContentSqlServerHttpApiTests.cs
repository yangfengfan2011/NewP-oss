using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ICP.Platform.Testing;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Modules.Authorization.DTOs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using System.Data.Common;
using System.Linq.Expressions;
using ICP.Platform.Crud.LinkQueries;
using ICP.Platform.Persistence.Planning;
using ICP.Platform.Modeling.Metadata;
using Xunit.Abstractions;
using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Data.SqlClient;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed class WorkspaceContentSqlServerHttpApiTests(ITestOutputHelper output)
{
    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "正式工作空间创建和通用Link读取在同一SQL存储贯通")]
    public async Task CreateFolder_QueriesRealLinkWithDistinctRelationAndObjectIdentity()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        var entry = Assert.Single(fixture.FeatureCatalog, p => p.ModuleId == "Workspace" && p.Key == "workspace");
        using var initialized = await FeatureHttpTestRequests.SendAsync(admin, entry, HttpMethod.Post, "/api/workspace/spaces/public/initialize");
        var root = await initialized.Content.ReadFromJsonAsync<JsonElement>();
        var position = new { spaceId = root.GetProperty("spaceId").GetString(), rootFolderId = root.GetProperty("rootFolderId").GetString(), linkIds = Array.Empty<string>() };
        var create = Assert.Single(fixture.FeatureCatalog, p => p.BehaviorKey == "workspace.folder.create");
        using var created = await FeatureHttpTestRequests.SendAsync(admin, create, HttpMethod.Post, "/api/workspace/content/folders", new { position, values = new { ENAME = "首个文件夹", Description = new string('描', 500) } });
        Assert.True(created.StatusCode == HttpStatusCode.OK, await created.Content.ReadAsStringAsync());
        var detail = await created.Content.ReadFromJsonAsync<JsonElement>();
        var id = detail.GetProperty("id").GetString();
        var scope = JsonSerializer.Serialize(new { position.spaceId, position.rootFolderId, linkIds = "[]" });
        var url = "/api/links/workspace.content?direction=forward&anchorModelKey=WorkspaceFolder&anchorId=" + Uri.EscapeDataString(position.rootFolderId!) + "&queryScope=" + Uri.EscapeDataString(scope);
        var list = await FeatureHttpTestRequests.ReadAsync(admin, entry, url);
        foreach (var (legacyUrl, code, field) in new[]
        {
            (url.Replace("anchorModelKey=WorkspaceFolder", "anchorModelKey=Workspace%2FWorkspaceFolder", StringComparison.Ordinal), "LinkQuery.InvalidAnchor", "anchor"),
            (url + "&types=Workspace%2FWorkspaceFolder", "LinkQuery.InvalidType", "types")
        })
        {
            using var rejected = await FeatureHttpTestRequests.SendAsync(admin, entry, HttpMethod.Get, legacyUrl);
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
            var problem = await rejected.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(code, problem.GetProperty("code").GetString());
            Assert.Equal(code, problem.GetProperty("reasonKey").GetString());
            var fieldError = Assert.Single(problem.GetProperty("fieldErrors").EnumerateArray());
            Assert.Equal(field, fieldError.GetProperty("field").GetString());
            Assert.Equal(code, fieldError.GetProperty("reasonKey").GetString());
        }
        var row = Assert.Single(list.GetProperty("items").EnumerateArray());
        Assert.Equal(1, list.GetProperty("total").GetInt32());
        Assert.NotEqual(id, row.GetProperty("linkId").GetString());
        Assert.Equal(id, row.GetProperty("associatedObject").GetProperty("id").GetString());
        Assert.Equal("WorkspaceFolder", row.GetProperty("parent").GetProperty("modelKey").GetString());
        Assert.Equal("首个文件夹", row.GetProperty("fields").GetProperty("Name").GetString());
        Assert.False(row.GetProperty("fields").TryGetProperty("OriginSpaceId", out _));
        Assert.Equal(500, detail.GetProperty("fields").GetProperty("Description").GetString()!.Length);
        Assert.True(row.GetProperty("fields").TryGetProperty("UpdatedAt_DisplayValue", out _));
        Assert.True(row.GetProperty("fields").TryGetProperty("UpdatedBy_DisplayValue", out _));
        var normalizedSort = await FeatureHttpTestRequests.ReadAsync(admin, entry, url + "&sortBy=%20Name%20&sortDirection=%20ASC%20");
        Assert.Equal("Name", normalizedSort.GetProperty("sortBy").GetString()); Assert.Equal("asc", normalizedSort.GetProperty("sortDirection").GetString());
        var linkId = row.GetProperty("linkId").GetString();
        await fixture.RestartWithActionsAsync(new Dictionary<string, bool>());
        using var relogged = fixture.CreateClient();
        await BusinessAuditHostFixture.RefreshAntiforgeryAsync(relogged);
        using var login = await relogged.PostAsJsonAsync("/api/auth/login", new { ECODE = "admin", Password = "Audit-test-secure-938!" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        await BusinessAuditHostFixture.RefreshAntiforgeryAsync(relogged);
        var restored = await FeatureHttpTestRequests.ReadAsync(relogged, Entry(fixture), url);
        var restoredRow = Assert.Single(restored.GetProperty("items").EnumerateArray());
        Assert.Equal(linkId, restoredRow.GetProperty("linkId").GetString()); Assert.Equal(id, restoredRow.GetProperty("associatedObject").GetProperty("id").GetString());
        var restoredDetail = await FeatureHttpTestRequests.ReadAsync(relogged, Entry(fixture), "/api/workspace/folders/" + id);
        Assert.Equal(500, restoredDetail.GetProperty("fields").GetProperty("Description").GetString()!.Length);
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "字段保护、同级并发唯一与实体关系事务回滚使用真实SQL")]
    public async Task CreateFolder_ProtectsFieldsSerializesSiblingNamesAndRollsBackLinkFailure()
    {
        var fault = new LinkInsertFault();
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(services =>
            services.AddDbContext<EfPlatformDbContext>((_, options) => options.AddInterceptors(fault)), includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync(); var entry = Entry(fixture); var create = Create(fixture);
        var position = await InitializeAsync(admin, entry);
        var before = await CountsAsync(fixture);
        foreach (var (values, code) in new (object, string)[]
        {
            (new { ENAME = "   " }, "Workspace.InvalidName"),
            (new { ENAME = "超长说明", Description = new string('字', 501) }, "Workspace.DescriptionTooLong"),
            (new { ENAME = "伪造来源", OriginSpaceId = "other" }, "Workspace.ServerFieldForbidden"),
            (new { ENAME = "伪造审计", CreatedBy = "other" }, "Workspace.ServerFieldForbidden"),
            (new { ENAME = "伪造关系", LID = "chosen", ChildModelKey = "Other/Type", CEID = "missing" }, "Workspace.ServerFieldForbidden")
        })
        {
            using var rejected = await FeatureHttpTestRequests.SendAsync(admin, create, HttpMethod.Post, "/api/workspace/content/folders", new { position, values });
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
            Assert.Equal(code, (await rejected.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        }
        Assert.Equal(before, await CountsAsync(fixture));
        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => FeatureHttpTestRequests.SendAsync(admin, create, HttpMethod.Post,
            "/api/workspace/content/folders", new { position, values = new { ENAME = "并发同级名称" } })));
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
        Assert.Equal(3, responses.Count(response => response.StatusCode == HttpStatusCode.Conflict));
        foreach (var response in responses)
        {
            if (response.StatusCode == HttpStatusCode.Conflict)
                Assert.Equal("Workspace.SiblingNameConflict", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
            response.Dispose();
        }
        Assert.Equal((before.Folders + 1, before.Links + 1), await CountsAsync(fixture));
        fault.Enabled = true;
        using var failed = await FeatureHttpTestRequests.SendAsync(admin, create, HttpMethod.Post, "/api/workspace/content/folders", new { position, values = new { ENAME = "关系失败回滚" } });
        Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode); fault.Enabled = false;
        Assert.Equal((before.Folders + 1, before.Links + 1), await CountsAsync(fixture));
        foreach (var route in new[] { "/api/workspace/folders", "/api/workspace/folders/missing", "/api/workspace/content", "/api/links/workspace.content" })
            foreach (var method in new[] { HttpMethod.Put, HttpMethod.Delete })
            {
                using var bypass = await FeatureHttpTestRequests.SendAsync(admin, create, method, route, new { ENAME = "绕过" });
                Assert.True(bypass.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed);
            }
        using var scope = fixture.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
        var link = Assert.Single(await db.Set<Dictionary<string, object?>>("WorkspaceFolderContent").AsNoTracking().ToArrayAsync());
        Assert.StartsWith("WorkspaceFolderContent_", (string)link["LID"]!);
        Assert.NotNull(link["LCreatedBy"]); Assert.NotNull(link["LUpdatedBy"]); Assert.NotNull(link["LCreatedAt"]); Assert.NotNull(link["LUpdatedAt"]);
        var entity = await db.Set<Dictionary<string, object?>>("WorkspaceFolder").AsNoTracking().SingleAsync(row => EF.Property<string>(row, "EID") == (string)link["CEID"]!);
        Assert.Equal(entity["CreatedBy"], link["LCreatedBy"]); Assert.Equal(entity["CreatedAt"], link["LCreatedAt"]);
        Assert.Equal(position.SpaceId, entity["OriginSpaceId"]);
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "完整直属层与hasChildren交集真实对象权限，所有路径入口重验祖先")]
    public async Task DirectLayer_UsesAllReadableChildrenAndRejectsInvalidPathsBeforePaging()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync(); var entry = Entry(fixture); var create = Create(fixture);
        var root = await InitializeAsync(admin, entry);
        for (var index = 0; index < 27; index++) await CreateAsync(admin, create, root, "层" + index.ToString("D2"));
        using var costs = new SqlReadObservation(); costs.Begin();
        var all = await ListAsync(admin, entry, root, root.RootFolderId, "&sortBy=Name&sortDirection=asc&fields=LID,Name");
        output.WriteLine(JsonSerializer.Serialize(new { scenario = "WorkspaceDirectList", siblingFolders = 27, total = all.GetProperty("total").GetInt32(), materializedRelations = all.GetProperty("items").GetArrayLength(), cost = costs.End() }));
        Assert.Equal(27, all.GetProperty("total").GetInt32()); Assert.Equal(20, all.GetProperty("items").GetArrayLength());
        Assert.All(all.GetProperty("items").EnumerateArray(), row => Assert.Equal(new[] { "LID", "Name" }, row.GetProperty("fields").EnumerateObject().Select(field => field.Name).ToArray()));
        var second = await ListAsync(admin, entry, root, root.RootFolderId, "&pageIndex=2&sortBy=Name&sortDirection=asc&fields=LID,Name");
        Assert.Equal(7, second.GetProperty("items").GetArrayLength()); Assert.Equal("层20", second.GetProperty("items")[0].GetProperty("fields").GetProperty("Name").GetString());
        var first = all.GetProperty("items")[0]; var childId = first.GetProperty("associatedObject").GetProperty("id").GetString()!;
        var childPosition = root with { LinkIds = [first.GetProperty("linkId").GetString()!] };
        var grand = await CreateAsync(admin, create, childPosition, "孙文件夹");
        costs.Begin(); var tree = await FeatureHttpTestRequests.ReadAsync(admin, entry, ChildrenUrl(root));
        output.WriteLine(JsonSerializer.Serialize(new { scenario = "WorkspaceDirectLayer", siblingFolders = 27, descendantFolders = 1, materializedRelations = tree.GetProperty("items").GetArrayLength(), cost = costs.End() }));
        Assert.Equal(27, tree.GetProperty("items").GetArrayLength()); Assert.Single(tree.GetProperty("items").EnumerateArray(), row => row.GetProperty("hasChildren").GetBoolean());
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "ContentReader", "内容读取", "content-reader", "内容读取账号", entry, create);
        using var ordinary = actor.Client;
        var grantSave = Assert.Single(fixture.FeatureCatalog, p => p.BehaviorKey == "authorization.dataScope.save");
        await SaveRuleAsync(admin, grantSave, "HideParent", "EID", childId, new Dictionary<string, string> { ["read"] = "deny" });
        var filtered = await ListAsync(ordinary, entry, root, root.RootFolderId, "&pageSize=5&sortBy=Name&sortDirection=asc&fields=Name");
        Assert.Equal(26, filtered.GetProperty("total").GetInt32()); Assert.Equal(5, filtered.GetProperty("items").GetArrayLength());
        Assert.Equal("层01", filtered.GetProperty("items")[0].GetProperty("fields").GetProperty("Name").GetString());
        var hiddenTree = await FeatureHttpTestRequests.ReadAsync(ordinary, entry, ChildrenUrl(root));
        Assert.Equal(26, hiddenTree.GetProperty("items").GetArrayLength()); Assert.All(hiddenTree.GetProperty("items").EnumerateArray(), row => Assert.False(row.GetProperty("hasChildren").GetBoolean()));
        using var deniedChild = await FeatureHttpTestRequests.SendAsync(ordinary, entry, HttpMethod.Get, ChildrenUrl(childPosition));
        Assert.Equal(HttpStatusCode.NotFound, deniedChild.StatusCode);
        // 孙实体自身可读，但不允许通过祖先被拒的当前出现位置查询或写入。
        var childList = await ListAsync(admin, entry, childPosition, childId);
        var grandPosition = childPosition with { LinkIds = [.. childPosition.LinkIds, childList.GetProperty("items")[0].GetProperty("linkId").GetString()!] };
        using var deniedPath = await FeatureHttpTestRequests.SendAsync(ordinary, entry, HttpMethod.Get, PathUrl(grandPosition)); Assert.Equal(HttpStatusCode.NotFound, deniedPath.StatusCode);
        using var deniedCreate = await FeatureHttpTestRequests.SendAsync(ordinary, create, HttpMethod.Post, "/api/workspace/content/folders", new { position = grandPosition, values = new { ENAME = "不应创建" } }); Assert.Equal(HttpStatusCode.NotFound, deniedCreate.StatusCode);
        var grandDetail = await FeatureHttpTestRequests.ReadAsync(ordinary, entry, "/api/workspace/folders/" + grand.GetProperty("id").GetString()); Assert.Equal("孙文件夹", grandDetail.GetProperty("fields").GetProperty("ENAME").GetString());
        foreach (var suffix in new[] { "&fields=OriginSpaceId", "&fields=Description", "&fields=EID", "&sortBy=UpdatedAt_DisplayValue", "&unknown=1", "&Name:gte=x", "&types=Other%2FUnknown" })
        {
            using var bad = await FeatureHttpTestRequests.SendAsync(ordinary, entry, HttpMethod.Get, ListUrl(root, root.RootFolderId) + suffix); Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        }
        var injection = await ListAsync(ordinary, entry, root, root.RootFolderId, "&keyword=" + Uri.EscapeDataString("'; DROP TABLE WorkspaceFolder;--")); Assert.Equal(0, injection.GetProperty("total").GetInt32());
        using var forged = await FeatureHttpTestRequests.SendAsync(ordinary, entry, HttpMethod.Get, ListUrl(root, childId)); Assert.Equal(HttpStatusCode.NotFound, forged.StatusCode);
        using var missing = await FeatureHttpTestRequests.SendAsync(ordinary, entry, HttpMethod.Get, ChildrenUrl(root with { LinkIds = ["missing"] })); Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        var personal = await FeatureHttpTestRequests.SendAsync(ordinary, entry, HttpMethod.Post, "/api/workspace/spaces/personal/ensure"); var privateRoot = await personal.Content.ReadFromJsonAsync<JsonElement>(); personal.Dispose();
        var privatePosition = new Position(privateRoot.GetProperty("spaceId").GetString()!, privateRoot.GetProperty("rootFolderId").GetString()!, []);
        using var privateDenied = await FeatureHttpTestRequests.SendAsync(admin, entry, HttpMethod.Get, ChildrenUrl(privatePosition)); Assert.Equal(HttpStatusCode.NotFound, privateDenied.StatusCode);
        using var wrongRoot = await FeatureHttpTestRequests.SendAsync(ordinary, entry, HttpMethod.Get, ChildrenUrl(root with { RootFolderId = privatePosition.RootFolderId })); Assert.Equal(HttpStatusCode.NotFound, wrongRoot.StatusCode);
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "创建动作与写后读取资格独立，拒绝父编辑及创建候选不遗留数据")]
    public async Task CreateFolder_SeparatesActionEligibilityFromPostCommitReadReceipt()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync(); var entry = Entry(fixture); var create = Create(fixture); var root = await InitializeAsync(admin, entry);
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "ContentWriter", "内容写入", "content-writer", "内容写入账号", entry, create);
        using var ordinary = actor.Client; var save = Assert.Single(fixture.FeatureCatalog, p => p.BehaviorKey == "authorization.dataScope.save");
        await SaveRuleAsync(admin, save, "ParentNoEdit", "EID", root.RootFolderId, new Dictionary<string, string> { ["edit"] = "deny" });
        using var noEdit = await FeatureHttpTestRequests.SendAsync(ordinary, create, HttpMethod.Post, "/api/workspace/content/folders", new { position = root, values = new { ENAME = "不允许编辑父级" } }); Assert.Equal(HttpStatusCode.Forbidden, noEdit.StatusCode);
        await SaveRuleAsync(admin, save, "ParentNoEdit", "EID", "none", new Dictionary<string, string> { ["edit"] = "deny" });
        await SaveRuleAsync(admin, save, "CandidateNoCreate", "ENAME", "拒绝创建", new Dictionary<string, string> { ["create"] = "deny" });
        using var noCreate = await FeatureHttpTestRequests.SendAsync(ordinary, create, HttpMethod.Post, "/api/workspace/content/folders", new { position = root, values = new { ENAME = "拒绝创建" } }); Assert.Equal(HttpStatusCode.Forbidden, noCreate.StatusCode);
        Assert.Equal((1, 0), await CountsAsync(fixture));
        await SaveRuleAsync(admin, save, "CreatedUnreadable", "ENAME", "创建后不可读", new Dictionary<string, string> { ["read"] = "deny" });
        var receipt = await CreateAsync(ordinary, create, root, "创建后不可读"); Assert.True(receipt.GetProperty("succeeded").GetBoolean()); Assert.Single(receipt.EnumerateObject());
        var hidden = await ListAsync(ordinary, entry, root, root.RootFolderId); Assert.Equal(0, hidden.GetProperty("total").GetInt32()); Assert.Equal((2, 1), await CountsAsync(fixture));
        using var absentGrant = await FeatureHttpTestRequests.SendAsync(ordinary, entry, HttpMethod.Post, "/api/workspace/content/folders", new { position = root, values = new { ENAME = "以read位置伪装创建" } }); Assert.Equal(HttpStatusCode.Forbidden, absentGrant.StatusCode);
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "强制端点业务条件在统一计数分页前下推，声明故障安全关闭")]
    public async Task LinkQuery_IntersectsDeclaredEndpointConstraintBeforeCountAndPaging()
    {
        var constraint = new EndpointConstraint();
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(services =>
        {
            var registration = Assert.Single(services, service => service.ServiceType == typeof(CompiledPersistencePlan));
            var plan = Assert.IsType<CompiledPersistencePlan>(registration.ImplementationInstance);
            services.Remove(registration);
            services.AddSingleton(plan with { Models = plan.Models.Select(model => model.LinkQuery is not { } query ? model : model with
            { LinkQuery = query with { Endpoints = query.Endpoints.ToDictionary(pair => pair.Key, pair => pair.Value with { ConstraintKey = "test.folder.constraint" }) } }).ToArray() });
            services.AddSingleton<ILinkQueryEndpointConstraint>(constraint);
        }, includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync(); var entry = Entry(fixture); var create = Create(fixture); var root = await InitializeAsync(admin, entry);
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "ConstraintReader", "范围读取", "constraint-reader", "范围读取账号", entry, create);
        using var ordinary = actor.Client;
        var allowed = await CreateAsync(ordinary, create, root, "允许的文件夹"); var excluded = await CreateAsync(ordinary, create, root, "被范围排除");
        var list = await ListAsync(ordinary, entry, root, root.RootFolderId, "&pageSize=1");
        Assert.Equal(1, list.GetProperty("total").GetInt32()); Assert.Equal(allowed.GetProperty("id").GetString(), Assert.Single(list.GetProperty("items").EnumerateArray()).GetProperty("associatedObject").GetProperty("id").GetString());
        var independent = await FeatureHttpTestRequests.ReadAsync(ordinary, entry, "/api/workspace/folders/" + excluded.GetProperty("id").GetString()); Assert.Equal("被范围排除", independent.GetProperty("fields").GetProperty("ENAME").GetString());
        // 外层强制条件排除 B，而可信 SQL scope 的 UNION 刻意包含 B，交集仍不得放行或计数 B。
        constraint.ScopeIds = [root.RootFolderId, allowed.GetProperty("id").GetString()!, excluded.GetProperty("id").GetString()!];
        var scopedList = await ListAsync(ordinary, entry, root, root.RootFolderId, "&pageSize=1");
        Assert.Equal(1, scopedList.GetProperty("total").GetInt32());
        var allowedRow = Assert.Single(scopedList.GetProperty("items").EnumerateArray());
        Assert.Equal(allowed.GetProperty("id").GetString(), allowedRow.GetProperty("associatedObject").GetProperty("id").GetString());
        var add = Assert.Single(fixture.FeatureCatalog, point => point.BehaviorKey == "workspace.folder.add");
        constraint.AllowExcluded = true;
        using var attached = await FeatureHttpTestRequests.SendAsync(admin, add, HttpMethod.Post, "/api/workspace/content/references",
            new { position = root with { LinkIds = [allowedRow.GetProperty("linkId").GetString()!] }, target = new { modelKey = "WorkspaceFolder", id = excluded.GetProperty("id").GetString() } });
        Assert.Equal(HttpStatusCode.OK, attached.StatusCode);
        constraint.AllowExcluded = false;
        using var excludedWrite = await FeatureHttpTestRequests.SendAsync(admin, add, HttpMethod.Post, "/api/workspace/content/references",
            new { position = root with { LinkIds = [allowedRow.GetProperty("linkId").GetString()!] }, target = new { modelKey = "WorkspaceFolder", id = excluded.GetProperty("id").GetString() } });
        Assert.Equal(HttpStatusCode.NotFound, excludedWrite.StatusCode);
        var tree = await FeatureHttpTestRequests.ReadAsync(ordinary, entry, ChildrenUrl(root));
        var treeRow = Assert.Single(tree.GetProperty("items").EnumerateArray());
        Assert.Equal(1, tree.GetProperty("total").GetInt32()); Assert.False(treeRow.GetProperty("hasChildren").GetBoolean());
        constraint.ExcludeRoot = true;
        using var deniedAnchor = await FeatureHttpTestRequests.SendAsync(ordinary, entry, HttpMethod.Get, ListUrl(root, root.RootFolderId));
        Assert.Equal(HttpStatusCode.NotFound, deniedAnchor.StatusCode); constraint.ExcludeRoot = false;
        var searched = await ListAsync(ordinary, entry, root, root.RootFolderId, "&keyword=" + Uri.EscapeDataString("被范围排除")); Assert.Equal(0, searched.GetProperty("total").GetInt32());
        constraint.Fail = true;
        using var failed = await FeatureHttpTestRequests.SendAsync(ordinary, entry, HttpMethod.Get, ListUrl(root, root.RootFolderId)); Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
        constraint.Fail = false; constraint.CannotTranslate = true;
        using var untranslatable = await FeatureHttpTestRequests.SendAsync(ordinary, entry, HttpMethod.Get, ListUrl(root, root.RootFolderId)); Assert.Equal(HttpStatusCode.InternalServerError, untranslatable.StatusCode);
        constraint.CannotTranslate = false;
        Assert.Equal(1, (await ListAsync(ordinary, entry, root, root.RootFolderId)).GetProperty("total").GetInt32());
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "异常目标在分页前排除，未知存储类型失败关闭且读取无修复写入")]
    public async Task LinkQuery_ExcludesMissingEndpointAndRejectsUnknownStoredTypeWithoutRepair()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync(); var entry = Entry(fixture); var root = await InitializeAsync(admin, entry);
        await CreateAsync(admin, Create(fixture), root, "真实端点");
        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
            var example = await db.Set<Dictionary<string, object?>>("WorkspaceFolderContent").AsNoTracking().SingleAsync();
            db.Set<Dictionary<string, object?>>("WorkspaceFolderContent").Add(new(example) { ["LID"] = "broken-link", ["CEID"] = "missing-entity" }); await db.SaveChangesAsync();
        }
        var list = await ListAsync(admin, entry, root, root.RootFolderId, "&pageSize=1"); Assert.Equal(1, list.GetProperty("total").GetInt32()); Assert.Single(list.GetProperty("items").EnumerateArray());
        Assert.Equal((2, 2), await CountsAsync(fixture));
        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
            var broken = await db.Set<Dictionary<string, object?>>("WorkspaceFolderContent").SingleAsync(row => EF.Property<string>(row, "LID") == "broken-link");
            broken["ChildModelKey"] = "Unknown/Entity"; await db.SaveChangesAsync();
        }
        using var failed = await FeatureHttpTestRequests.SendAsync(admin, entry, HttpMethod.Get, ListUrl(root, root.RootFolderId)); Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
        Assert.Equal((2, 2), await CountsAsync(fixture));
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "事务已提交后展示故障返回受控成功，重读不重复创建")]
    public async Task CreateFolder_PostCommitReadFailureReturnsReceiptAndDoesNotReplayWrite()
    {
        var fault = new PostCommitReadFault();
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(services => services.AddDbContext<EfPlatformDbContext>((_, options) => options.AddInterceptors(fault, fault.Commit)), includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync(); var entry = Entry(fixture); var root = await InitializeAsync(admin, entry);
        fault.Arm = true;
        var receipt = await CreateAsync(admin, Create(fixture), root, "提交后读失败");
        Assert.True(receipt.GetProperty("succeeded").GetBoolean()); Assert.Single(receipt.EnumerateObject());
        fault.Enabled = false;
        Assert.Equal((2, 1), await CountsAsync(fixture));
        Assert.Equal(1, (await ListAsync(admin, entry, root, root.RootFolderId)).GetProperty("total").GetInt32());
        Assert.Equal(1, (await ListAsync(admin, entry, root, root.RootFolderId)).GetProperty("total").GetInt32());
        Assert.Equal((2, 1), await CountsAsync(fixture));
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "父节点保持可见时hasChildren不泄露唯一不可读子文件夹")]
    public async Task HasChildren_DoesNotExposeTheOnlyUnreadableChildOfAReadableFolder()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync(); var entry = Entry(fixture); var create = Create(fixture); var root = await InitializeAsync(admin, entry);
        await CreateAsync(admin, create, root, "可读父文件夹");
        var parent = Assert.Single((await ListAsync(admin, entry, root, root.RootFolderId)).GetProperty("items").EnumerateArray());
        var parentPosition = root with { LinkIds = [parent.GetProperty("linkId").GetString()!] };
        var child = await CreateAsync(admin, create, parentPosition, "唯一子文件夹");
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "ChildReader", "子节点读取", "child-reader", "子节点读取账号", entry);
        using var ordinary = actor.Client;
        Assert.True(Assert.Single((await FeatureHttpTestRequests.ReadAsync(ordinary, entry, ChildrenUrl(root))).GetProperty("items").EnumerateArray()).GetProperty("hasChildren").GetBoolean());
        var save = Assert.Single(fixture.FeatureCatalog, point => point.BehaviorKey == "authorization.dataScope.save");
        await SaveRuleAsync(admin, save, "HiddenOnlyChild", "EID", child.GetProperty("id").GetString()!, new Dictionary<string, string> { ["read"] = "deny" });
        var visibleParent = Assert.Single((await FeatureHttpTestRequests.ReadAsync(ordinary, entry, ChildrenUrl(root))).GetProperty("items").EnumerateArray());
        Assert.Equal(parent.GetProperty("linkId").GetString(), visibleParent.GetProperty("linkId").GetString()); Assert.False(visibleParent.GetProperty("hasChildren").GetBoolean());
        Assert.Empty((await FeatureHttpTestRequests.ReadAsync(ordinary, entry, ChildrenUrl(parentPosition))).GetProperty("items").EnumerateArray());
        Assert.True(Assert.Single((await FeatureHttpTestRequests.ReadAsync(admin, entry, ChildrenUrl(root))).GetProperty("items").EnumerateArray()).GetProperty("hasChildren").GetBoolean());
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "创建成功后的真实roots刷新返回可展开根，不把已提交创建改报刷新失败")]
    public async Task Roots_AfterFolderCreation_ReturnsTheRootAsExpandable()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync(); var entry = Entry(fixture); var root = await InitializeAsync(admin, entry);
        Assert.False(Assert.Single((await FeatureHttpTestRequests.ReadAsync(admin, entry, "/api/workspace/spaces")).EnumerateArray()).GetProperty("hasChildren").GetBoolean());
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "RefreshWriter", "刷新写入", "refresh-writer", "刷新写入账号", entry, Create(fixture));
        using var ordinary = actor.Client;
        await CreateAsync(ordinary, Create(fixture), root, "刷新后可展开");
        Assert.Equal(1, (await ListAsync(ordinary, entry, root, root.RootFolderId)).GetProperty("total").GetInt32());
        using var refreshed = await FeatureHttpTestRequests.SendAsync(ordinary, entry, HttpMethod.Get, "/api/workspace/spaces");
        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
        Assert.True(Assert.Single((await refreshed.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray()).GetProperty("hasChildren").GetBoolean());
        using var path = await FeatureHttpTestRequests.SendAsync(ordinary, entry, HttpMethod.Get, PathUrl(root));
        Assert.Equal(HttpStatusCode.OK, path.StatusCode);
        var restored = await path.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(root.RootFolderId, Assert.Single(restored.GetProperty("folderIds").EnumerateArray()).GetString());
    }

    private sealed record Position(string SpaceId, string RootFolderId, string[] LinkIds);
    private static FeaturePoint Entry(BusinessAuditHostFixture fixture) => Assert.Single(fixture.FeatureCatalog, p => p.ModuleId == "Workspace" && p.Key == "workspace");
    private static FeaturePoint Create(BusinessAuditHostFixture fixture) => Assert.Single(fixture.FeatureCatalog, p => p.BehaviorKey == "workspace.folder.create");
    private static async Task<Position> InitializeAsync(HttpClient client, FeaturePoint entry)
    {
        using var response = await FeatureHttpTestRequests.SendAsync(client, entry, HttpMethod.Post, "/api/workspace/spaces/public/initialize"); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var root = await response.Content.ReadFromJsonAsync<JsonElement>(); return new(root.GetProperty("spaceId").GetString()!, root.GetProperty("rootFolderId").GetString()!, []);
    }
    private static async Task<JsonElement> CreateAsync(HttpClient client, FeaturePoint create, Position position, string name)
    {
        using var response = await FeatureHttpTestRequests.SendAsync(client, create, HttpMethod.Post, "/api/workspace/content/folders", new { position, values = new { ENAME = name } });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
    private static string ListUrl(Position position, string anchor) => "/api/links/workspace.content?direction=forward&anchorModelKey=WorkspaceFolder&anchorId=" + Uri.EscapeDataString(anchor)
        + "&queryScope=" + Uri.EscapeDataString(JsonSerializer.Serialize(new { spaceId = position.SpaceId, rootFolderId = position.RootFolderId, linkIds = JsonSerializer.Serialize(position.LinkIds) }));
    private static Task<JsonElement> ListAsync(HttpClient client, FeaturePoint entry, Position position, string anchor, string suffix = "") => FeatureHttpTestRequests.ReadAsync(client, entry, ListUrl(position, anchor) + suffix);
    private static string ChildrenUrl(Position position) => "/api/workspace/content/children?spaceId=" + Uri.EscapeDataString(position.SpaceId) + "&rootFolderId=" + Uri.EscapeDataString(position.RootFolderId) + "&linkIds=" + Uri.EscapeDataString(JsonSerializer.Serialize(position.LinkIds));
    private static string PathUrl(Position position) => ChildrenUrl(position).Replace("/children?", "/path?", StringComparison.Ordinal);
    private static async Task<(int Folders, int Links)> CountsAsync(BusinessAuditHostFixture fixture)
    {
        using var scope = fixture.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
        return (await db.Set<Dictionary<string, object?>>("WorkspaceFolder").CountAsync(), await db.Set<Dictionary<string, object?>>("WorkspaceFolderContent").CountAsync());
    }
    private static async Task SaveRuleAsync(HttpClient admin, FeaturePoint save, string id, string field, string value, Dictionary<string, string> actions)
    {
        var rule = new DataScopeRuleDto(id, id, null, id, true, "workspace.folder", new("attribute", field, "equal", JsonSerializer.SerializeToElement(value)), [new(id + "Subject", "all", null, 0, actions)], []);
        using var response = await FeatureHttpTestRequests.SendAsync(admin, save, HttpMethod.Put, "/api/authorization/data-scope/rules/" + id, rule); Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }
    private sealed class LinkInsertFault : DbCommandInterceptor
    {
        public bool Enabled { get; set; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (Enabled && command.CommandText.Contains("INSERT INTO", StringComparison.Ordinal) && command.CommandText.Contains("WorkspaceFolderContent", StringComparison.Ordinal)) throw new InvalidOperationException("WORKSPACE_TEST_LINK_INSERT_FAILURE");
            return ValueTask.FromResult(result);
        }
    }

    private sealed class EndpointConstraint : ILinkQueryEndpointConstraint
    {
        public string Key => "test.folder.constraint";
        public bool Fail { get; set; }
        public bool CannotTranslate { get; set; }
        public bool ExcludeRoot { get; set; }
        public bool AllowExcluded { get; set; }
        public string[]? ScopeIds { get; set; }
        public Task<Expression<Func<Dictionary<string, object?>, bool>>> ReadPredicateAsync(LinkQueryEndpoint endpoint, CancellationToken token)
        {
            if (Fail) throw new InvalidOperationException("TEST_REQUIRED_CONDITION_FAILED");
            Expression<Func<Dictionary<string, object?>, bool>> predicate = CannotTranslate ? row => OnlyInMemory(EF.Property<string>(row, "ENAME"))
                : row => (AllowExcluded || EF.Property<string>(row, "ENAME") != "被范围排除") && (!ExcludeRoot || EF.Property<string>(row, "ENAME") != "公共空间");
            return Task.FromResult(predicate);
        }
        public Task<LinkQuerySqlScope?> ReadSqlScopeAsync(LinkQueryEndpoint endpoint, string scopeName, CancellationToken token)
        {
            if (ScopeIds is null) return Task.FromResult<LinkQuerySqlScope?>(null);
            var parameters = ScopeIds.Select((id, index) => new SqlParameter("@" + scopeName + "Test" + index, id)).ToArray();
            return Task.FromResult<LinkQuerySqlScope?>(new("", string.Join(" UNION ", parameters.Select(parameter => "SELECT " + parameter.ParameterName)), parameters));
        }
        private static bool OnlyInMemory(string name) => name.Length > 0;
    }

    private sealed class PostCommitReadFault : DbCommandInterceptor
    {
        public bool Arm { get; set; }
        public bool Enabled { get; set; }
        public DbTransactionInterceptor Commit => new CommitSignal(this);
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (Enabled && command.CommandText.Contains("SELECT", StringComparison.Ordinal) && command.CommandText.Contains("WorkspaceFolder", StringComparison.Ordinal)) throw new InvalidOperationException("WORKSPACE_TEST_POST_COMMIT_READ_FAILURE");
            return ValueTask.FromResult(result);
        }
        private sealed class CommitSignal(PostCommitReadFault fault) : DbTransactionInterceptor
        {
            public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
            {
                if (fault.Arm) { fault.Arm = false; fault.Enabled = true; }
                return Task.CompletedTask;
            }
        }
    }

    // SqlClient diagnostics 同时观察 EF 与通用 reader 的真实指令；仅给代表查询启用 IO/TIME，不记录参数或凭据。
    private sealed class SqlReadObservation : IObserver<DiagnosticListener>, IObserver<KeyValuePair<string, object?>>, IDisposable
    {
        private readonly IDisposable _all;
        private readonly List<IDisposable> _subscriptions = [];
        private readonly ConcurrentQueue<string> _commands = new();
        private readonly ConcurrentQueue<string> _statistics = new();
        private readonly Stopwatch _clock = new();
        private volatile bool _active;
        public SqlReadObservation() => _all = DiagnosticListener.AllListeners.Subscribe(this);
        public void Begin() { _commands.Clear(); _statistics.Clear(); _clock.Restart(); _active = true; }
        public object End() { _active = false; _clock.Stop(); return new { databaseCommands = _commands.Count, directLinkCommands = _commands.Count(kind => kind == "LinkRelations"), elapsedMs = _clock.Elapsed.TotalMilliseconds, sqlStatistics = _statistics.ToArray() }; }
        public void OnNext(DiagnosticListener value) { if (value.Name == "SqlClientDiagnosticListener") _subscriptions.Add(value.Subscribe(this)); }
        public void OnNext(KeyValuePair<string, object?> value)
        {
            if (!_active || value.Value?.GetType().GetProperty("Command")?.GetValue(value.Value) is not DbCommand command) return;
            var link = command.CommandText.Contains("WITH Relations AS", StringComparison.Ordinal);
            if (value.Key.EndsWith("WriteCommandAfter", StringComparison.Ordinal)) _commands.Enqueue(link ? "LinkRelations" : "OtherSql");
            if (link && value.Key.EndsWith("WriteCommandBefore", StringComparison.Ordinal) && command.Connection is SqlConnection connection)
            {
                connection.InfoMessage += (_, arguments) => { if (_active) _statistics.Enqueue(arguments.Message); };
                command.CommandText = "SET STATISTICS IO ON; SET STATISTICS TIME ON; " + command.CommandText + " SET STATISTICS IO OFF; SET STATISTICS TIME OFF;";
            }
        }
        public void OnCompleted() { }
        public void OnError(Exception error) { }
        public void Dispose() { _active = false; foreach (var subscription in _subscriptions) subscription.Dispose(); _all.Dispose(); }
    }
}
