using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Modules.Authorization.DTOs;
using ICP.Platform.Modules.Authorization.Caching;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static ICP.Platform.HostApi.Tests.EntityOwnershipBackfillSqlServerHttpApiTests;

namespace ICP.Platform.HostApi.Tests;

public sealed partial class DataScopePermissionCostSqlServerTests
{
    [EnvironmentGatedFact("NEWP_RUN_RULE_CACHE_CAPACITY", "先以8节点验证准备、两资源声明、重启client、保存与采集清理")]
    public Task RuleCache_EntryBindingsAndRestartClients() => MeasureRuleCacheAsync(8, true);

    [EnvironmentGatedFact("NEWP_RUN_RULE_CACHE_CAPACITY", "独占测量100规则节点、每节点10主体、并发1/16/64")]
    public Task RuleCache_100Nodes() => MeasureRuleCacheAsync(100);

    [EnvironmentGatedFact("NEWP_RUN_RULE_CACHE_CAPACITY", "独占测量1000规则节点、每节点10主体、并发1/16/64")]
    public Task RuleCache_1000Nodes() => MeasureRuleCacheAsync(1000);

    private async Task MeasureRuleCacheAsync(int nodes, bool entryProbe = false)
    {
        var commands = new CostCommands();
        var phase = Environment.GetEnvironmentVariable("NEWP_RULE_CACHE_CANDIDATE") ?? "unspecified";
        var directory = Environment.GetEnvironmentVariable("NEWP_RULE_CACHE_EVIDENCE")
            ?? throw new InvalidOperationException("NEWP_RULE_CACHE_EVIDENCE must select a durable evidence directory.");
        Directory.CreateDirectory(directory);
        var evidencePath = Path.Combine(directory, $"{phase}-{nodes}.jsonl");
        var started = Stopwatch.StartNew();
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(services =>
            RegisterRuleNaturalCosts(services, commands), includeWorkspace: true);
        Emit(new { phase = "host-start-empty", milliseconds = started.Elapsed.TotalMilliseconds,
            fullCacheLoadMilliseconds = fixture.Services.GetRequiredService<DataScopeRuleCache>().LastMaintenanceDuration?.TotalMilliseconds });
        using var preparationAdmin = await fixture.LoginAdministratorAsync();
        var admin = preparationAdmin;
        await InitializeAsync(fixture, admin);
        var position = await ContainerAsync(fixture, admin);
        var part = await CreateAsync(fixture, admin, position, "Part", "parts", "createPart", "RuleCachePart");
        var document = await CreateAsync(fixture, admin, position, "Document", "documents", "createDocument", "RuleCacheDocument");
        var reads = new[] { "Part", "Document" }.Select(module => fixture.FeatureCatalog.Single(point =>
            point.ModuleId == module && point.Kind == "navigation" && point.Key == module.ToLowerInvariant() + "." + (module == "Part" ? "parts" : "documents"))).ToArray();
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "RuleCacheCapacity", "规则缓存容量", "rule-cache-capacity", "规则缓存容量账号", reads);
        using var preparationOrdinary = actor.Client;
        var ordinary = preparationOrdinary;
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
        var loaded = fixture.Services.GetRequiredService<DataScopeRuleCache>();
        Assert.Equal(nodes * 11L, loaded.RecordCount);
        Assert.NotNull(loaded.LastMaintenanceDuration);
        Emit(new { phase = "cache-full-load", milliseconds = loaded.LastMaintenanceDuration.Value.TotalMilliseconds,
            records = loaded.RecordCount, memoryScope = "各阶段内存为整个测试进程观察值，含fixture/旧新Host和client，不是精确缓存对象驻留" });
        using var restartedAdmin = fixture.CreateClient();
        using var restartedOrdinary = fixture.CreateClient();
        await ReconnectAsync(restartedAdmin, "admin");
        await ReconnectAsync(restartedOrdinary, "rule-cache-capacity");
        admin = restartedAdmin;
        ordinary = restartedOrdinary;
        var sources = reads.Select(read => fixture.Services.GetRequiredService<CanonicalModulePlan>().ActionDataAccessCatalog.Single(source =>
            source.Execution == read.ModuleId.ToLowerInvariant() + "." + read.ModuleId.ToLowerInvariant() + ".authorization.view"
            && source.Provider == "authorization.object.view"
            && source.ViewKey == read.ModuleId.ToLowerInvariant() + "." + read.ModuleId.ToLowerInvariant() + ".permissions"
            && source.Surface == "read"
            && source.FeaturePositions.Contains(read.Position))).ToArray();
        await CaptureAsync("first-request", 1, 2, ReadAsync);
        if (entryProbe)
        {
            await ReadAsync(1);
            await VerifyRuleObservationAffinityAsync(fixture);
            await CaptureAsync("maintenance", 1, 1, _ => SaveAsync(roots[0] with { Enabled = false }));
            await SaveAsync(roots[0]);
            await ReadAsync(0);
            return;
        }
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

        async Task ReconnectAsync(HttpClient client, string code)
        {
            await BusinessAuditHostFixture.RefreshAntiforgeryAsync(client);
            using var response = await client.PostAsJsonAsync("/api/auth/login", new { ECODE = code, Password = "Audit-test-secure-938!" });
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            await BusinessAuditHostFixture.RefreshAntiforgeryAsync(client);
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
            fixture.Services.GetRequiredService<RuleNaturalPublicationCosts>().Samples.Clear();
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
            var naturalPublication = await Task.WhenAll(fixture.Services.GetRequiredService<RuleNaturalPublicationCosts>().Samples.ToArray());
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            Array.Sort(latencies);
            var statements = commands.Items.ToArray();
            var ruleCommands = statements.Count(item => item.Sql.Contains("[DataScopeRuleNode]", StringComparison.Ordinal)
                || item.Sql.Contains("[DataScopeRuleSubject]", StringComparison.Ordinal));
            if (measurement is "first-request" or "steady") Assert.Equal(0, ruleCommands);
            if (measurement is "maintenance" or "reads-during-save")
            {
                Assert.Single(naturalPublication);
                Assert.All(naturalPublication, sample => Assert.NotNull(sample.CommitToPublicationMilliseconds));
            }
            Emit(new { phase = measurement, concurrency, requests = count, elapsedMilliseconds = elapsed.Elapsed.TotalMilliseconds,
                requestsPerSecond = count / elapsed.Elapsed.TotalSeconds,
                p50 = latencies[(latencies.Length - 1) / 2], p95 = latencies[(int)((latencies.Length - 1) * .95)], p99 = latencies[(int)((latencies.Length - 1) * .99)],
                allocatedBytes = allocated, retainedManagedBytesBefore = retainedBefore, retainedManagedBytesAfter = GC.GetTotalMemory(false),
                processWorkingSetBytes = Process.GetCurrentProcess().WorkingSet64,
                sqlCommands = statements.Length, ruleTableCommands = ruleCommands,
                sqlExecuteMilliseconds = statements.Sum(item => item.ExecuteMilliseconds),
                naturalPublication,
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
