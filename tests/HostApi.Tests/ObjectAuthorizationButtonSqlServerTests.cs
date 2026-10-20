using System.Net;
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

    private static async Task<string> SnapshotAsync(BusinessAuditHostFixture fixture, string objectId)
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
                && EF.Property<string>(row, "TargetId") == "part.part/" + objectId)
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
