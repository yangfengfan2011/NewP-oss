using System.Net;
using System.Net.Http.Json;
using System.Data.Common;
using System.Diagnostics;
using System.Text.Json;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.HostApi.Persistence;
using ICP.Platform.Modules.Audit.Contracts;
using ICP.Platform.Modules.Authorization.DTOs;
using ICP.Platform.Persistence.Abstractions;
using ICP.Platform.Persistence.Auditing;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Persistence.EfCore.Transactions;
using ICP.Platform.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;
using Xunit.Abstractions;
using static ICP.Platform.HostApi.Tests.EntityOwnershipBackfillSqlServerHttpApiTests;

namespace ICP.Platform.HostApi.Tests;

public sealed partial class FolderStructureParentAuthorizationSqlServerTests(ITestOutputHelper output)
{
    private readonly ITestOutputHelper testOutput = output;

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "结构新根取目标父，内部取新父；共享DAG只建一次，技术ID不同不假冲突，源ACL不复制，旧省略仍无初始化")]
    public async Task StructureCopy_InitializesSharedNewFoldersFromEquivalentNewParents_WithoutSourceAclOrReadRequirement()
    {
        var ticks = new AuditTicks();
        var cost = new StructureCost();
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(services =>
        {
            services.Replace(ServiceDescriptor.Scoped<IEntityAuditStampProvider>(provider => new AdvancingStamp(new HttpContextEntityAuditStampProvider(
                provider.GetRequiredService<Microsoft.AspNetCore.Http.IHttpContextAccessor>(), provider.GetRequiredService<TimeProvider>()), ticks)));
            services.AddScoped<EfPlatformTransactionExecutor>();
            services.Replace(ServiceDescriptor.Scoped<IPlatformTransactionExecutor>(provider => new ObservedFlushes(provider.GetRequiredService<EfPlatformTransactionExecutor>(), cost)));
            services.AddDbContext<EfPlatformDbContext>((_, options) => options.AddInterceptors(cost));
        }, includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        await InitializeAsync(fixture, admin);
        var position = await ContainerAsync(fixture, admin);
        var source = await CreateAsync(fixture, admin, position, "WorkspaceFolder", "folders", "create", "StructureSource298");
        var sourcePosition = Within(position, source);
        var a = await CreateAsync(fixture, admin, sourcePosition, "WorkspaceFolder", "folders", "create", "StructureA298");
        var b = await CreateAsync(fixture, admin, sourcePosition, "WorkspaceFolder", "folders", "create", "StructureB298");
        var shared = await CreateAsync(fixture, admin, Within(sourcePosition, a), "WorkspaceFolder", "folders", "create", "StructureShared298");
        using (var added = await FeatureHttpTestRequests.SendAsync(admin, Point(fixture, "workspace.folder.add"), HttpMethod.Post,
            "/api/workspace/content/references", new { position = Within(sourcePosition, b), target = new { modelKey = "WorkspaceFolder", id = shared.Id } }))
            await ReadOkAsync(added);
        var destination = await CreateAsync(fixture, admin, position, "WorkspaceFolder", "folders", "create", "StructureTarget298");
        var targetPosition = Within(position, destination);
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "StructureParent298", "结构父初始化", "structure-parent298", "结构父初始化账号",
            Point(fixture, "workspace.spaces.access"), Point(fixture, "workspace.folder.pasteFolderStructure"));
        using var ordinary = actor.Client;
        await SaveAsync(fixture, admin, sourcePosition, a,
            [new("StructureSourceA298", "creator", null, 3, new Dictionary<string, string> { ["history"] = "deny" })]);
        await SaveAsync(fixture, admin, sourcePosition, b,
            [new("StructureSourceB298", "creator", null, 8, new Dictionary<string, string> { ["history"] = "allow" })]);
        await SaveAsync(fixture, admin, position, destination,
            [new("StructureTemplateCreator298", "creator", null, 10, new Dictionary<string, string> { ["read"] = "deny" }, ExpiresAt: "2099-04-01T00:00:00Z"),
             new("StructureTemplateActor298", "user", actor.AccountId, 1, new Dictionary<string, string> { ["read"] = "allow", ["edit"] = "allow", ["authorize"] = "deny" })]);
        var originalFolders = await RowsAsync(fixture, "WorkspaceFolder");
        var originalIds = originalFolders.Select(row => (string)row["EID"]!).ToArray();
        var originalSubjects = Snapshot(await RowsAsync(fixture, "DataScopeObjectSubject"));
        var originalActions = Snapshot(await RowsAsync(fixture, "DataScopeObjectAction"));
        // 真实旧五成员输入仍保持不复制；不能把所有旧消费者静默切到新语义。
        using (var legacy = await FeatureHttpTestRequests.SendAsync(admin, Point(fixture, "workspace.folder.pasteFolderStructure"), HttpMethod.Post,
            "/api/workspace/content/folders/copy-structure", new { mode = "copy", sourcePosition = position, linkIds = new[] { source.LinkId }, targetPosition, name = "LegacyStructure298" }))
            await ReadOkAsync(legacy);
        var afterLegacy = await RowsAsync(fixture, "WorkspaceFolder");
        var legacyIds = afterLegacy.Where(row => !originalIds.Contains((string)row["EID"]!)).Select(row => (string)row["EID"]!).ToArray();
        Assert.Equal(4, legacyIds.Length);
        Assert.DoesNotContain(await RowsAsync(fixture, "DataScopeObjectSubject"), row => legacyIds.Contains((string)row["ObjectId"]!));
        var beforeIds = afterLegacy.Select(row => (string)row["EID"]!).ToArray();
        cost.Active = true;
        var clock = Stopwatch.StartNew();
        try
        {
            using var response = await FeatureHttpTestRequests.SendAsync(ordinary, Point(fixture, "workspace.folder.pasteFolderStructure"), HttpMethod.Post,
                "/api/workspace/content/folders/copy-structure", new { mode = "copy", sourcePosition = position, linkIds = new[] { source.LinkId }, targetPosition, name = "InitializedStructure298", copyParentAuthorization = true });
            var receipt = await ReadOkAsync(response);
            Assert.True(receipt.GetProperty("succeeded").GetBoolean()); Assert.False(receipt.TryGetProperty("id", out _));
        }
        finally
        {
            clock.Stop(); cost.Active = false;
            testOutput.WriteLine($"structureInitialization requestMs={clock.Elapsed.TotalMilliseconds:F1} flushCalls={cost.Flushes} sqlCommands={cost.Commands}");
        }
        var folders = await RowsAsync(fixture, "WorkspaceFolder");
        var created = folders.Where(row => !beforeIds.Contains((string)row["EID"]!)).ToArray();
        Assert.Equal(4, created.Length);
        var createdIds = created.Select(row => (string)row["EID"]!).ToArray();
        var root = Assert.Single(created, row => Equals(row["ENAME"], "InitializedStructure298"));
        var newA = Assert.Single(created, row => Equals(row["ENAME"], a.Name));
        var newB = Assert.Single(created, row => Equals(row["ENAME"], b.Name));
        var newShared = Assert.Single(created, row => Equals(row["ENAME"], shared.Name));
        var links = await RowsAsync(fixture, "WorkspaceFolderContent");
        var createdLinks = links.Where(row => createdIds.Contains((string)row["CEID"]!)).ToArray();
        Assert.Equal(5, createdLinks.Length);
        Assert.All(createdLinks, row => Assert.Equal(false, row["IsScopeNeutral"]));
        Assert.Contains(createdLinks, row => Equals(row["PEID"], destination.Id) && Equals(row["CEID"], root["EID"]));
        var incoming = createdLinks.Where(row => Equals(row["CEID"], newShared["EID"])).ToArray();
        Assert.Equal(2, incoming.Length);
        Assert.Equal(new[] { (string)newA["EID"]!, (string)newB["EID"]! }.Order(), incoming.Select(row => (string)row["PEID"]!).Order());
        var subjects = (await RowsAsync(fixture, "DataScopeObjectSubject")).Where(row => createdIds.Contains((string)row["ObjectId"]!)).ToArray();
        var entryIds = subjects.Select(row => (string)row["EID"]!).ToArray();
        Assert.Equal(8, subjects.Length); Assert.Equal(8, entryIds.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.DoesNotContain("StructureTemplateCreator298", entryIds); Assert.DoesNotContain("StructureTemplateActor298", entryIds);
        var actions = (await RowsAsync(fixture, "DataScopeObjectAction")).Where(row => entryIds.Contains((string)row["EntryId"]!)).ToArray();
        var events = (await RowsAsync(fixture, "BusinessChange")).Where(row => Equals(row["EventType"], "workspace.folder.parentAuthorizationInitialized") && createdIds.Contains((string)row["TargetId"]!)).ToArray();
        Assert.Equal(4, events.Length);
        foreach (var folder in created)
        {
            Assert.Equal(actor.AccountId, folder["CreatedBy"]);
            var entries = subjects.Where(row => Equals(row["ObjectId"], folder["EID"])).ToArray();
            Assert.Equal(2, entries.Length);
            var creator = Assert.Single(entries, row => Equals(row["SubjectType"], "creator"));
            Assert.Null(creator["SubjectId"]); Assert.Equal(10L, creator["Priority"]);
            Assert.Equal(new DateTime(2099, 4, 1, 0, 0, 0, DateTimeKind.Utc), creator["ExpiresAt"]);
            var user = Assert.Single(entries, row => Equals(row["SubjectType"], "user"));
            Assert.Equal(actor.AccountId, user["SubjectId"]); Assert.Equal(1L, user["Priority"]);
            var change = Assert.Single(events, row => Equals(row["TargetId"], folder["EID"]));
            Assert.Equal(folder["CreatedBy"], change["ActorId"]); Assert.Equal(folder["CreatedAt"], change["OccurredAt"]);
            foreach (var entry in entries)
            {
                Assert.Equal(folder["CreatedBy"], entry["CreatedBy"]); Assert.Equal(folder["CreatedAt"], entry["CreatedAt"]);
                foreach (var action in actions.Where(row => Equals(row["EntryId"], entry["EID"])))
                { Assert.Equal(folder["CreatedBy"], action["CreatedBy"]); Assert.Equal(folder["CreatedAt"], action["CreatedAt"]); }
            }
        }
        Assert.Equal(16, actions.Length);
        var excluded = (await RowsAsync(fixture, "DataScopeObjectSubject")).Where(row => !createdIds.Contains((string)row["ObjectId"]!)).ToArray();
        Assert.Equal(originalSubjects, Snapshot(excluded));
        Assert.Equal(originalActions, Snapshot((await RowsAsync(fixture, "DataScopeObjectAction")).Where(row => !entryIds.Contains((string)row["EntryId"]!)).ToArray()));
        foreach (var original in originalFolders) Assert.Contains(folders, row => Snapshot([row]) == Snapshot([original]));
        using var unreadable = await FeatureHttpTestRequests.SendAsync(ordinary, Point(fixture, "workspace.spaces.access"), HttpMethod.Get,
            "/api/workspace/folders/" + (string)newShared["EID"]!);
        Assert.Equal(HttpStatusCode.NotFound, unreadable.StatusCode);
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "结构末共享节点/最后事件失败或真Flush取消回滚整图ACL；已提交父撤权及内部候选create仍拒绝")]
    public async Task StructureInitialization_RollsBackLateFailuresAndCancellation_AndRechecksExternalParentAndEveryCandidate()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        await InitializeAsync(fixture, admin);
        var position = await ContainerAsync(fixture, admin);
        var parentLink = Assert.Single(await RowsAsync(fixture, "WorkspaceFolderContent"), row => Equals(row["LID"], position.LinkIds[^1]));
        var target = new Target("WorkspaceFolder", (string)parentLink["CEID"]!, position.LinkIds[^1], "归属维护目录");
        var source = await CreateAsync(fixture, admin, position, "WorkspaceFolder", "folders", "create", "RollbackSourceStructure298");
        var sourcePosition = Within(position, source);
        var a = await CreateAsync(fixture, admin, sourcePosition, "WorkspaceFolder", "folders", "create", "RollbackAStructure298");
        var b = await CreateAsync(fixture, admin, sourcePosition, "WorkspaceFolder", "folders", "create", "RollbackBStructure298");
        var shared = await CreateAsync(fixture, admin, Within(sourcePosition, a), "WorkspaceFolder", "folders", "create", "RollbackSharedStructure298");
        using (var added = await FeatureHttpTestRequests.SendAsync(admin, Point(fixture, "workspace.folder.add"), HttpMethod.Post,
            "/api/workspace/content/references", new { position = Within(sourcePosition, b), target = new { modelKey = "WorkspaceFolder", id = shared.Id } }))
            await ReadOkAsync(added);
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "StructureRollback298", "结构回滚", "structure-rollback298", "结构回滚账号",
            Point(fixture, "workspace.spaces.access"), Point(fixture, "workspace.folder.pasteFolderStructure"));
        using var ordinary = actor.Client;
        DataScopeDirectSubjectDto[] allowed = [new("StructureRollbackParent298", "user", actor.AccountId, 1,
            new Dictionary<string, string> { ["read"] = "allow", ["edit"] = "allow", ["create"] = "allow", ["authorize"] = "deny" })];
        await SaveAsync(fixture, admin, position with { LinkIds = [] }, target, allowed);
        var body = new { mode = "copy", sourcePosition = position, linkIds = new[] { source.LinkId }, targetPosition = position,
            name = "RollbackStructure298", copyParentAuthorization = true };
        foreach (var stage in new[] { "shared-initialization", "last-structure", "cancel-last-structure" })
        {
            var before = await TransactionSnapshotAsync(fixture);
            var initialized = new List<BusinessAuditChange>();
            var lastEvents = 0; var reached = false; var flushed = false;
            using var cancellation = new CancellationTokenSource();
            fixture.Fault.AfterRegister = async (change, token) =>
            {
                if (change.EventType == "workspace.folder.parentAuthorizationInitialized") initialized.Add(change);
                if (change.EventType == "workspace.folder.structureCopied") lastEvents++;
                if (!(stage == "shared-initialization" && change.EventType == "workspace.folder.parentAuthorizationInitialized" && initialized.Count == 4)
                    && !(stage != "shared-initialization" && change.EventType == "workspace.folder.structureCopied" && lastEvents == 4)) return;
                reached = true;
                if (stage != "cancel-last-structure") throw new InvalidOperationException("injected late structure initialization failure");
                var context = fixture.Services.GetRequiredService<Microsoft.AspNetCore.Http.IHttpContextAccessor>().HttpContext!;
                await context.RequestServices.GetRequiredService<IPlatformTransactionExecutor>().FlushAsync(token);
                var db = context.RequestServices.GetRequiredService<EfPlatformDbContext>();
                var ids = initialized.Select(item => item.TargetId).ToArray();
                Assert.Equal(4, await db.Set<Dictionary<string, object?>>("WorkspaceFolder").AsNoTracking().CountAsync(row => ids.Contains(EF.Property<string>(row, "EID")), token));
                Assert.Equal(5, await db.Set<Dictionary<string, object?>>("WorkspaceFolderContent").AsNoTracking().CountAsync(row => ids.Contains(EF.Property<string>(row, "CEID")), token));
                Assert.Equal(4, await db.Set<Dictionary<string, object?>>("DataScopeObjectSubject").AsNoTracking().CountAsync(row => ids.Contains(EF.Property<string>(row, "ObjectId")), token));
                flushed = true; cancellation.Cancel();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            };
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, "/api/workspace/content/folders/copy-structure")
                { Content = JsonContent.Create(body, options: new JsonSerializerOptions()) };
                request.Headers.Add("X-NewP-Feature", Point(fixture, "workspace.folder.pasteFolderStructure").Position);
                if (stage == "cancel-last-structure")
                    await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                    {
                        using var response = await ordinary.SendAsync(request, cancellation.Token);
                        Assert.Fail($"请求未进入取消点，HTTP {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
                    });
                else
                {
                    using var response = await ordinary.SendAsync(request);
                    Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
                }
            }
            finally { fixture.Fault.AfterRegister = null; }
            Assert.True(reached, stage);
            if (stage == "cancel-last-structure") Assert.True(flushed);
            Assert.Equal(before, await TransactionSnapshotAsync(fixture));
            Assert.Equal(4, initialized.Count);
            foreach (var change in initialized)
            {
                var endpoint = Assert.Single(change.Fields, field => field.Field == "CEID");
                Assert.Null(endpoint.Before); Assert.Equal("workspace.folder", endpoint.After.Kind); Assert.Equal(change.TargetId, endpoint.After.Value);
                var parent = Assert.Single(change.Fields, field => field.Field == "PEID");
                Assert.Null(parent.Before); Assert.Equal("workspace.folder", parent.After.Kind);
            }
            var rootEvent = Assert.Single(initialized, item => item.Fields.Single(field => field.Field == "ObjectName").After.Value == body.name);
            Assert.Equal(target.Id, rootEvent.Fields.Single(field => field.Field == "PEID").After.Value);
            var sharedEvent = Assert.Single(initialized, item => item.Fields.Single(field => field.Field == "ObjectName").After.Value == shared.Name);
            var newParents = initialized.Where(item => item.Fields.Single(field => field.Field == "ObjectName").After.Value is "RollbackAStructure298" or "RollbackBStructure298").Select(item => item.TargetId).ToArray();
            Assert.Equal(2, newParents.Length);
            Assert.Contains(sharedEvent.Fields.Single(field => field.Field == "PEID").After.Value, newParents);
            Assert.DoesNotContain(sharedEvent.Fields.Single(field => field.Field == "PEID").After.Value, new[] { a.Id, b.Id });
        }
        // 真正的外部撤权与创建并发：通用管理入口不取图锁，父ACL锁取得后必须复判完整目标位置。
        var paused = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var proceed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Fault.AfterRegister = async (change, token) =>
        {
            if (change.EventType != "entity.ownership.initialized") return;
            paused.TrySetResult(); await proceed.Task.WaitAsync(token);
        };
        Task<HttpResponseMessage>? creating = null;
        try
        {
            creating = FeatureHttpTestRequests.SendAsync(ordinary, Point(fixture, "workspace.folder.pasteFolderStructure"), HttpMethod.Post,
                "/api/workspace/content/folders/copy-structure", body);
            await paused.Task.WaitAsync(TimeSpan.FromSeconds(30));
            using (var saved = await FeatureHttpTestRequests.SendAsync(admin, Point(fixture, "workspace.folder.authorize"), HttpMethod.Put,
                "/api/authorization/data-scope/objects/workspace.folder/" + target.Id, new DataScopeObjectDto("workspace.folder", target.Id,
                    [new("StructureRollbackParent298", "user", actor.AccountId, 1, new Dictionary<string, string> { ["read"] = "allow", ["edit"] = "deny" })])))
                Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);
            var parentEntry = Assert.Single(await RowsAsync(fixture, "DataScopeObjectSubject"), row => Equals(row["ObjectId"], target.Id));
            Assert.Equal("deny", Assert.Single(await RowsAsync(fixture, "DataScopeObjectAction"), row => Equals(row["EntryId"], parentEntry["EID"])
                && Equals(row["ActionKey"], "edit"))["Opinion"]);
            var afterRevocation = await TransactionSnapshotAsync(fixture);
            proceed.TrySetResult();
            using var response = await creating;
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Equal(afterRevocation, await TransactionSnapshotAsync(fixture));
        }
        finally
        {
            proceed.TrySetResult(); fixture.Fault.AfterRegister = null;
            if (creating is not null && !creating.IsCompleted) { using var ignored = await creating; }
        }
        await SaveAsync(fixture, admin, position with { LinkIds = [] }, target, allowed);
        // 即使父配置含create allow，也不能替代某个内部新候选的create裁决。
        var rule = new DataScopeRuleDto("StructureCreate298", "StructureCreate298", null, "内部新候选拒绝", true, "workspace.folder",
            new("attribute", "ENAME", "equal", JsonSerializer.SerializeToElement(a.Name)),
            [new("StructureCreateAll298", "all", null, 0, new Dictionary<string, string> { ["create"] = "deny" })], []);
        using (var saved = await FeatureHttpTestRequests.SendAsync(admin, Point(fixture, "authorization.dataScope.save"), HttpMethod.Put,
            "/api/authorization/data-scope/rules/StructureCreate298", rule))
            Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);
        var beforeDeniedCreate = await TransactionSnapshotAsync(fixture);
        using (var response = await FeatureHttpTestRequests.SendAsync(ordinary, Point(fixture, "workspace.folder.pasteFolderStructure"), HttpMethod.Post,
            "/api/workspace/content/folders/copy-structure", body))
        {
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Equal("Workspace.FolderCreateDenied", JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync()).GetProperty("code").GetString());
        }
        Assert.Equal(beforeDeniedCreate, await TransactionSnapshotAsync(fixture));
    }

    private static async Task<string> TransactionSnapshotAsync(BusinessAuditHostFixture fixture)
    {
        var snapshot = new Dictionary<string, string>();
        foreach (var model in new[] { "WorkspaceFolder", "WorkspaceFolderContent", "DataScopeObjectSubject", "DataScopeObjectAction", "BusinessChange" })
            snapshot[model] = Snapshot(await RowsAsync(fixture, model));
        return JsonSerializer.Serialize(snapshot);
    }
    private static Position Within(Position position, Target target) => position with { LinkIds = [.. position.LinkIds, target.LinkId] };
    private static async Task SaveAsync(BusinessAuditHostFixture fixture, HttpClient admin, Position position, Target target, DataScopeDirectSubjectDto[] subjects)
    {
        using var response = await FeatureHttpTestRequests.SendAsync(admin, Point(fixture, "workspace.folder.authorize"), HttpMethod.Put,
            "/api/workspace/content/" + target.LinkId + "/permissions?queryScope=" + Uri.EscapeDataString(JsonSerializer.Serialize(position, new JsonSerializerOptions(JsonSerializerDefaults.Web))),
            new DataScopeObjectConfiguration("workspace.folder", target.Id, subjects));
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }
    private static async Task<Dictionary<string, object?>[]> RowsAsync(BusinessAuditHostFixture fixture, string model)
    {
        using var scope = fixture.Services.CreateScope();
        var id = model == "WorkspaceFolderContent" ? "LID" : "EID";
        return await scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>().Set<Dictionary<string, object?>>(model).AsNoTracking().OrderBy(row => EF.Property<string>(row, id)).ToArrayAsync();
    }
    private static string Snapshot(Dictionary<string, object?>[] rows) => JsonSerializer.Serialize(rows.OrderBy(row => row.TryGetValue("EID", out var id) ? id : row["LID"])
        .Select(row => row.OrderBy(pair => pair.Key, StringComparer.Ordinal)));
    // 只观测本次真实请求，不设置容量预算，不改变事务执行或保存语义。
    private sealed class StructureCost : DbCommandInterceptor
    {
        internal bool Active; internal int Flushes; internal int Commands;
        private void Observe() { if (Active) Interlocked.Increment(ref Commands); }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken token = default) { Observe(); return ValueTask.FromResult(result); }
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken token = default) { Observe(); return ValueTask.FromResult(result); }
        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<object> result, CancellationToken token = default) { Observe(); return ValueTask.FromResult(result); }
    }
    private sealed class ObservedFlushes(EfPlatformTransactionExecutor inner, StructureCost cost) : IPlatformTransactionExecutor
    {
        public bool IsActive => inner.IsActive;
        public bool HasParticipants(string modelKey) => inner.HasParticipants(modelKey);
        public ValueTask<TResult> ExecuteAsync<TResult>(Func<CancellationToken, ValueTask<TResult>> register, CancellationToken token = default) => inner.ExecuteAsync(register, token);
        public ValueTask<TResult> ExecuteAsync<TResult>(IReadOnlyCollection<string> models, Func<CancellationToken, ValueTask<TResult>> register, CancellationToken token = default) => inner.ExecuteAsync(models, register, token);
        public ValueTask FlushAsync(CancellationToken token = default) { if (cost.Active) Interlocked.Increment(ref cost.Flushes); return inner.FlushAsync(token); }
        public void RegisterChange(ICP.Platform.Persistence.Transactions.PlatformRecordChange change) => inner.RegisterChange(change);
    }
    private sealed class AuditTicks { internal long Count; }
    private sealed class AdvancingStamp(HttpContextEntityAuditStampProvider inner, AuditTicks ticks) : IEntityAuditStampProvider
    {
        public EntityAuditStamp GetCurrent()
        {
            var stamp = inner.GetCurrent();
            return new(stamp.ActorId, stamp.OccurredAtUtc.AddMilliseconds(Interlocked.Increment(ref ticks.Count)));
        }
    }
}
