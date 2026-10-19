using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ICP.Platform.Modules.Workspace;
using System.Security.Claims;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Crud.ObjectOperations;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Testing;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed class PartWriteExecutionSqlServerTests
{
    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "真实Part服务端写入缺可信来源时在写前拒绝，管理员也不豁免绑定")]
    public async Task InternalCreate_WithoutTrustedWriteSourceRejectsBeforeInsertingPart()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includePart: true);
        using var administrator = await fixture.LoginAdministratorAsync();
        using var scope = fixture.Services.CreateScope();
        var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        var previous = accessor.HttpContext;
        accessor.HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, fixture.ActorId)], "part-write-source-test")) };
        try
        {
            var actor = scope.ServiceProvider.GetRequiredService<ICP.Platform.Persistence.Auditing.IEntityAuditStampProvider>().GetCurrent().ActorId;
            Assert.Equal(fixture.ActorId, actor);
            var owners = await scope.ServiceProvider.GetRequiredService<ICP.Platform.Persistence.Ownership.IAccountOwnershipDirectory>().FindManyAsync([actor]);
            Assert.False(string.IsNullOrWhiteSpace(owners[actor].DepartmentId));
            using var trace = new Activity(nameof(InternalCreate_WithoutTrustedWriteSourceRejectsBeforeInsertingPart)).Start();
            var executor = scope.ServiceProvider.GetRequiredService<IObjectOperationExecutor>();
            var observed = await Record.ExceptionAsync(async () =>
            {
                var result = await executor.ExecuteAsync("part.part", ObjectOperationAction.Create, null,
                    new Dictionary<string, object?> { ["ECODE"] = "PART-SOURCE-MISSING", ["ENAME"] = "缺可信写来源" });
                Assert.True(result.Succeeded, "Existing write was rejected before the binding boundary.");
            });
            var failure = Assert.IsType<ActionDataAccessException>(observed);
            Assert.Equal(400, failure.Status);
            Assert.Equal("ActionDataAccess.WriteContextRequired", failure.Code);
            Assert.False(await scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>().Set<Dictionary<string, object?>>("Part")
                .AnyAsync(row => EF.Property<string>(row, "ECODE") == "PART-SOURCE-MISSING"));
        }
        finally { accessor.HttpContext = previous; }
    }
    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "真实普通Part入口和固定内部创建更新删除消费自身要求，资源/目标/caller串用写前拒绝")]
    public async Task EntryAndInternalWrites_RequireExactSourceAndPreserveClosedHttpOperations()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includePart: true);
        using var admin = await fixture.LoginAdministratorAsync();
        var read = Assert.Single(fixture.FeatureCatalog, point => point.ModuleId == "Part" && point.Kind == "navigation" && point.Key == "part.parts");
        var create = Assert.Single(fixture.FeatureCatalog, point => point.BehaviorKey == "part.part.create");
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "PartWriteSource", "Part写来源",
            "part-write-source", "Part写来源账号", read, create);
        using var ordinary = actor.Client;
        var declaration = OwnedResourceReadHttpRequests.Declaration(fixture, create, "part.part", "create");
        var values = new { ECODE = "PART-SOURCE-ENTRY", ENAME = "独立正式创建" };
        using (var missing = await FeatureHttpTestRequests.SendAsync(ordinary, create, HttpMethod.Post, "/api/part/parts", values))
            Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        using var created = await FeatureHttpTestRequests.SendAsync(ordinary, create, HttpMethod.Post, "/api/part/parts", values, declaration);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var entryId = BusinessAuditSqlServerHttpApiTests.Id((await created.Content.ReadFromJsonAsync<JsonElement>()));
        foreach (var method in new[] { HttpMethod.Put, HttpMethod.Delete })
        {
            using var closed = await FeatureHttpTestRequests.SendAsync(ordinary, create, method, "/api/part/parts/" + entryId,
                new { ENAME = "不能从独立接口修改" }, declaration);
            Assert.Equal(HttpStatusCode.Forbidden, closed.StatusCode);
        }
        using var scope = fixture.Services.CreateScope();
        var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        var previous = accessor.HttpContext;
        accessor.HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, actor.AccountId)], "part-fixed-internal")) };
        using var trace = new Activity(nameof(EntryAndInternalWrites_RequireExactSourceAndPreserveClosedHttpOperations)).Start();
        try
        {
            var executor = scope.ServiceProvider.GetRequiredService<IObjectOperationExecutor>();
            ObjectOperationWriteSource.Declared Source(string operation, string? id)
            {
                var key = operation == "create" ? "part.part.internal.create" : "part.part." + operation;
                return new(new(new(key, "standardCrud." + operation, "submit"), "part.part", id)
                { Internal = new(key, "standardCrud." + operation, typeof(WorkspaceContents), operation == "create" ? "CreateObjectAsync" : "MutateObjectsAsync") });
            }
            var input = new Dictionary<string, object?> { ["ECODE"] = "PART-SOURCE-INTERNAL", ["ENAME"] = "可信内部候选" };
            var source = Source("create", null);
            foreach (var wrong in new[]
            {
                source with { Context = source.Context with { ResourceKey = "document.document" } },
                source with { Context = source.Context with { ObjectId = entryId } },
                source with { Context = source.Context with { Internal = source.Context.Internal! with { Member = "ReadEditValuesAsync" } } }
            })
            {
                var error = await Assert.ThrowsAsync<ActionDataAccessException>(() => executor.ExecuteAsync("part.part", ObjectOperationAction.Create, null, input, wrong));
                Assert.Equal(400, error.Status);
            }
            var rejectedLegacy = await Assert.ThrowsAsync<ActionDataAccessException>(() => executor.ExecuteAsync("part.part", ObjectOperationAction.Create,
                null, input, new ObjectOperationWriteSource.Legacy(typeof(PartWriteExecutionSqlServerTests), nameof(EntryAndInternalWrites_RequireExactSourceAndPreserveClosedHttpOperations), "part.part", "create", null)));
            Assert.Equal("ActionDataAccess.LegacyWriteBindingInvalid", rejectedLegacy.Code);
            var result = await executor.ExecuteAsync("part.part", ObjectOperationAction.Create, null, input, source);
            Assert.True(result.Succeeded, result.Error?.Code);
            var id = (string)result.Detail!.Fields["EID"]!;
            Assert.Equal(actor.AccountId, result.Detail.Fields["OwnerId"]);
            Assert.Equal(actor.AccountId, result.Detail.Fields["CreatedBy"]);
            var update = Source("update", id);
            var mismatch = await Assert.ThrowsAsync<ActionDataAccessException>(() => executor.ExecuteAsync("part.part", ObjectOperationAction.Update,
                entryId, new Dictionary<string, object?> { ["ENAME"] = "不得串目标" }, update));
            Assert.Equal("ActionDataAccess.WriteBindingInvalid", mismatch.Code);
            var updated = await executor.ExecuteAsync("part.part", ObjectOperationAction.Update, id,
                new Dictionary<string, object?> { ["ENAME"] = "内部正常修改" }, update);
            Assert.True(updated.Succeeded, updated.Error?.Code);
            Assert.Equal("内部正常修改", updated.Detail!.Fields["ENAME"]);
            var deleted = await executor.ExecuteAsync("part.part", ObjectOperationAction.Delete, id, new Dictionary<string, object?>(), Source("delete", id));
            Assert.True(deleted.Succeeded, deleted.Error?.Code);
            var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
            Assert.False(await db.Set<Dictionary<string, object?>>("Part").AnyAsync(row => EF.Property<string>(row, "EID") == id));
            Assert.Equal(1, await db.Set<Dictionary<string, object?>>("Part").CountAsync());
        }
        finally { accessor.HttpContext = previous; }
    }
    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "真实Part内部写在原旧事实保护位置执行read和edit/delete，拒绝不修改对象")]
    public async Task InternalWrites_ReadAndActionDenialsPreserveProtectedFacts()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includePart: true);
        using var admin = await fixture.LoginAdministratorAsync();
        var read = Assert.Single(fixture.FeatureCatalog, point => point.ModuleId == "Part" && point.Kind == "navigation" && point.Key == "part.parts");
        var create = Assert.Single(fixture.FeatureCatalog, point => point.BehaviorKey == "part.part.create");
        var declaration = OwnedResourceReadHttpRequests.Declaration(fixture, create, "part.part", "create");
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "PartWriteDenials", "内部写拒绝",
            "part-write-denials", "内部写普通账号", read, create);
        using var ordinary = actor.Client;
        var ids = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var action in new[] { "read", "edit", "delete" })
        {
            using var response = await FeatureHttpTestRequests.SendAsync(ordinary, create, HttpMethod.Post, "/api/part/parts",
                new { ECODE = "DENY-" + action, ENAME = "拒绝前的名称" }, declaration);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            ids[action] = BusinessAuditSqlServerHttpApiTests.Id(await response.Content.ReadFromJsonAsync<JsonElement>());
            var key = "PartInternalDeny" + action;
            var rule = new ICP.Platform.Modules.Authorization.DTOs.DataScopeRuleDto(key, key, null, key, true, "part.part",
                new("attribute", "ECODE", "equal", JsonSerializer.SerializeToElement("DENY-" + action)),
                [new(key + "Subject", "all", null, 0, new Dictionary<string, string> { [action] = "deny" })], []);
            var save = Assert.Single(fixture.FeatureCatalog, point => point.BehaviorKey == "authorization.dataScope.save");
            using var saved = await FeatureHttpTestRequests.SendAsync(admin, save, HttpMethod.Put, "/api/authorization/data-scope/rules/" + key, rule);
            Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);
        }
        using var scope = fixture.Services.CreateScope();
        var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        var previous = accessor.HttpContext;
        accessor.HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, actor.AccountId)], "part-internal-write-denials")) };
        using var trace = new Activity(nameof(InternalWrites_ReadAndActionDenialsPreserveProtectedFacts)).Start();
        try
        {
            var executor = scope.ServiceProvider.GetRequiredService<IObjectOperationExecutor>();
            foreach (var (denied, operation, status, code) in new[]
            {
                ("read", "update", 404, "StandardCrud.NotFound"),
                ("read", "delete", 404, "StandardCrud.NotFound"),
                ("edit", "update", 403, "DataScope.ActionDenied"),
                ("delete", "delete", 403, "DataScope.ActionDenied")
            })
            {
                var key = "part.part." + operation;
                var source = new ObjectOperationWriteSource.Declared(new(new(key, "standardCrud." + operation, "submit"), "part.part", ids[denied])
                { Internal = new(key, "standardCrud." + operation, typeof(WorkspaceContents), "MutateObjectsAsync") });
                var action = operation == "update" ? ObjectOperationAction.Update : ObjectOperationAction.Delete;
                var result = await executor.ExecuteAsync("part.part", action, ids[denied],
                    new Dictionary<string, object?> { ["ENAME"] = "不得生效" }, source);
                Assert.False(result.Succeeded);
                Assert.Equal(status, result.Error!.StatusCode);
                Assert.Equal(code, result.Error.Code);
            }
            var rows = await scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>().Set<Dictionary<string, object?>>("Part").AsNoTracking().ToListAsync();
            Assert.Equal(3, rows.Count);
            Assert.All(rows, row => Assert.Equal("拒绝前的名称", row["ENAME"]));
        }
        finally { accessor.HttpContext = previous; }
    }
}
