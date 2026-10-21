using System.Diagnostics;
using System.Net;
using System.Text.Json;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Modules.Authorization.DTOs;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static ICP.Platform.HostApi.Tests.EntityOwnershipBackfillSqlServerHttpApiTests;

namespace ICP.Platform.HostApi.Tests;

public sealed partial class DataScopePermissionCostSqlServerTests
{
    [EnvironmentGatedFact("NEWP_RUN_RULE_CACHE_CAPACITY", "独占测量100规则节点、每节点10主体、并发1/16/64")]
    public Task RuleCache_100Nodes() => MeasureRuleCacheAsync(100);

    [EnvironmentGatedFact("NEWP_RUN_RULE_CACHE_CAPACITY", "独占测量1000规则节点、每节点10主体、并发1/16/64")]
    public Task RuleCache_1000Nodes() => MeasureRuleCacheAsync(1000);

    private async Task MeasureRuleCacheAsync(int nodes)
    {
        var commands = new CostCommands();
        var phase = Environment.GetEnvironmentVariable("NEWP_RULE_CACHE_CANDIDATE") ?? "unspecified";
        var directory = Environment.GetEnvironmentVariable("NEWP_RULE_CACHE_EVIDENCE")
            ?? throw new InvalidOperationException("NEWP_RULE_CACHE_EVIDENCE must select a durable evidence directory.");
        Directory.CreateDirectory(directory);
        var evidencePath = Path.Combine(directory, $"{phase}-{nodes}.jsonl");
        var started = Stopwatch.StartNew();
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(services =>
            services.AddDbContext<EfPlatformDbContext>((_, options) => options.AddInterceptors(commands)), includeWorkspace: true);
        Emit(new { phase = "host-start-empty", milliseconds = started.Elapsed.TotalMilliseconds });
        using var admin = await fixture.LoginAdministratorAsync();
        await InitializeAsync(fixture, admin);
        var position = await ContainerAsync(fixture, admin);
        var part = await CreateAsync(fixture, admin, position, "Part", "parts", "createPart", "RuleCachePart");
        var document = await CreateAsync(fixture, admin, position, "Document", "documents", "createDocument", "RuleCacheDocument");
        var reads = new[] { "Part", "Document" }.Select(module => fixture.FeatureCatalog.Single(point =>
            point.ModuleId == module && point.Kind == "navigation" && point.Key == module.ToLowerInvariant() + "." + (module == "Part" ? "parts" : "documents"))).ToArray();
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "RuleCacheCapacity", "规则缓存容量", "rule-cache-capacity", "规则缓存容量账号", reads);
        using var ordinary = actor.Client;
        var save = fixture.FeatureCatalog.Single(point => point.BehaviorKey == "authorization.dataScope.save");
        var roots = Enumerable.Range(0, nodes / 4).Select(index => Node(index, 0, null)).ToArray();
        foreach (var root in roots) await SaveAsync(root);
        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
            Assert.Equal(nodes, await db.Set<Dictionary<string, object?>>("DataScopeRuleNode").CountAsync());
            Assert.Equal(nodes * 10, await db.Set<Dictionary<string, object?>>("DataScopeRuleSubject").CountAsync());
        }
        // 相同正式Host重启区分完整初载和首次请求；不把准备阶段或请求分配量称为缓存驻留。
        await CaptureAsync("host-restart-with-rules", 0, 0, async _ => await fixture.RestartWithReadActionAsync(true));
        var sources = reads.Select(read => fixture.Services.GetRequiredService<CanonicalModulePlan>().ActionDataAccessCatalog.Single(source =>
            source.Execution == read.ModuleId.ToLowerInvariant() + "." + read.ModuleId.ToLowerInvariant() + ".authorization.view"
            && source.Provider == "authorization.object.view"
            && (read.ModuleId == "Part" ? source.ViewKey == "part.part.list" && source.ActionKey == "viewAuthorization"
                : source.ViewKey == "document.document.permissions" && source.Surface == "read")
            && source.FeaturePositions.Contains(read.Position))).ToArray();
        await CaptureAsync("first-request", 1, 1, ReadAsync);
        foreach (var concurrency in new[] { 1, 16, 64 })
            await CaptureAsync("steady", concurrency, 64, ReadAsync);
        await CaptureAsync("maintenance", 1, 1, _ => SaveAsync(roots[0] with { Enabled = false }));
        // 保存与普通用户读取共享同一规模；行为交错另由受控事务测试证明，不靠容量偶然竞态。
        await CaptureAsync("reads-during-save", 16, 64, async index =>
        {
            if (index == 0) await SaveAsync(roots[0]);
            else await ReadAsync(index);
        });

        async Task ReadAsync(int index)
        {
            var target = index % 2 == 0 ? part : document;
            var kind = index % 2;
            var path = $"/api/{target.Model.ToLowerInvariant()}/{(kind == 0 ? "parts" : "documents")}/{target.Id}/permissions/explanation";
            var result = await FeatureHttpTestRequests.ReadAsync(ordinary, reads[kind], path, sources[kind]);
            Assert.NotEmpty(result.GetProperty("actions").EnumerateArray());
        }

        async Task SaveAsync(DataScopeRuleDto root)
        {
            using var response = await new FeatureHttpTestRequests(fixture).SendButtonAsync(admin, save, HttpMethod.Put,
                "/api/authorization/data-scope/rules/" + root.Id, root);
            Assert.True(response.StatusCode == HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
        }

        DataScopeRuleDto Node(int root, int depth, string? parent)
        {
            var id = $"Capacity338_{root}_{depth}";
            var resource = root % 2 == 0 ? "part.part" : "document.document";
            var objectId = root % 2 == 0 ? part.Id : document.Id;
            return new(id, $"Capacity338_{root}_0", parent, "容量规则" + id, true, resource,
                new("attribute", "EID", "equal", JsonSerializer.SerializeToElement(objectId)),
                Enumerable.Range(0, 10).Select(subject => new DataScopeSubjectDto(id + "_s" + subject, "all", null, subject,
                    new Dictionary<string, string> { ["read"] = "allow" })).ToArray(),
                depth == 3 ? [] : [Node(root, depth + 1, id)]);
        }

        async Task CaptureAsync(string measurement, int concurrency, int count, Func<int, Task> action)
        {
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            var retainedBefore = GC.GetTotalMemory(false);
            var allocatedBefore = GC.GetTotalAllocatedBytes(true);
            commands.Begin();
            var elapsed = Stopwatch.StartNew();
            var latencies = new double[Math.Max(count, 1)];
            try
            {
                if (concurrency == 0) await action(0);
                else await Parallel.ForEachAsync(Enumerable.Range(0, count), new ParallelOptions { MaxDegreeOfParallelism = concurrency }, async (index, _) =>
                {
                    var watch = Stopwatch.StartNew();
                    await action(index);
                    latencies[index] = watch.Elapsed.TotalMilliseconds;
                });
            }
            finally { commands.End(); }
            elapsed.Stop();
            var allocated = GC.GetTotalAllocatedBytes(true) - allocatedBefore;
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            Array.Sort(latencies);
            var statements = commands.Items.ToArray();
            Emit(new { phase = measurement, concurrency, requests = count, elapsedMilliseconds = elapsed.Elapsed.TotalMilliseconds,
                requestsPerSecond = count / elapsed.Elapsed.TotalSeconds,
                p50 = latencies[(latencies.Length - 1) / 2], p95 = latencies[(int)((latencies.Length - 1) * .95)], p99 = latencies[(int)((latencies.Length - 1) * .99)],
                allocatedBytes = allocated, retainedManagedBytesBefore = retainedBefore, retainedManagedBytesAfter = GC.GetTotalMemory(false),
                processWorkingSetBytes = Process.GetCurrentProcess().WorkingSet64,
                sqlCommands = statements.Length, ruleTableCommands = statements.Count(item => item.Sql.Contains("[DataScopeRuleNode]", StringComparison.Ordinal)
                    || item.Sql.Contains("[DataScopeRuleSubject]", StringComparison.Ordinal)),
                sqlExecuteMilliseconds = statements.Sum(item => item.ExecuteMilliseconds),
                sql = statements.GroupBy(item => item.Sql).Select(group => new { statement = group.Key, count = group.Count() }) });
        }

        void Emit(object result)
        {
            var line = JsonSerializer.Serialize(new { candidate = phase, ruleNodes = nodes, subjectsPerNode = 10, depth = 4, resources = 2, result });
            File.AppendAllText(evidencePath, line + Environment.NewLine);
            output.WriteLine(line);
        }
    }
}
