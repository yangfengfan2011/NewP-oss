using System.Data.Common;
using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Persistence.Abstractions;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Persistence.EfCore.Transactions;
using ICP.Platform.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;
using static ICP.Platform.HostApi.Tests.EntityOwnershipBackfillSqlServerHttpApiTests;

namespace ICP.Platform.HostApi.Tests;

public sealed partial class FolderStructureParentAuthorizationSqlServerTests
{
    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_PARENT_AUTHORIZATION_CAPACITY", "独占测量4/19真实Folder的拓扑初始化、4主体/2期限及末尾真Flush取消，不设置业务硬上限")]
    public async Task StructureInitialization_MeasuresBoundedSharedGraphsAndCancellation()
    {
        var cost = new StructureCost();
        var graph = new CapacityGraphClock(cost);
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(services =>
        {
            services.AddScoped<EfPlatformTransactionExecutor>();
            services.Replace(ServiceDescriptor.Scoped<IPlatformTransactionExecutor>(provider => new ObservedFlushes(provider.GetRequiredService<EfPlatformTransactionExecutor>(), cost)));
            services.AddDbContext<EfPlatformDbContext>((_, options) => options.AddInterceptors(cost, graph, new CapacityTransactionClock(graph)));
        }, includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        var department = await InitializeAsync(fixture, admin);
        var position = await ContainerAsync(fixture, admin);
        var parentLink = Assert.Single(await RowsAsync(fixture, "WorkspaceFolderContent"), row => Equals(row["LID"], position.LinkIds[^1]));
        var parent = new Target("WorkspaceFolder", (string)parentLink["CEID"]!, position.LinkIds[^1], "归属维护目录");
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "ParentCapacity298", "父初始化容量", "parent-capacity298", "父初始化容量账号",
            Point(fixture, "workspace.spaces.access"), Point(fixture, "workspace.folder.pasteFolderStructure"));
        using var ordinary = actor.Client;
        await SaveAsync(fixture, admin, position with { LinkIds = [] }, parent,
            [new("CapacityCreator298", "creator", null, 0, new Dictionary<string, string> { ["history"] = "allow" }, ExpiresAt: "2099-05-01T00:00:00Z"),
             new("CapacityOwner298", "owner", null, 3, new Dictionary<string, string> { ["read"] = "allow" }),
             new("CapacityUser298", "user", actor.AccountId, 1, new Dictionary<string, string> { ["read"] = "allow", ["edit"] = "allow", ["authorize"] = "deny" }),
             new("CapacityDepartment298", "department", department, 7, new Dictionary<string, string> { ["history"] = "allow" }, new(true), "2099-06-01T00:00:00Z")]);

        foreach (var units in new[] { 1, 6 })
        {
            var (source, sourceIds) = await BuildCapacityGraphAsync(fixture, admin, position, units);
            var edges = (await RowsAsync(fixture, "WorkspaceFolderContent")).Where(row => sourceIds.Contains((string)row["PEID"]!)
                && sourceIds.Contains((string)row["CEID"]!)).ToArray();
            var depths = new Dictionary<string, int>(StringComparer.Ordinal) { [source.Id] = 1 };
            var paths = new Dictionary<string, long>(StringComparer.Ordinal) { [source.Id] = 1 };
            foreach (var id in sourceIds.Skip(1))
            {
                var parents = edges.Where(row => Equals(row["CEID"], id)).Select(row => (string)row["PEID"]!).ToArray();
                depths[id] = parents.Max(parentId => depths[parentId]) + 1;
                paths[id] = parents.Sum(parentId => paths[parentId]);
            }
            var dimensions = new { sourceFolders = sourceIds.Length, internalEdges = edges.Length, depth = depths.Values.Max(),
                extraSharedIncomingEdges = edges.GroupBy(row => row["CEID"]).Sum(group => Math.Max(0, group.Count() - 1)),
                conceptualLeafPaths = paths[sourceIds[^1]], parentDirectEntries = 4, parentAbsoluteExpiries = 2 };
            Assert.Equal(1 + 3 * units, dimensions.sourceFolders); Assert.Equal(4 * units, dimensions.internalEdges);
            Assert.Equal(1 + 2 * units, dimensions.depth); Assert.Equal(units, dimensions.extraSharedIncomingEdges);
            // 路径数只由拓扑计数派生，不枚举64条出现路径。
            Assert.Equal(1L << units, dimensions.conceptualLeafPaths);
            object Input(string name) => new { mode = "copy", sourcePosition = position, linkIds = new[] { source.LinkId }, targetPosition = position, name, copyParentAuthorization = true };
            var before = await RowsAsync(fixture, "WorkspaceFolder");
            var beforeIds = before.Select(row => (string)row["EID"]!).ToArray();
            var measured = await MeasureCapacityAsync(cost, graph, async () =>
            {
                using var response = await FeatureHttpTestRequests.SendAsync(ordinary, Point(fixture, "workspace.folder.pasteFolderStructure"), HttpMethod.Post,
                    "/api/workspace/content/folders/copy-structure", Input("父初始化容量副本" + units));
                await ReadOkAsync(response);
            });
            testOutput.WriteLine("PARENT_AUTHORIZATION_CAPACITY " + JsonSerializer.Serialize(new { scenario = "success", dimensions, measured,
                actorKind = "ordinary-limited-features", scope = "Single isolated request; graph application lock only, no SQL internal lock-wait measurement; diagnostic evidence without SLA or business limit." }));
            var foldersAfter = await RowsAsync(fixture, "WorkspaceFolder");
            var created = foldersAfter.Where(row => !beforeIds.Contains((string)row["EID"]!)).ToArray();
            Assert.Equal(dimensions.sourceFolders, created.Length);
            var ids = created.Select(row => (string)row["EID"]!).ToArray();
            Assert.Equal(dimensions.internalEdges + 1, (await RowsAsync(fixture, "WorkspaceFolderContent")).Count(row => ids.Contains((string)row["CEID"]!)));
            var entries = (await RowsAsync(fixture, "DataScopeObjectSubject")).Where(row => ids.Contains((string)row["ObjectId"]!)).ToArray();
            Assert.Equal(4 * ids.Length, entries.Length); Assert.Equal(2 * ids.Length, entries.Count(row => row["ExpiresAt"] is not null));
            var entryIds = entries.Select(row => (string)row["EID"]!).ToArray();
            Assert.Equal(6 * ids.Length, (await RowsAsync(fixture, "DataScopeObjectAction")).Count(row => entryIds.Contains((string)row["EntryId"]!)));
            foreach (var original in before) Assert.Contains(foldersAfter, row => Snapshot([row]) == Snapshot([original]));
            if (units != 6) continue;

            var rollbackBefore = await TransactionSnapshotAsync(fixture);
            var initialized = new List<string>(); var completed = 0; var flushed = false;
            using var cancellation = new CancellationTokenSource();
            fixture.Fault.AfterRegister = async (change, token) =>
            {
                if (change.EventType == "workspace.folder.parentAuthorizationInitialized") initialized.Add(change.TargetId);
                if (change.EventType != "workspace.folder.structureCopied" || ++completed != dimensions.sourceFolders) return;
                var context = fixture.Services.GetRequiredService<Microsoft.AspNetCore.Http.IHttpContextAccessor>().HttpContext!;
                await context.RequestServices.GetRequiredService<IPlatformTransactionExecutor>().FlushAsync(token);
                var db = context.RequestServices.GetRequiredService<EfPlatformDbContext>();
                Assert.Equal(dimensions.sourceFolders, initialized.Count);
                Assert.Equal(dimensions.internalEdges + 1, await db.Set<Dictionary<string, object?>>("WorkspaceFolderContent").AsNoTracking()
                    .CountAsync(row => initialized.Contains(EF.Property<string>(row, "CEID")), token));
                Assert.Equal(4 * dimensions.sourceFolders, await db.Set<Dictionary<string, object?>>("DataScopeObjectSubject").AsNoTracking()
                    .CountAsync(row => initialized.Contains(EF.Property<string>(row, "ObjectId")), token));
                flushed = true; cancellation.Cancel(); await Task.Delay(Timeout.InfiniteTimeSpan, token);
            };
            try
            {
                measured = await MeasureCapacityAsync(cost, graph, async () =>
                {
                    using var request = new HttpRequestMessage(HttpMethod.Post, "/api/workspace/content/folders/copy-structure")
                    { Content = JsonContent.Create(Input("父初始化容量取消"), options: new JsonSerializerOptions()) };
                    request.Headers.Add("X-NewP-Feature", Point(fixture, "workspace.folder.pasteFolderStructure").Position);
                    await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                    {
                        using var response = await ordinary.SendAsync(request, cancellation.Token);
                        Assert.Fail($"未进入真实取消点，HTTP {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
                    });
                    await graph.RolledBack.Task.WaitAsync(TimeSpan.FromSeconds(30));
                });
            }
            finally { fixture.Fault.AfterRegister = null; }
            Assert.True(flushed); Assert.Equal(rollbackBefore, await TransactionSnapshotAsync(fixture));
            testOutput.WriteLine("PARENT_AUTHORIZATION_CAPACITY " + JsonSerializer.Serialize(new { scenario = "cancel-after-flush", dimensions, measured,
                actorKind = "ordinary-limited-features", scope = "Includes real SQL flush, request cancellation and observed transaction rollback; no inferred SQL internal lock waits or capacity approval." }));
        }
    }

    private static async Task<(Target Source, string[] Ids)> BuildCapacityGraphAsync(BusinessAuditHostFixture fixture, HttpClient admin, Position position, int units)
    {
        var source = await CreateAsync(fixture, admin, position, "WorkspaceFolder", "folders", "create", "容量源" + units);
        var ids = new List<string> { source.Id }; var current = Within(position, source);
        for (var unit = 0; unit < units; unit++)
        {
            var left = await CreateAsync(fixture, admin, current, "WorkspaceFolder", "folders", "create", $"容量左{units}-{unit}");
            var right = await CreateAsync(fixture, admin, current, "WorkspaceFolder", "folders", "create", $"容量右{units}-{unit}");
            var merge = await CreateAsync(fixture, admin, Within(current, left), "WorkspaceFolder", "folders", "create", $"容量共享{units}-{unit}");
            using var response = await FeatureHttpTestRequests.SendAsync(admin, Point(fixture, "workspace.folder.add"), HttpMethod.Post,
                "/api/workspace/content/references", new { position = Within(current, right), target = new { modelKey = "WorkspaceFolder", id = merge.Id } });
            await ReadOkAsync(response);
            ids.AddRange([left.Id, right.Id, merge.Id]); current = Within(Within(current, left), merge);
        }
        return (source, ids.ToArray());
    }

    private static async Task<object> MeasureCapacityAsync(StructureCost cost, CapacityGraphClock graph, Func<Task> action)
    {
        using var process = Process.GetCurrentProcess(); process.Refresh();
        var baseline = process.WorkingSet64; long peak = baseline; var samples = 0;
        var allocated = GC.GetTotalAllocatedBytes(precise: true);
        using var stop = new CancellationTokenSource();
        var sampler = Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
            {
                process.Refresh(); Interlocked.Exchange(ref peak, Math.Max(Interlocked.Read(ref peak), process.WorkingSet64)); Interlocked.Increment(ref samples);
                try { await Task.Delay(10, stop.Token); } catch (OperationCanceledException) { }
            }
        });
        cost.Commands = cost.Flushes = 0; graph.Begin(); cost.Active = true; var timer = Stopwatch.StartNew();
        try { await action(); }
        finally { timer.Stop(); cost.Active = false; stop.Cancel(); await sampler; }
        return new { totalMs = timer.Elapsed.TotalMilliseconds, sqlCommands = cost.Commands, flushCalls = cost.Flushes,
            graphApplicationLockWaitMs = graph.WaitMs, graphApplicationLockHoldMs = graph.HoldMs, graphTransactionMs = graph.TransactionMs,
            transactionOutcome = graph.Outcome, processWorkingSetBaselineBytes = baseline, sampledPeakWorkingSetBytes = peak,
            sampledWorkingSetIncreaseBytes = peak - baseline, workingSetSamples = samples, processAllocatedDeltaBytes = GC.GetTotalAllocatedBytes(precise: true) - allocated };
    }

    private sealed class CapacityGraphClock(StructureCost cost) : DbCommandInterceptor
    {
        internal readonly Dictionary<DbTransaction, long> Started = [];
        internal DbTransaction? Transaction; internal long Waiting; internal long Acquired;
        internal double WaitMs; internal double HoldMs; internal double TransactionMs; internal string? Outcome;
        internal TaskCompletionSource RolledBack = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal void Begin() { Started.Clear(); Transaction = null; Waiting = Acquired = 0; WaitMs = HoldMs = TransactionMs = 0; Outcome = null; RolledBack = new(TaskCreationOptions.RunContinuationsAsynchronously); }
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken token = default)
        { if (cost.Active && Waiting == 0 && command.CommandText.Contains("Workspace.FolderGraph", StringComparison.Ordinal)) Waiting = Stopwatch.GetTimestamp(); return ValueTask.FromResult(result); }
        public override ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData, int result, CancellationToken token = default)
        { if (cost.Active && Acquired == 0 && command.CommandText.Contains("Workspace.FolderGraph", StringComparison.Ordinal)) { Acquired = Stopwatch.GetTimestamp(); WaitMs = Stopwatch.GetElapsedTime(Waiting, Acquired).TotalMilliseconds; Transaction = command.Transaction; } return ValueTask.FromResult(result); }
        internal void Start(DbTransaction transaction) { if (cost.Active) Started[transaction] = Stopwatch.GetTimestamp(); }
        internal void End(DbTransaction transaction, string outcome)
        {
            if (!cost.Active || !ReferenceEquals(transaction, Transaction)) return;
            var ended = Stopwatch.GetTimestamp(); HoldMs = Stopwatch.GetElapsedTime(Acquired, ended).TotalMilliseconds;
            if (Started.TryGetValue(transaction, out var started)) TransactionMs = Stopwatch.GetElapsedTime(started, ended).TotalMilliseconds;
            Outcome = outcome; if (outcome == "rolledBack") RolledBack.TrySetResult();
        }
    }
    private sealed class CapacityTransactionClock(CapacityGraphClock graph) : DbTransactionInterceptor
    {
        public override ValueTask<DbTransaction> TransactionStartedAsync(DbConnection connection, TransactionEndEventData eventData, DbTransaction result, CancellationToken token = default) { graph.Start(result); return ValueTask.FromResult(result); }
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken token = default) { graph.End(transaction, "committed"); return Task.CompletedTask; }
        public override Task TransactionRolledBackAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken token = default) { graph.End(transaction, "rolledBack"); return Task.CompletedTask; }
    }
}
