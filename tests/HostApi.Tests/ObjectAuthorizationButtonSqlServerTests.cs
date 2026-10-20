using System.Net;
using System.Net.Http.Json;
using System.Data.Common;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Text.Json;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static ICP.Platform.HostApi.Tests.EntityOwnershipBackfillSqlServerHttpApiTests;

namespace ICP.Platform.HostApi.Tests;

public sealed class ObjectAuthorizationButtonSqlServerTests
{
    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "等待 ACL 锁期间撤权后不得写入；成功提交自身失权不追加回读")]
    public async Task GenericSave_RechecksManagementAfterAclWaitAndAllowsCommittedSelfRevocation()
    {
        var gate = new AclAttemptGate();
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(services =>
            services.AddDbContext<EfPlatformDbContext>((_, options) => options.AddInterceptors(gate)), includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        await InitializeAsync(fixture, admin);
        var position = await ContainerAsync(fixture, admin);
        var part = await CreateAsync(fixture, admin, position, "Part", "parts", "createPart", "Auth329AclWait");
        var http = new FeatureHttpTestRequests(fixture);
        var manage = Assert.Single(fixture.FeatureCatalog, point => point.ModuleId == "Part" && point.Key == "authorize"
            && point.ActionScope == "list.rowActions");
        var declaration = http.Declaration("part.part.authorize", manage);
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "Auth329AclRole", "锁内授权角色",
            "auth329.acl.actor", "锁内授权主体", Point(fixture, "part.part.read"), manage);
        using var ordinary = actor.Client;
        var path = "/api/authorization/data-scope/objects/part.part/" + part.Id;
        DataScopeObjectConfiguration Configuration(string authorize) => new("part.part", part.Id,
            [new("Auth329AclSubject", "user", actor.AccountId, 1,
                new Dictionary<string, string> { ["read"] = "allow", ["authorize"] = authorize })]);
        using (var allowed = await FeatureHttpTestRequests.SendAsync(admin, manage, HttpMethod.Put, path, Configuration("allow"), declaration))
            Assert.Equal(HttpStatusCode.NoContent, allowed.StatusCode);
        gate.Context = fixture.Services.GetRequiredService<IHttpContextAccessor>();
        gate.ActorId = actor.AccountId;
        gate.Target = "Authorization.DataScopeObject:part.part:" + part.Id;
        gate.Active = true;
        var pending = FeatureHttpTestRequests.SendAsync(ordinary, manage, HttpMethod.Put, path,
            new DataScopeObjectConfiguration("part.part", part.Id, []), declaration);
        try
        {
            await gate.Attempt.Task.WaitAsync(TimeSpan.FromSeconds(30));
            // 正式管理员请求在先前请求真正取 ACL 锁前提交撤权，不把前置功能准入当作允许缓存。
            using (var revoked = await FeatureHttpTestRequests.SendAsync(admin, manage, HttpMethod.Put, path, Configuration("deny"), declaration))
                Assert.Equal(HttpStatusCode.NoContent, revoked.StatusCode);
            var before = await SnapshotAsync(fixture, part.Id);
            gate.Release.TrySetResult();
            using var denied = await pending.WaitAsync(TimeSpan.FromSeconds(30));
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            Assert.Equal(before, await SnapshotAsync(fixture, part.Id));
        }
        finally
        {
            gate.Active = false;
            gate.Release.TrySetResult();
            using var completed = await pending;
        }
        using (var restored = await FeatureHttpTestRequests.SendAsync(admin, manage, HttpMethod.Put, path, Configuration("allow"), declaration))
            Assert.Equal(HttpStatusCode.NoContent, restored.StatusCode);
        using (var selfRevoked = await FeatureHttpTestRequests.SendAsync(ordinary, manage, HttpMethod.Put, path, Configuration("deny"), declaration))
            Assert.Equal(HttpStatusCode.NoContent, selfRevoked.StatusCode);
        var committed = await SnapshotAsync(fixture, part.Id);
        using (var next = await FeatureHttpTestRequests.SendAsync(ordinary, manage, HttpMethod.Put, path, Configuration("allow"), declaration))
            Assert.Equal(HttpStatusCode.Forbidden, next.StatusCode);
        Assert.Equal(committed, await SnapshotAsync(fixture, part.Id));
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "专用Folder不暴露CRUD仍可授权，功能撤权后generic拒绝且无变化")]
    public async Task GenericFolderSave_UsesDedicatedObjectFactsAndRejectsRevokedFeatureWithoutWrites()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        await InitializeAsync(fixture, admin);
        var position = await ContainerAsync(fixture, admin);
        var folder = await CreateAsync(fixture, admin, position, "WorkspaceFolder", "folders", "create", "Auth329DedicatedFolder");
        using (var scope = fixture.Services.CreateScope())
            Assert.Null(scope.ServiceProvider.GetRequiredService<ICP.Platform.Crud.Abstractions.IStandardCrudResourceRegistry>()
                .FindByResourceKey("workspace.folder"));
        var http = new FeatureHttpTestRequests(fixture);
        var manage = Assert.Single(fixture.FeatureCatalog, point => point.ModuleId == "Workspace" && point.Key == "authorize"
            && point.BehaviorKey == "workspace.folder.authorize");
        var declaration = http.Declaration("workspace.folder.authorize", manage);
        var access = Point(fixture, "workspace.spaces.access");
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "Auth329FolderRole", "文件夹授权角色",
            "auth329.folder.actor", "文件夹授权主体", access, manage);
        using var ordinary = actor.Client;
        var path = "/api/authorization/data-scope/objects/workspace.folder/" + folder.Id;
        var input = new DataScopeObjectConfiguration("workspace.folder", folder.Id,
            [new("Auth329FolderSubject", "user", actor.AccountId, 1,
                new Dictionary<string, string> { ["read"] = "allow", ["authorize"] = "allow" })]);
        using (var saved = await FeatureHttpTestRequests.SendAsync(admin, manage, HttpMethod.Put, path, input, declaration))
            Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);
        using (var current = await FeatureHttpTestRequests.SendAsync(ordinary, manage, HttpMethod.Get, path, dataAccess: declaration))
            Assert.Equal(HttpStatusCode.OK, current.StatusCode);
        await http.GrantAsync(admin, actor.RoleId, access);
        var before = await SnapshotAsync(fixture, folder.Id, "workspace.folder");
        using (var revoked = await FeatureHttpTestRequests.SendAsync(ordinary, manage, HttpMethod.Put, path,
            input with { Subjects = [] }, declaration))
            Assert.Equal(HttpStatusCode.Forbidden, revoked.StatusCode);
        using (var revokedRead = await FeatureHttpTestRequests.SendAsync(ordinary, manage, HttpMethod.Get, path + "/catalog", dataAccess: declaration))
            Assert.Equal(HttpStatusCode.Forbidden, revokedRead.StatusCode);
        Assert.Equal(before, await SnapshotAsync(fixture, folder.Id, "workspace.folder"));
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "授权输入的真实独立read来源保留目录读取，完整管理不降级")]
    public async Task ActualPartInputReadSource_AllowsAuxiliaryReadsButNeverFullManagement()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        await InitializeAsync(fixture, admin);
        var position = await ContainerAsync(fixture, admin);
        var part = await CreateAsync(fixture, admin, position, "Part", "parts", "createPart", "Auth329ReadInput");
        var http = new FeatureHttpTestRequests(fixture);
        var manage = Assert.Single(fixture.FeatureCatalog, point => point.ModuleId == "Part" && point.Key == "authorize"
            && point.ActionScope == "list.rowActions");
        var button = http.Declaration("part.part.authorize", manage);
        var read = Assert.Single(fixture.Services.GetRequiredService<ICP.Platform.Kernel.ModulePlanning.CanonicalModulePlan>()
            .ActionDataAccessCatalog, item => item.ViewKey == "part.part.permissions" && item.Surface == "read"
                && item.FeaturePositions.Contains(manage.Position));
        Assert.Null(read.BusinessBinding);
        Assert.Equal(new[] { "read" }, read.Actions);
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "Auth329InputReadRole", "输入读取角色",
            "auth329.input.read", "输入读取主体", Point(fixture, "part.part.read"), manage);
        using var ordinary = actor.Client;
        var path = "/api/part/parts/" + part.Id + "/permissions";
        using (var configured = await FeatureHttpTestRequests.SendAsync(admin, manage, HttpMethod.Put, path,
            new DataScopeObjectConfiguration("part.part", part.Id, [new("Auth329ReadSubject", "user", actor.AccountId, 1,
                new Dictionary<string, string> { ["read"] = "allow", ["authorize"] = "deny" })]), button))
            Assert.Equal(HttpStatusCode.NoContent, configured.StatusCode);
        var before = await SnapshotAsync(fixture, part.Id);
        foreach (var suffix in new[] { "/catalog", "/explanation", "/subjects/user?page=1" })
        {
            using var allowed = await FeatureHttpTestRequests.SendAsync(ordinary, manage, HttpMethod.Get, path + suffix, dataAccess: read);
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        }
        foreach (var suffix in new[] { "", "/eligibility", "/subjects/role?page=1" })
        {
            using var denied = await FeatureHttpTestRequests.SendAsync(ordinary, manage, HttpMethod.Get, path + suffix, dataAccess: button);
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        }
        using (var borrowed = await FeatureHttpTestRequests.SendAsync(ordinary, manage, HttpMethod.Get, path, dataAccess: read))
            Assert.Equal(HttpStatusCode.BadRequest, borrowed.StatusCode);
        Assert.Equal(before, await SnapshotAsync(fixture, part.Id));
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "批量准备的显式读取引用允许空筛选并保留管理拒绝反馈")]
    public async Task WorkspaceBatchPrepare_UsesActualInputReadSourceAndKeepsManagementFeedback()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        await InitializeAsync(fixture, admin);
        var position = await ContainerAsync(fixture, admin);
        var part = await CreateAsync(fixture, admin, position, "Part", "parts", "createPart", "Auth329BatchRead");
        var http = new FeatureHttpTestRequests(fixture);
        var batch = Assert.Single(fixture.FeatureCatalog, point => point.ModuleId == "Workspace" && point.Key == "batchAuthorize"
            && point.ActionScope == "list.pageActions");
        var read = Assert.Single(fixture.Services.GetRequiredService<ICP.Platform.Kernel.ModulePlanning.CanonicalModulePlan>()
            .ActionDataAccessCatalog, item => item.ViewKey == "workspace.content.permissions" && item.Surface == "read"
                && item.FeaturePositions.Contains(batch.Position));
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "Auth329BatchReadRole", "批量准备角色",
            "auth329.batch.read", "批量准备主体", Point(fixture, "workspace.spaces.access"), batch);
        using var ordinary = actor.Client;
        var partManage = Assert.Single(fixture.FeatureCatalog, point => point.ModuleId == "Part" && point.Key == "authorize"
            && point.ActionScope == "list.rowActions");
        using (var configured = await FeatureHttpTestRequests.SendAsync(admin, partManage, HttpMethod.Put,
            "/api/part/parts/" + part.Id + "/permissions", new DataScopeObjectConfiguration("part.part", part.Id,
                [new("Auth329BatchReadSubject", "user", actor.AccountId, 1,
                    new Dictionary<string, string> { ["read"] = "allow", ["authorize"] = "deny" })]),
            http.Declaration("part.part.authorize", partManage)))
            Assert.Equal(HttpStatusCode.NoContent, configured.StatusCode);
        var before = await SnapshotAsync(fixture, part.Id);
        var sources = new[] { new { position, linkIds = new[] { part.LinkId } } };
        const string path = "/api/workspace/content/permissions/batch/prepare";
        using (var empty = await FeatureHttpTestRequests.SendAsync(ordinary, batch, HttpMethod.Post, path,
            new { sources, filters = new { resourceKeys = new[] { "document.document" } } }, read))
        {
            var payload = await empty.Content.ReadAsStringAsync();
            Assert.True(empty.StatusCode == HttpStatusCode.OK, $"HTTP {(int)empty.StatusCode}: {payload}");
            var body = JsonSerializer.Deserialize<JsonElement>(payload);
            Assert.Empty(body.GetProperty("items").EnumerateArray());
            Assert.Equal(0, body.GetProperty("total").GetInt32());
        }
        using (var actual = await FeatureHttpTestRequests.SendAsync(ordinary, batch, HttpMethod.Post, path, new { sources }, read))
        {
            Assert.Equal(HttpStatusCode.OK, actual.StatusCode);
            var item = Assert.Single((await actual.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("items").EnumerateArray());
            Assert.Equal(part.Id, item.GetProperty("objectId").GetString());
            Assert.False(item.GetProperty("manageable").GetBoolean());
        }
        using (var missing = await FeatureHttpTestRequests.SendAsync(ordinary, batch, HttpMethod.Post, path, new { sources }))
            Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        await http.GrantAsync(admin, actor.RoleId, Point(fixture, "workspace.spaces.access"));
        using (var revoked = await FeatureHttpTestRequests.SendAsync(ordinary, batch, HttpMethod.Post, path,
            new { sources, filters = new { resourceKeys = new[] { "document.document" } } }, read))
            Assert.Equal(HttpStatusCode.Forbidden, revoked.StatusCode);
        Assert.Equal(before, await SnapshotAsync(fixture, part.Id));
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "批量后项锁前撤权整批无写；成功自身失权只返回安全回执")]
    public async Task WorkspaceBatchSave_RechecksLaterTargetAfterAclWaitAndCommitsSelfRevocation()
    {
        var gate = new AclAttemptGate();
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(services =>
            services.AddDbContext<EfPlatformDbContext>((_, options) => options.AddInterceptors(gate)), includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        await InitializeAsync(fixture, admin);
        var position = await ContainerAsync(fixture, admin);
        var part = await CreateAsync(fixture, admin, position, "Part", "parts", "createPart", "Auth329BatchAclPart");
        var document = await CreateAsync(fixture, admin, position, "Document", "documents", "createDocument", "Auth329BatchAclDoc");
        var http = new FeatureHttpTestRequests(fixture);
        var batch = Assert.Single(fixture.FeatureCatalog, point => point.ModuleId == "Workspace" && point.Key == "batchAuthorize"
            && point.ActionScope == "list.pageActions");
        var button = http.Declaration("workspace.folder.authorize", batch);
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "Auth329BatchAclRole", "批量锁内角色",
            "auth329.batch.acl", "批量锁内主体", Point(fixture, "workspace.spaces.access"), batch);
        using var ordinary = actor.Client;
        var targets = new[] { (Resource: "document.document", Model: "Document", Id: document.Id),
            (Resource: "part.part", Model: "Part", Id: part.Id) };
        foreach (var target in targets) await ConfigureAsync(target, "allow");
        object BatchInput(string authorize) => new { mode = "R", sources = new[] { new { position, linkIds = new[] { part.LinkId, document.LinkId } } },
            resources = targets.Select(target => new DataScopeBatchResourceConfiguration(target.Resource,
                [new("user", actor.AccountId, 7, new Dictionary<string, string> { ["read"] = "allow", ["authorize"] = authorize })])).ToArray() };
        gate.Context = fixture.Services.GetRequiredService<IHttpContextAccessor>();
        gate.ActorId = actor.AccountId;
        gate.Target = "Authorization.DataScopeObject:part.part:" + part.Id;
        gate.Active = true;
        const string path = "/api/workspace/content/permissions/batch";
        var pending = FeatureHttpTestRequests.SendAsync(ordinary, batch, HttpMethod.Post, path, BatchInput("allow"), button);
        try
        {
            // 原排序使Document先锁、Part后锁；管理员只写Part，不需要被本批次持有的图锁。
            var reached = await Task.WhenAny(pending, gate.Attempt.Task).WaitAsync(TimeSpan.FromSeconds(30));
            if (ReferenceEquals(reached, pending))
            {
                using var early = await pending;
                Assert.Fail($"批量尚未进入真实ACL保护点：HTTP {(int)early.StatusCode}: {await early.Content.ReadAsStringAsync()}");
            }
            await ConfigureAsync(targets[1], "deny");
            var before = await SnapshotAllAsync();
            gate.Release.TrySetResult();
            using var denied = await pending.WaitAsync(TimeSpan.FromSeconds(30));
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            Assert.Equal(before, await SnapshotAllAsync());
        }
        finally
        {
            gate.Active = false;
            gate.Release.TrySetResult();
            using var completed = await pending;
        }
        await ConfigureAsync(targets[1], "allow");
        using (var committed = await FeatureHttpTestRequests.SendAsync(ordinary, batch, HttpMethod.Post, path, BatchInput("deny"), button))
        {
            Assert.Equal(HttpStatusCode.OK, committed.StatusCode);
            var receipt = await committed.Content.ReadFromJsonAsync<JsonElement>();
            Assert.True(receipt.GetProperty("succeeded").GetBoolean());
            Assert.Equal(2, receipt.GetProperty("targets").GetInt32());
            Assert.Equal(2, receipt.GetProperty("changed").GetInt32());
        }
        var saved = await SnapshotAllAsync();
        using (var next = await FeatureHttpTestRequests.SendAsync(ordinary, batch, HttpMethod.Post, path, BatchInput("allow"), button))
            Assert.Equal(HttpStatusCode.Forbidden, next.StatusCode);
        Assert.Equal(saved, await SnapshotAllAsync());

        async Task ConfigureAsync((string Resource, string Model, string Id) target, string authorize)
        {
            var manage = Assert.Single(fixture.FeatureCatalog, point => point.ModuleId == target.Model && point.Key == "authorize"
                && point.ActionScope == "list.rowActions");
            using var configured = await FeatureHttpTestRequests.SendAsync(admin, manage, HttpMethod.Put,
                "/api/authorization/data-scope/objects/" + target.Resource + "/" + target.Id,
                new DataScopeObjectConfiguration(target.Resource, target.Id, [new("Auth329BatchAcl" + target.Model, "user", actor.AccountId, 1,
                    new Dictionary<string, string> { ["read"] = "allow", ["authorize"] = authorize })]), http.Declaration(target.Resource + ".authorize", manage));
            Assert.Equal(HttpStatusCode.NoContent, configured.StatusCode);
        }
        async Task<string> SnapshotAllAsync() => string.Join("\n", await SnapshotAsync(fixture, document.Id, "document.document"),
            await SnapshotAsync(fixture, part.Id));
    }

    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "实际只读按钮绑定独立GET实现，不能借其身份保存且原authorize资格不变")]
    public async Task ExampleInspection_SeparatesReadBusinessFromSaveAndPreservesItsQualification()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeExample: true);
        using var admin = await fixture.LoginAdministratorAsync();
        var http = new FeatureHttpTestRequests(fixture);
        var create = http.Example("create");
        var created = await http.CreateButtonAsync(admin, create, "/api/example/records",
            new { ECODE = "AUTH329-INSPECTION", ENAME = "只读授权实现", RecordSource = "Manual" });
        var id = BusinessAuditSqlServerHttpApiTests.Id(created);
        var inspect = http.Example("viewAuthorization");
        var manage = http.Example("authorize");
        Assert.Equal("example.record.authorize", inspect.BehaviorKey);
        var source = http.Declaration("example.record.viewAuthorization", inspect);
        Assert.Equal(new[] { "read", "authorize" }, source.Actions);
        var manager = http.Declaration("example.record.authorize", manage);
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "Auth329InspectRole", "只读授权角色",
            "auth329.inspect", "只读授权主体", http.Example(), inspect);
        using var ordinary = actor.Client;
        var original = "/api/authorization/data-scope/objects/example.record/" + id;
        var path = original + "/inspection";
        DataScopeObjectConfiguration Configuration(string read, string authorize) => new("example.record", id,
            [new("Auth329InspectionSubject", "user", actor.AccountId, 1,
                new Dictionary<string, string> { ["read"] = read, ["authorize"] = authorize })]);
        await ConfigureAsync("allow", "allow");
        var before = await SnapshotAsync(fixture, id, "example.record");
        foreach (var suffix in new[] { "", "/eligibility", "/catalog", "/explanation", "/subjects/user?page=1" })
        {
            using var read = await FeatureHttpTestRequests.SendAsync(ordinary, inspect, HttpMethod.Get, path + suffix, dataAccess: source);
            Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        }
        using (var borrowed = await FeatureHttpTestRequests.SendAsync(ordinary, inspect, HttpMethod.Put, original, Configuration("deny", "deny"), source))
            Assert.Equal(HttpStatusCode.BadRequest, borrowed.StatusCode);
        using (var borrowedManager = await FeatureHttpTestRequests.SendAsync(admin, manage, HttpMethod.Get, path + "/catalog", dataAccess: manager))
            Assert.Equal(HttpStatusCode.BadRequest, borrowedManager.StatusCode);
        using (var missing = await FeatureHttpTestRequests.SendAsync(ordinary, inspect, HttpMethod.Get, path + "/catalog"))
            Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Equal(before, await SnapshotAsync(fixture, id, "example.record"));
        await ConfigureAsync("deny", "allow");
        using (var hidden = await FeatureHttpTestRequests.SendAsync(ordinary, inspect, HttpMethod.Get, path, dataAccess: source))
            Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        await ConfigureAsync("allow", "deny");
        using (var denied = await FeatureHttpTestRequests.SendAsync(ordinary, inspect, HttpMethod.Get, path, dataAccess: source))
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        await ConfigureAsync("allow", "allow");
        await http.GrantAsync(admin, actor.RoleId, http.Example());
        var revokedBefore = await SnapshotAsync(fixture, id, "example.record");
        using (var revoked = await FeatureHttpTestRequests.SendAsync(ordinary, inspect, HttpMethod.Get, path + "/catalog", dataAccess: source))
            Assert.Equal(HttpStatusCode.Forbidden, revoked.StatusCode);
        Assert.Equal(revokedBefore, await SnapshotAsync(fixture, id, "example.record"));

        async Task ConfigureAsync(string read, string authorize)
        {
            using var saved = await FeatureHttpTestRequests.SendAsync(admin, manage, HttpMethod.Put, original, Configuration(read, authorize), manager);
            Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);
        }
    }

    private static async Task<string> SnapshotAsync(BusinessAuditHostFixture fixture, string objectId, string resourceKey = "part.part")
    {
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
        var subjects = await db.Set<Dictionary<string, object?>>("DataScopeObjectSubject").AsNoTracking()
            .Where(row => EF.Property<string>(row, "ObjectId") == objectId)
            .OrderBy(row => EF.Property<string>(row, "EID")).ToArrayAsync();
        var ids = subjects.Select(row => (string)row["EID"]!).ToArray();
        var actions = await db.Set<Dictionary<string, object?>>("DataScopeObjectAction").AsNoTracking()
            .Where(row => ids.Contains(EF.Property<string>(row, "EntryId")))
            .OrderBy(row => EF.Property<string>(row, "EID")).ToArrayAsync();
        var audit = await db.Set<Dictionary<string, object?>>("BusinessChange").AsNoTracking()
            .Where(row => EF.Property<string>(row, "EventType") == "dataScope.object.changed"
                && EF.Property<string>(row, "TargetId") == resourceKey + "/" + objectId)
            .OrderBy(row => EF.Property<string>(row, "EID")).ToArrayAsync();
        return JsonSerializer.Serialize(new { subjects, actions, audit });
    }

    private sealed class AclAttemptGate : DbCommandInterceptor
    {
        internal bool Active { get; set; }
        internal IHttpContextAccessor? Context { get; set; }
        internal string? ActorId { get; set; }
        internal string? Target { get; set; }
        internal TaskCompletionSource Attempt { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<int> result, CancellationToken token = default)
        {
            if (Active && Context?.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier) == ActorId
                && command.CommandText.Contains("sp_getapplock", StringComparison.Ordinal)
                && command.Parameters.Cast<DbParameter>().Any(parameter => Equals(parameter.Value, Target)))
            {
                Attempt.TrySetResult();
                await Release.Task.WaitAsync(token);
            }
            return result;
        }
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "通用授权端点绑定真实资源按钮，借资源或业务来源拒绝且不写入")]
    public async Task GenericSave_UsesActualResourceButtonAndRejectsBorrowedResourceOrBehaviorWithoutWrites()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        await InitializeAsync(fixture, admin);
        var position = await ContainerAsync(fixture, admin);
        var part = await CreateAsync(fixture, admin, position, "Part", "parts", "createPart", "Auth329Generic");
        var http = new FeatureHttpTestRequests(fixture);
        var manage = Assert.Single(fixture.FeatureCatalog, point => point.ModuleId == "Part" && point.Key == "authorize"
            && point.ActionScope == "list.rowActions");
        var declaration = http.Declaration("part.part.authorize", manage);
        Assert.Equal("part.part.authorize", declaration.BusinessBinding);
        Assert.Equal("Part", declaration.Object);
        Assert.Equal(new[] { "authorize" }, declaration.Actions);
        var input = new DataScopeObjectConfiguration("part.part", part.Id, []);
        // 正式 owner 接入先提供真实合法声明；本 RED 针对 generic 尚未适配该新身份。
        using (var owned = await FeatureHttpTestRequests.SendAsync(admin, manage, HttpMethod.Put,
            "/api/part/parts/" + part.Id + "/permissions", input, declaration))
            Assert.Equal(HttpStatusCode.NoContent, owned.StatusCode);
        var path = "/api/authorization/data-scope/objects/part.part/" + part.Id;
        var before = await SubjectsAsync();
        using (var missing = await FeatureHttpTestRequests.SendAsync(admin, manage, HttpMethod.Put, path, input))
            Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        var create = Assert.Single(fixture.FeatureCatalog, point => point.ModuleId == "Part" && point.Key == "create"
            && point.ActionScope == "list.pageActions");
        using (var behavior = await FeatureHttpTestRequests.SendAsync(admin, create, HttpMethod.Put, path, input,
            http.Declaration("part.part.create", create)))
            Assert.True(behavior.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.BadRequest);
        var positionEdit = Assert.Single(fixture.FeatureCatalog, point => point.ModuleId == "Organization"
            && point.BehaviorKey == "organization.position.update" && point.ActionScope == "list.rowActions");
        using (var resource = await FeatureHttpTestRequests.SendAsync(admin, positionEdit, HttpMethod.Put, path, input,
            http.Declaration("organization.position.update", positionEdit)))
            Assert.True(resource.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.BadRequest);
        Assert.Equal(before, await SubjectsAsync());
        using var generic = await FeatureHttpTestRequests.SendAsync(admin, manage, HttpMethod.Put, path, input, declaration);
        var body = await generic.Content.ReadAsStringAsync();
        Assert.Equal(before, await SubjectsAsync());
        Assert.True(generic.StatusCode == HttpStatusCode.NoContent, $"generic actual Part button: {(int)generic.StatusCode} {body}");

        async Task<string> SubjectsAsync()
        {
            using var scope = fixture.Services.CreateScope();
            var subjects = await scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>()
                .Set<Dictionary<string, object?>>("DataScopeObjectSubject").AsNoTracking()
                .Where(row => EF.Property<string>(row, "ObjectId") == part.Id).ToArrayAsync();
            return JsonSerializer.Serialize(subjects.OrderBy(row => (string)row["EID"]!, StringComparer.Ordinal));
        }
    }
}
