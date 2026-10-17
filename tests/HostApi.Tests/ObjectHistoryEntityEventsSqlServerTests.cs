using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ICP.Platform.HostApi.Persistence;
using ICP.Platform.Persistence.Auditing;
using ICP.Platform.Persistence.Abstractions;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;
using static ICP.Platform.HostApi.Tests.EntityOwnershipBackfillSqlServerHttpApiTests;

namespace ICP.Platform.HostApi.Tests;

public sealed class ObjectHistoryEntityEventsSqlServerTests
{
    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "Part和Document真实创建修改删除记录精确身份、名称编码与可信stamp，同值和拒绝无假记录")]
    public async Task PartAndDocument_RealCrudRecordsSnapshotsAndStamp_WithoutNoOpEvents()
    {
        var ticks = new AuditTicks();
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(services =>
            services.Replace(ServiceDescriptor.Scoped<IEntityAuditStampProvider>(provider => new AdvancingStamp(
                new HttpContextEntityAuditStampProvider(provider.GetRequiredService<Microsoft.AspNetCore.Http.IHttpContextAccessor>(),
                    provider.GetRequiredService<TimeProvider>()), ticks))), includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        await InitializeAsync(fixture, admin);
        var position = await ContainerAsync(fixture, admin);
        foreach (var (model, kind, action) in Kinds.Where(item => item.Model != "WorkspaceFolder"))
        {
            var target = await CreateAsync(fixture, admin, position, model, kind, action, "HistoryEvent296" + model);
            var resource = model.ToLowerInvariant() + "." + model.ToLowerInvariant();
            var prefix = model.ToLowerInvariant();
            var created = Assert.Single(await EventsAsync(fixture, resource, target.Id), row => Equals(row["EventType"], prefix + ".created"));
            AssertSnapshot(created, target.Name, target.Name);
            AssertStamp(created, await RowAsync(fixture, target), "CreatedBy", "CreatedAt");

            using (var unchanged = await EditAsync(fixture, admin, position, target, new { ENAME = target.Name, ECODE = target.Name }))
                await ReadOkAsync(unchanged);
            Assert.DoesNotContain(await EventsAsync(fixture, resource, target.Id), row => Equals(row["EventType"], prefix + ".updated"));
            using (var invalid = await EditAsync(fixture, admin, position, target, new { ECODE = "" }))
                Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            Assert.DoesNotContain(await EventsAsync(fixture, resource, target.Id), row => Equals(row["EventType"], prefix + ".updated"));

            var renamed = target.Name + "Changed";
            using (var changed = await EditAsync(fixture, admin, position, target, new { ENAME = renamed, ECODE = renamed, Description = "真实修改" }))
                await ReadOkAsync(changed);
            var updated = Assert.Single(await EventsAsync(fixture, resource, target.Id), row => Equals(row["EventType"], prefix + ".updated"));
            AssertSnapshot(updated, renamed, renamed);
            AssertStamp(updated, await RowAsync(fixture, target), "UpdatedBy", "UpdatedAt");
            Assert.NotEqual(created["OccurredAt"], updated["OccurredAt"]);
            // 名称/编码未变的真实Description修改也必须记录；修改回原空值同样是真实变化。
            foreach (var description in new string?[] { "仅说明变化", null })
            {
                using var descriptionOnly = await EditAsync(fixture, admin, position, target, new { Description = description });
                await ReadOkAsync(descriptionOnly);
            }
            Assert.Equal(3, (await EventsAsync(fixture, resource, target.Id)).Count(row => Equals(row["EventType"], prefix + ".updated")));
            using (var unchangedNull = await EditAsync(fixture, admin, position, target, new { Description = (string?)null }))
                await ReadOkAsync(unchangedNull);
            Assert.Equal(3, (await EventsAsync(fixture, resource, target.Id)).Count(row => Equals(row["EventType"], prefix + ".updated")));

            using (var deleted = await DeleteAsync(fixture, admin, position, target)) await ReadOkAsync(deleted);
            var events = await EventsAsync(fixture, resource, target.Id);
            var deletion = Assert.Single(events, row => Equals(row["EventType"], prefix + ".deleted"));
            AssertSnapshot(deletion, renamed, renamed);
            Assert.Equal(fixture.ActorId, deletion["ActorId"]);
            AssertSnapshot(Assert.Single(events, row => Equals(row["EventType"], prefix + ".created")), target.Name, target.Name);
            using var scope = fixture.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
            Assert.False(await db.Set<Dictionary<string, object?>>(model).AnyAsync(row => EF.Property<string>(row, "EID") == target.Id));
            Assert.False(await db.Set<Dictionary<string, object?>>("WorkspaceFolderContent").AnyAsync(row => EF.Property<string>(row, "LID") == target.LinkId));
            using var audit = await FeatureHttpTestRequests.SendAsync(admin, Point(fixture, "audit.change.read"), HttpMethod.Get,
                "/api/audit/business-changes/" + deletion["EID"]);
            Assert.Equal(HttpStatusCode.OK, audit.StatusCode);
        }
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "Part和Document每类CRUD审计登记失败时业务、引用、版本与成功事件一起回滚")]
    public async Task PartAndDocument_EventRegistrationFailureRollsBackBusinessAndHistory()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        await InitializeAsync(fixture, admin);
        var position = await ContainerAsync(fixture, admin);
        foreach (var (model, kind, action) in Kinds.Where(item => item.Model != "WorkspaceFolder"))
        {
            var prefix = model.ToLowerInvariant();
            var resource = prefix + "." + prefix;
            var failedName = "HistoryCreateFailure296" + model;
            fixture.Fault.AfterRegister = (change, _) => change.EventType == prefix + ".created"
                ? throw new InvalidOperationException("injected entity created audit failure") : Task.CompletedTask;
            try
            {
                using var failedCreate = await FeatureHttpTestRequests.SendAsync(admin, Point(fixture, "workspace.folder." + action), HttpMethod.Post,
                    "/api/workspace/content/" + kind, new { position, values = new { ENAME = failedName, ECODE = failedName } });
                Assert.Equal(HttpStatusCode.InternalServerError, failedCreate.StatusCode);
            }
            finally { fixture.Fault.AfterRegister = null; }
            using (var scope = fixture.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
                Assert.False(await db.Set<Dictionary<string, object?>>(model).AnyAsync(row => EF.Property<string>(row, "ENAME") == failedName));
                Assert.DoesNotContain(await db.Set<Dictionary<string, object?>>("BusinessChange")
                    .Where(row => EF.Property<string>(row, "Resource") == resource && EF.Property<string>(row, "EventType") == prefix + ".created").ToArrayAsync(),
                    row => ((string)row["FieldsJson"]!).Contains(failedName, StringComparison.Ordinal));
            }
            var target = await CreateAsync(fixture, admin, position, model, kind, action, "HistoryRollback296" + model);
            var original = await RowAsync(fixture, target);
            var originalCount = (await EventsAsync(fixture, resource, target.Id)).Length;
            foreach (var operation in new[] { "updated", "deleted" })
            {
                fixture.Fault.AfterRegister = (change, _) => change.EventType == prefix + "." + operation
                    ? throw new InvalidOperationException("injected entity audit failure") : Task.CompletedTask;
                try
                {
                    using var failed = operation == "updated"
                        ? await EditAsync(fixture, admin, position, target, new { ENAME = "must rollback", Description = "must rollback" })
                        : await DeleteAsync(fixture, admin, position, target);
                    Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
                }
                finally { fixture.Fault.AfterRegister = null; }
                var retained = await RowAsync(fixture, target);
                foreach (var field in new[] { "ECODE", "ENAME", "Description", "CreatedBy", "CreatedAt", "UpdatedBy", "UpdatedAt" })
                    Assert.Equal(original.GetValueOrDefault(field), retained.GetValueOrDefault(field));
                Assert.Equal(originalCount, (await EventsAsync(fixture, resource, target.Id)).Length);
                using var scope = fixture.Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
                Assert.True(await db.Set<Dictionary<string, object?>>("WorkspaceFolderContent").AnyAsync(row => EF.Property<string>(row, "LID") == target.LinkId));
                if (model == "Document")
                {
                    var current = await scope.ServiceProvider.GetRequiredService<ICP.Platform.Modules.Document.DocumentInitialIdentity>().ReadCurrentAsync(target.Id);
                    Assert.Equal(target.Name, current.Iteration["ENAME"]);
                    Assert.Equal(target.Name, current.Iteration["ECODE"]);
                }
            }
        }
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "Part和Document六类真实事件及业务已Flush后取消请求，业务、引用、版本和事件全部回滚")]
    public async Task PartAndDocument_RequestCancellationAfterFlushRollsBackBusinessAndHistory()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        await InitializeAsync(fixture, admin);
        var position = await ContainerAsync(fixture, admin);
        foreach (var (model, kind, action) in Kinds.Where(item => item.Model != "WorkspaceFolder"))
        {
            var target = await CreateAsync(fixture, admin, position, model, kind, action, "HistoryCancel296" + model);
            var original = await RowAsync(fixture, target);
            var prefix = model.ToLowerInvariant();
            foreach (var operation in new[] { "created", "updated", "deleted" })
            {
                var before = await PersistedCountsAsync(fixture);
                var flushed = false;
                using var cancellation = new CancellationTokenSource();
                fixture.Fault.AfterRegister = async (change, token) =>
                {
                    if (change.EventType != prefix + "." + operation) return;
                    var context = fixture.Services.GetRequiredService<Microsoft.AspNetCore.Http.IHttpContextAccessor>().HttpContext!;
                    // 使用当前请求真正持有的事务，把业务与事件写到SQL后再取消；不是另开scope或抛一个无关token。
                    await context.RequestServices.GetRequiredService<IPlatformTransactionExecutor>().FlushAsync(token);
                    flushed = true;
                    cancellation.Cancel();
                    await Task.Delay(Timeout.InfiniteTimeSpan, token);
                };
                try
                {
                    var failedName = target.Name + "CancelledCreate";
                    var method = operation == "created" ? HttpMethod.Post : operation == "updated" ? HttpMethod.Put : HttpMethod.Delete;
                    var route = "/api/workspace/content/" + (operation == "created" ? kind : target.LinkId);
                    object body = operation switch
                    {
                        "created" => new { position, values = new { ENAME = failedName, ECODE = failedName } },
                        "updated" => new { position, values = new { Description = "cancelled description" } },
                        _ => new { position }
                    };
                    var behavior = "workspace.folder." + (operation == "created" ? action : operation == "updated" ? "editObject" : "deleteObject");
                    using var request = new HttpRequestMessage(method, route)
                    {
                        Content = JsonContent.Create(body, options: new JsonSerializerOptions())
                    };
                    request.Headers.Add("X-NewP-Feature", Point(fixture, behavior).Position);
                    await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                    {
                        using var response = await admin.SendAsync(request, cancellation.Token);
                        Assert.Fail($"请求未进入取消点，HTTP {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
                    });
                }
                finally { fixture.Fault.AfterRegister = null; }
                Assert.True(flushed);
                Assert.Equal(before, await PersistedCountsAsync(fixture));
                var retained = await RowAsync(fixture, target);
                foreach (var field in new[] { "ECODE", "ENAME", "Description", "CreatedBy", "CreatedAt", "UpdatedBy", "UpdatedAt" })
                    Assert.Equal(original.GetValueOrDefault(field), retained.GetValueOrDefault(field));
                if (model == "Document")
                {
                    using var scope = fixture.Services.CreateScope();
                    var current = await scope.ServiceProvider.GetRequiredService<ICP.Platform.Modules.Document.DocumentInitialIdentity>().ReadCurrentAsync(target.Id);
                    Assert.Equal(target.Name, current.Iteration["ENAME"]);
                    Assert.Equal(target.Name, current.Iteration["ECODE"]);
                    Assert.Null(current.Iteration["Description"]);
                }
            }
        }
    }

    private static async Task<int[]> PersistedCountsAsync(BusinessAuditHostFixture fixture)
    {
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
        var counts = new List<int>();
        foreach (var model in new[] { "Part", "Document", "WorkspaceFolderContent", "DocumentVersion", "DocumentIteration", "BusinessChange" })
            counts.Add(await db.Set<Dictionary<string, object?>>(model).CountAsync());
        return counts.ToArray();
    }

    private static Task<HttpResponseMessage> EditAsync(BusinessAuditHostFixture fixture, HttpClient client, Position position, Target target, object values) =>
        FeatureHttpTestRequests.SendAsync(client, Point(fixture, "workspace.folder.editObject"), HttpMethod.Put,
            "/api/workspace/content/" + target.LinkId, new { position, values });

    private static Task<HttpResponseMessage> DeleteAsync(BusinessAuditHostFixture fixture, HttpClient client, Position position, Target target) =>
        FeatureHttpTestRequests.SendAsync(client, Point(fixture, "workspace.folder.deleteObject"), HttpMethod.Delete,
            "/api/workspace/content/" + target.LinkId, new { position });

    private static async Task<Dictionary<string, object?>[]> EventsAsync(BusinessAuditHostFixture fixture, string resource, string id)
    {
        using var scope = fixture.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>().Set<Dictionary<string, object?>>("BusinessChange").AsNoTracking()
            .Where(row => EF.Property<string>(row, "Resource") == resource && EF.Property<string>(row, "TargetId") == id).ToArrayAsync();
    }

    private static void AssertStamp(Dictionary<string, object?> change, Dictionary<string, object?> row, string actor, string instant)
    {
        Assert.Equal(row[actor], change["ActorId"]);
        Assert.Equal(row[instant], change["OccurredAt"]);
    }

    private static void AssertSnapshot(Dictionary<string, object?> change, string name, string code)
    {
        var fields = JsonSerializer.Deserialize<JsonElement>((string)change["FieldsJson"]!);
        foreach (var (field, value) in new[] { ("ObjectName", name), ("ObjectCode", code) })
        {
            var snapshot = Assert.Single(fields.EnumerateArray(), item => item.GetProperty("Field").GetString() == field);
            Assert.Equal(JsonValueKind.Null, snapshot.GetProperty("Before").ValueKind);
            Assert.Equal("text", snapshot.GetProperty("After").GetProperty("Kind").GetString());
            Assert.Equal(value, snapshot.GetProperty("After").GetProperty("Value").GetString());
        }
    }

    private sealed class AuditTicks
    {
        internal long Count;
    }

    // 每次可信stamp读取都推进时间，防止固定时钟掩盖业务行与事件各自取stamp的缺陷。
    private sealed class AdvancingStamp(HttpContextEntityAuditStampProvider inner, AuditTicks ticks) : IEntityAuditStampProvider
    {
        public EntityAuditStamp GetCurrent()
        {
            var stamp = inner.GetCurrent();
            return new(stamp.ActorId, stamp.OccurredAtUtc.AddMilliseconds(Interlocked.Increment(ref ticks.Count)));
        }
    }
}
