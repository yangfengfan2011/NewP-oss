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
    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "正式DI内部Part创建不暗加create/read/edit下限，字段、归属、记录器与原子事务保持")]
    public async Task ExplicitInternalCreate_UsesTheRegisteredPartBehaviorWithoutAddingAnAuthorizationFloor()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includePart: true);
        using var admin = await fixture.LoginAdministratorAsync();
        var read = Assert.Single(fixture.FeatureCatalog, point => point.ModuleId == "Part" && point.Kind == "navigation" && point.Key == "part.parts");
        var create = Assert.Single(fixture.FeatureCatalog, point => point.BehaviorKey == "part.part.create");
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "PartOptionalFloor", "内部组合",
            "part-optional-floor", "内部组合账号", read, create);
        using var ordinary = actor.Client;
        const string name = "内部组合无固定下限";
        var rule = new ICP.Platform.Modules.Authorization.DTOs.DataScopeRuleDto("PartOptionalFloor", "PartOptionalFloor", null, "PartOptionalFloor", true, "part.part",
            new("attribute", "ENAME", "equal", JsonSerializer.SerializeToElement(name)),
            [new("PartOptionalFloorSubject", "all", null, 0, new Dictionary<string, string> { ["create"] = "deny", ["read"] = "deny", ["edit"] = "deny" })], []);
        using (var saved = await new FeatureHttpTestRequests(fixture).SendButtonAsync(admin, fixture.FeatureCatalog.Single(point => point.BehaviorKey == "authorization.dataScope.save"),
            HttpMethod.Put, "/api/authorization/data-scope/rules/PartOptionalFloor", rule)) Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);
        var declaration = new FeatureHttpTestRequests(fixture).Declaration("part.part.create", create);
        using (var denied = await FeatureHttpTestRequests.SendAsync(ordinary, create, HttpMethod.Post, "/api/part/parts",
            new { ECODE = "PART-PUBLIC-DENIED", ENAME = name }, declaration)) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using (var missing = await FeatureHttpTestRequests.SendAsync(ordinary, create, HttpMethod.Post, "/api/part/parts",
            new { ECODE = "PART-PUBLIC-MISSING", ENAME = name })) Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);

        using var scope = fixture.Services.CreateScope();
        var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        var previous = accessor.HttpContext;
        accessor.HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, actor.AccountId)], "part-explicit-internal")) };
        using var trace = new Activity(nameof(ExplicitInternalCreate_UsesTheRegisteredPartBehaviorWithoutAddingAnAuthorizationFloor)).Start();
        try
        {
            var executor = scope.ServiceProvider.GetRequiredService<IObjectOperationExecutor>();
            var transaction = scope.ServiceProvider.GetRequiredService<ICP.Platform.Persistence.Abstractions.IPlatformTransactionExecutor>();
            var source = new ObjectOperationWriteSource.Internal("part.part", "Part", ObjectOperationAction.Create, null);
            var descriptor = scope.ServiceProvider.GetRequiredService<ICP.Platform.Crud.Abstractions.IStandardCrudResourceRegistry>().FindByResourceKey("part.part")!;
            var outside = await Assert.ThrowsAsync<ActionDataAccessException>(() => scope.ServiceProvider.GetRequiredService<ICP.Platform.Crud.Runtime.StandardCrudDefaultWriter>()
                .PrepareCreateCandidateAsync(descriptor, new Dictionary<string, object?> { ["ECODE"] = "OUTSIDE", ["ENAME"] = name }, source));
            Assert.Equal("ObjectOperation.InternalContextRequired", outside.Code);
            foreach (var wrong in new[] { source with { ResourceKey = "document.document" }, source with { ObjectName = "Document" },
                source with { Action = ObjectOperationAction.Update }, source with { ObjectId = "Part_borrowed" } })
            {
                var error = await Assert.ThrowsAsync<ActionDataAccessException>(() => executor.ExecuteAsync("part.part", ObjectOperationAction.Create, null,
                    new Dictionary<string, object?> { ["ECODE"] = "WRONG", ["ENAME"] = name }, wrong));
                Assert.Equal("ObjectOperation.InternalBindingInvalid", error.Code);
            }
            var ids = await transaction.ExecuteAsync(async token =>
            {
                var created = new List<string>();
                foreach (var code in new[] { "PART-INTERNAL-ONE", "PART-INTERNAL-TWO" })
                {
                    var result = await executor.ExecuteAsync("part.part", ObjectOperationAction.Create, null,
                        new Dictionary<string, object?> { ["ECODE"] = code, ["ENAME"] = name }, source, token);
                    Assert.True(result.Succeeded, result.Error?.Code);
                    Assert.Equal(actor.AccountId, result.Detail!.Fields["OwnerId"]);
                    Assert.Equal(actor.AccountId, result.Detail.Fields["CreatedBy"]);
                    created.Add((string)result.Detail.Fields["EID"]!);
                }
                return created;
            });
            var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
            var rows = await db.Set<Dictionary<string, object?>>("Part").AsNoTracking().ToArrayAsync();
            Assert.Equal(2, rows.Length);
            var check = scope.ServiceProvider.GetRequiredService<IObjectActionAccess>();
            Assert.Equal(DataExecutionDecision.NotFound, await check.CheckAsync("Part", ids[0], ["edit"]));
            var audits = await db.Set<Dictionary<string, object?>>("BusinessChange").AsNoTracking()
                .Where(row => EF.Property<string>(row, "EventType") == "part.created").ToArrayAsync();
            Assert.Equal(ids.Order(), audits.Select(row => (string)row["TargetId"]!).Order());
            Assert.All(audits, change => { Assert.Equal(actor.AccountId, change["ActorId"]); Assert.False(string.IsNullOrWhiteSpace(change["TraceId"] as string)); });
            var before = JsonSerializer.Serialize(await db.Set<Dictionary<string, object?>>("BusinessChange").AsNoTracking().ToArrayAsync());
            // 执行器已经flush两行和记录器后故障，外层真实SQL事务仍必须一并回滚。
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await transaction.ExecuteAsync<bool>(async token =>
            {
                foreach (var code in new[] { "PART-ROLLBACK-ONE", "PART-ROLLBACK-TWO" })
                    Assert.True((await executor.ExecuteAsync("part.part", ObjectOperationAction.Create, null,
                        new Dictionary<string, object?> { ["ECODE"] = code, ["ENAME"] = name }, source, token)).Succeeded);
                throw new InvalidOperationException("Injected after actual Part and audit flush.");
            }));
            Assert.Equal(2, await db.Set<Dictionary<string, object?>>("Part").CountAsync());
            Assert.Equal(before, JsonSerializer.Serialize(await db.Set<Dictionary<string, object?>>("BusinessChange").AsNoTracking().ToArrayAsync()));
        }
        finally { accessor.HttpContext = previous; }
    }

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
    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "真实普通Part入口与显式可信内部创建更新删除核对精确目标，已退役复制合同不可复用")]
    public async Task EntryAndInternalWrites_RequireExactSourceAndPreserveClosedHttpOperations()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includePart: true);
        using var admin = await fixture.LoginAdministratorAsync();
        var read = Assert.Single(fixture.FeatureCatalog, point => point.ModuleId == "Part" && point.Kind == "navigation" && point.Key == "part.parts");
        var create = Assert.Single(fixture.FeatureCatalog, point => point.BehaviorKey == "part.part.create");
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "PartWriteSource", "Part写来源",
            "part-write-source", "Part写来源账号", read, create);
        using var ordinary = actor.Client;
        var declaration = new FeatureHttpTestRequests(fixture).Declaration("part.part.create", create);
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
            ObjectOperationWriteSource? retired = null;
            var input = new Dictionary<string, object?> { ["ECODE"] = "PART-SOURCE-INTERNAL", ["ENAME"] = "可信内部候选" };
            Assert.Equal(400, (await Assert.ThrowsAsync<ActionDataAccessException>(() => executor.ExecuteAsync("part.part", ObjectOperationAction.Create, null, input, retired))).Status);
            var source = new ObjectOperationWriteSource.Internal("part.part", "Part", ObjectOperationAction.Create, null);
            foreach (var wrong in new[]
            {
                source with { ResourceKey = "document.document" },
                source with { ObjectId = entryId },
                source with { Action = ObjectOperationAction.Update }
            })
            {
                var error = await Assert.ThrowsAsync<ActionDataAccessException>(() => executor.ExecuteAsync("part.part", ObjectOperationAction.Create, null, input, wrong));
                Assert.Equal(400, error.Status);
            }
            var result = await executor.ExecuteAsync("part.part", ObjectOperationAction.Create, null, input, source);
            Assert.True(result.Succeeded, result.Error?.Code);
            var id = (string)result.Detail!.Fields["EID"]!;
            Assert.Equal(actor.AccountId, result.Detail.Fields["OwnerId"]);
            Assert.Equal(actor.AccountId, result.Detail.Fields["CreatedBy"]);
            var update = new ObjectOperationWriteSource.Internal("part.part", "Part", ObjectOperationAction.Update, id);
            var mismatch = await Assert.ThrowsAsync<ActionDataAccessException>(() => executor.ExecuteAsync("part.part", ObjectOperationAction.Update,
                entryId, new Dictionary<string, object?> { ["ENAME"] = "不得串目标" }, update));
            Assert.Equal("ObjectOperation.InternalBindingInvalid", mismatch.Code);
            var updated = await executor.ExecuteAsync("part.part", ObjectOperationAction.Update, id,
                new Dictionary<string, object?> { ["ENAME"] = "内部正常修改" }, update);
            Assert.True(updated.Succeeded, updated.Error?.Code);
            Assert.Equal("内部正常修改", updated.Detail!.Fields["ENAME"]);
            var deleted = await executor.ExecuteAsync("part.part", ObjectOperationAction.Delete, id, new Dictionary<string, object?>(),
                new ObjectOperationWriteSource.Internal("part.part", "Part", ObjectOperationAction.Delete, id));
            Assert.True(deleted.Succeeded, deleted.Error?.Code);
            var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
            Assert.False(await db.Set<Dictionary<string, object?>>("Part").AnyAsync(row => EF.Property<string>(row, "EID") == id));
            Assert.Equal(1, await db.Set<Dictionary<string, object?>>("Part").CountAsync());
        }
        finally { accessor.HttpContext = previous; }
    }
    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "已退役Part声明写来源不能回退为显式内部授权，拒绝保留实体与审计")]
    public async Task MissingWriteSources_RejectInsteadOfFallingBackToInternalAuthority()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includePart: true);
        using var admin = await fixture.LoginAdministratorAsync();
        var read = Assert.Single(fixture.FeatureCatalog, point => point.ModuleId == "Part" && point.Kind == "navigation" && point.Key == "part.parts");
        var create = Assert.Single(fixture.FeatureCatalog, point => point.BehaviorKey == "part.part.create");
        var declaration = new FeatureHttpTestRequests(fixture).Declaration("part.part.create", create);
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
            using var saved = await new FeatureHttpTestRequests(fixture).SendButtonAsync(admin, save, HttpMethod.Put, "/api/authorization/data-scope/rules/" + key, rule);
            Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);
        }
        using var scope = fixture.Services.CreateScope();
        var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        var previous = accessor.HttpContext;
        accessor.HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, actor.AccountId)], "part-internal-write-denials")) };
        using var trace = new Activity(nameof(MissingWriteSources_RejectInsteadOfFallingBackToInternalAuthority)).Start();
        try
        {
            var executor = scope.ServiceProvider.GetRequiredService<IObjectOperationExecutor>();
            var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
            var beforeAudit = JsonSerializer.Serialize(await db.Set<Dictionary<string, object?>>("BusinessChange").AsNoTracking().ToArrayAsync());
            foreach (var (denied, operation) in new[]
            {
                ("read", "update"), ("read", "delete"), ("edit", "update"), ("delete", "delete")
            })
            {
                ObjectOperationWriteSource? source = null;
                var action = operation == "update" ? ObjectOperationAction.Update : ObjectOperationAction.Delete;
                var error = await Assert.ThrowsAsync<ActionDataAccessException>(() => executor.ExecuteAsync("part.part", action, ids[denied],
                    new Dictionary<string, object?> { ["ENAME"] = "不得生效" }, source));
                Assert.Equal(400, error.Status);
            }
            var rows = await scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>().Set<Dictionary<string, object?>>("Part").AsNoTracking().ToListAsync();
            Assert.Equal(3, rows.Count);
            Assert.All(rows, row => Assert.Equal("拒绝前的名称", row["ENAME"]));
            Assert.Equal(beforeAudit, JsonSerializer.Serialize(await db.Set<Dictionary<string, object?>>("BusinessChange").AsNoTracking().ToArrayAsync()));
        }
        finally { accessor.HttpContext = previous; }
    }
}
