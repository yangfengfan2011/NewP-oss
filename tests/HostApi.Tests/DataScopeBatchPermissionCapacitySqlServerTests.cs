using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Modules.Authorization.DTOs;
using ICP.Platform.Persistence.Abstractions;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Testing;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Data.SqlClient;
using Xunit;
using static ICP.Platform.HostApi.Tests.EntityOwnershipBackfillSqlServerHttpApiTests;

namespace ICP.Platform.HostApi.Tests;

public sealed partial class DataScopePermissionCostSqlServerTests
{
    [Fact]
    public void BatchCostReadSnapshot_PreservesBeforeParametersAndExcludesWrites()
    {
        using var command = new SqlCommand("SELECT * FROM [DataScopeRuleNode] WHERE [ResourceKey]=@resource");
        command.Parameters.AddWithValue("@resource", "part.part");
        var sample = Assert.IsType<ExampleExecutionSqlPlanTests.Sample>(CostCommands.SnapshotRead(command));
        command.Parameters[0].Value = "document.document";
        command.Parameters.Clear();
        Assert.Equal("part.part", Assert.Single(sample.Parameters).Value);
        command.CommandText = "UPDATE [DataScopeRuleNode] SET [Enabled]=0";
        Assert.Null(CostCommands.SnapshotRead(command));
    }

    [EnvironmentGatedFact("NEWP_RUN_ENTITY_PERMISSION_CAPACITY", "最小真实混合准备确认现有成本资产及关键只读IO/计划链，不记为17/65样本")]
    public Task MixedBatchCostAsset_UsesRealPreparationAndReadPlans() => MeasureMixedBatchAsync(1, readPlanControlOnly: true);

    [EnvironmentGatedFact("NEWP_RUN_ENTITY_PERMISSION_CAPACITY", "独占测量17个实际混合对象、共享来源、主体和规则，不设置业务上限")]
    public Task MixedBatch_Records17TargetGraphSubjectRuleAndCancellationCosts() => MeasureMixedBatchAsync(8);

    [EnvironmentGatedFact("NEWP_RUN_ENTITY_PERMISSION_CAPACITY", "独占测量65个实际混合对象、共享来源、主体和规则，不设置业务上限")]
    public Task MixedBatch_Records65TargetGraphSubjectRuleAndCancellationCosts() => MeasureMixedBatchAsync(32);

    private async Task MeasureMixedBatchAsync(int perResource, bool readPlanControlOnly = false)
    {
        var stage = Environment.GetEnvironmentVariable("NEWP_DATA_SCOPE_BATCH_COST_STAGE");
        if (readPlanControlOnly) Assert.False(string.IsNullOrWhiteSpace(stage));
        var commands = new CostCommands { CaptureReadSamples = stage is not null };
        var connections = new CostConnections(); var transactions = new CostTransactions();
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(services =>
            services.AddDbContext<EfPlatformDbContext>((_, options) => options.AddInterceptors(commands, connections, transactions)), includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        await InitializeAsync(fixture, admin);
        var parent = await ContainerAsync(fixture, admin);
        var folder = await CreateAsync(fixture, admin, parent, "WorkspaceFolder", "folders", "create", "BatchCapacity297Folder");
        var position = parent with { LinkIds = [.. parent.LinkIds, folder.LinkId] };
        var targets = new List<Target> { folder };
        // 全部Document均走真实初版/iteration创建，不能复制LatestVersionId种出不合法模型。
        for (var i = 0; i < perResource; i++)
        {
            targets.Add(await CreateAsync(fixture, admin, position, "Part", "parts", "createPart", "BatchCapacity297Part" + i));
            targets.Add(await CreateAsync(fixture, admin, position, "Document", "documents", "createDocument", "BatchCapacity297Doc" + i));
        }
        var part = targets.First(target => target.Model == "Part");
        var document = targets.First(target => target.Model == "Document");
        using (var added = await FeatureHttpTestRequests.SendAsync(admin, Point(fixture, "workspace.folder.add"), HttpMethod.Post,
            "/api/workspace/content/references", new { position = parent, target = new { modelKey = "Part", id = part.Id } }))
            await ReadOkAsync(added);
        string sharedLink;
        using (var scope = fixture.Services.CreateScope())
        {
            var links = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>().Set<Dictionary<string, object?>>("WorkspaceFolderContent");
            var parentId = await links.AsNoTracking().Where(row => EF.Property<string>(row, "LID") == folder.LinkId)
                .Select(row => EF.Property<string>(row, "PEID")).SingleAsync();
            sharedLink = await links.AsNoTracking().Where(row => EF.Property<string>(row, "PEID") == parentId && EF.Property<string>(row, "CEID") == part.Id)
                .Select(row => EF.Property<string>(row, "LID")).SingleAsync();
        }
        var batch = fixture.FeatureCatalog.Single(point => point.Key == "batchAuthorize" && point.ActionScope == "list.pageActions");
        var batchSource = Assert.Single(fixture.Services.GetRequiredService<CanonicalModulePlan>().ActionDataAccessCatalog,
            declaration => declaration.Execution == "workspace.content.authorization.batch" && declaration.FeaturePositions.Contains(batch.Position));
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "BatchCapacity297", "批量容量角色", "batchcapacity297", "批量容量主体",
            Point(fixture, "workspace.spaces.access"), batch);
        using var ordinary = actor.Client;
        var current = await ordinary.GetFromJsonAsync<JsonElement>("/api/auth/current-user");
        Assert.False(current.TryGetProperty("isLocalAdministrator", out var localAdministrator) && localAdministrator.GetBoolean());
        var groups = new[] { (Resource: "workspace.folder", Target: folder), (Resource: "part.part", Target: part), (Resource: "document.document", Target: document) };
        var saveRules = Point(fixture, "authorization.dataScope.save");
        foreach (var group in groups)
        {
            var root = "BatchCapacity297_" + group.Target.Model;
            using var response = await new FeatureHttpTestRequests(fixture).SendButtonAsync(admin, saveRules, HttpMethod.Put,
                "/api/authorization/data-scope/rules/" + root + "_0", Rule(root, group.Resource, group.Target.Id, null, 0));
            Assert.True(response.StatusCode == HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
        }
        using (var scope = fixture.Services.CreateScope())
        {
            var facts = await scope.ServiceProvider.GetServices<IDataScopeIdentityProvider>().Single(provider =>
                provider.SubjectTypes.Any(type => type.Key == "role")).ResolveAsync(actor.AccountId, default);
            Assert.True(facts.Matches("role", actor.RoleId, new()));
            Assert.False(facts.Matches("role", "unknown-batch-capacity-role", new()));
        }
        var sources = new[] { new { position = parent, linkIds = new[] { folder.LinkId, sharedLink } } };
        var targetCount = 1 + perResource * 2;
        var prepared = await MeasureAsync("complete-graph-prepare", async () =>
        {
            using var response = await FeatureHttpTestRequests.SendAsync(ordinary, batch, HttpMethod.Post,
                "/api/workspace/content/permissions/batch/prepare", new { sources, includeDescendants = true }, batchSource);
            return await ReadOkAsync(response);
        });
        Assert.Equal(targetCount, prepared.GetProperty("total").GetInt32());
        var items = prepared.GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(targetCount, items.Length);
        Assert.All(items, item => Assert.True(item.GetProperty("manageable").GetBoolean()));
        Assert.Single(items, item => item.GetProperty("objectId").GetString() == part.Id);
        if (readPlanControlOnly) return;
        var administrator = await admin.GetFromJsonAsync<JsonElement>("/api/auth/current-user");
        var filtered = await MeasureAsync("creator-owner-type-filter", async () =>
        {
            using var response = await FeatureHttpTestRequests.SendAsync(ordinary, batch, HttpMethod.Post,
                "/api/workspace/content/permissions/batch/prepare", new { sources, includeDescendants = true,
                    filters = new { resourceKeys = new[] { "part.part" }, createdBy = administrator.GetProperty("id").GetString(), ownerId = administrator.GetProperty("id").GetString() } }, batchSource);
            return await ReadOkAsync(response);
        });
        Assert.Equal(perResource, filtered.GetProperty("total").GetInt32());
        var finalSources = items.Select(item => item.GetProperty("source")).ToArray();
        object Input(long priority) => new { sources = finalSources, mode = "U", resources = groups.Select(group => new { resourceKey = group.Resource,
            subjects = new[] { (Type: "all", Id: (string?)null), (Type: "user", Id: (string?)actor.AccountId),
                (Type: "role", Id: (string?)actor.RoleId), (Type: "owner", Id: (string?)null) }.Select(selector => new {
                    type = selector.Type, subjectId = selector.Id, priority,
                    actions = new Dictionary<string, string> { ["read"] = "allow", ["authorize"] = "allow", ["edit"] = "deny" },
                    expiresAt = "2099-01-01T00:00:00Z" }).ToArray() }).ToArray() };
        var saved = await MeasureAsync("atomic-mixed-batch-save", async () =>
        {
            using var response = await FeatureHttpTestRequests.SendAsync(ordinary, batch, HttpMethod.Post,
                "/api/workspace/content/permissions/batch", Input(7), batchSource);
            return await ReadOkAsync(response);
        });
        Assert.Equal(targetCount, saved.GetProperty("changed").GetInt32());
        var configurations = await MeasureAsync("qualified-current-whole-set", async () =>
        {
            using var response = await FeatureHttpTestRequests.SendAsync(ordinary, batch, HttpMethod.Post,
                "/api/workspace/content/permissions/batch/current", new { sources = finalSources }, batchSource);
            return await ReadOkAsync(response);
        });
        Assert.Equal(targetCount, configurations.GetArrayLength());
        Assert.All(configurations.EnumerateArray(), configuration => Assert.Equal(4, configuration.GetProperty("subjects").GetArrayLength()));
        // 实际普通主体的解释必须消费四层规则和三个匹配直接selector，不能用管理员短路或未发布规则冒充成本。
        foreach (var group in groups)
        {
            var location = items.Single(item => item.GetProperty("objectId").GetString() == group.Target.Id).GetProperty("source");
            var path = "/api/workspace/content/" + location.GetProperty("linkIds")[0].GetString() + "/permissions/explanation?queryScope="
                + Uri.EscapeDataString(location.GetProperty("position").GetRawText());
            using var response = await FeatureHttpTestRequests.SendAsync(ordinary, batch, HttpMethod.Get, path, dataAccess: batchSource);
            var explanation = await ReadOkAsync(response);
            var read = explanation.GetProperty("actions").EnumerateArray().Single(action => action.GetProperty("key").GetString() == "read");
            var origins = read.GetProperty("sources").EnumerateArray().ToArray();
            Assert.Equal(4, origins.Count(origin => origin.GetProperty("kind").GetString() == "rule"));
            Assert.Equal(3, Assert.Single(origins, origin => origin.GetProperty("kind").GetString() == "object").GetProperty("subjects").GetArrayLength());
        }
        var before = await BatchSnapshotAsync();
        var seen = 0; var flushed = false; long cancelledAt = 0;
        using var cancellation = new CancellationTokenSource();
        fixture.Fault.AfterRegister = async (change, token) =>
        {
            if (change.EventType != "dataScope.object.changed" || ++seen != targetCount) return;
            var context = fixture.Services.GetRequiredService<IHttpContextAccessor>().HttpContext!;
            await context.RequestServices.GetRequiredService<IPlatformTransactionExecutor>().FlushAsync(token);
            flushed = true; cancelledAt = Stopwatch.GetTimestamp(); cancellation.Cancel();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
        };
        try
        {
            await MeasureAsync("all-targets-flushed-then-request-cancel", async () =>
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, "/api/workspace/content/permissions/batch");
                request.Headers.Add("X-NewP-Data-Declaration", batchSource.Id);
                request.Headers.Add("X-NewP-Feature", batch.Position); request.Content = JsonContent.Create(Input(9), options: new JsonSerializerOptions());
                await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                {
                    using var response = await ordinary.SendAsync(request, cancellation.Token);
                    Assert.Fail($"Expected cancellation, HTTP {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
                });
                Assert.True(flushed); Assert.Equal(targetCount, seen);
                return JsonSerializer.SerializeToElement(new { flushed, seen, cancelToObservedOceMilliseconds = Stopwatch.GetElapsedTime(cancelledAt).TotalMilliseconds });
            });
        }
        finally { fixture.Fault.AfterRegister = null; }
        Assert.Equal(before, await BatchSnapshotAsync());
        output.WriteLine(JsonSerializer.Serialize(new { sampleTargets = targetCount, cleanupBoundary = "HTTP completed; unchanged raw subjects/actions/events; fixture owned disposal follows" }));

        DataScopeRuleDto Rule(string root, string resource, string target, string? parentId, int level)
        {
            var id = root + "_" + level;
            return new(id, root + "_0", parentId, "批量容量规则" + id, true, resource,
                new("attribute", "EID", "equal", JsonSerializer.SerializeToElement(target)),
                [new(id + "Subject", "all", null, 0, new Dictionary<string, string> { ["read"] = "allow" })],
                level == 3 ? [] : [Rule(root, resource, target, id, level + 1)]);
        }
        async Task<string> BatchSnapshotAsync()
        {
            var ids = targets.Select(target => target.Id).ToArray();
            var keys = targets.Select(target => (target.Model == "Part" ? "part.part/" : target.Model == "Document" ? "document.document/" : "workspace.folder/") + target.Id).ToArray();
            using var scope = fixture.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
            var subjects = await db.Set<Dictionary<string, object?>>("DataScopeObjectSubject").AsNoTracking()
                .Where(row => EF.Parameter(ids).Contains(EF.Property<string>(row, "ObjectId"))).OrderBy(row => EF.Property<string>(row, "EID")).ToArrayAsync();
            var entries = subjects.Select(row => (string)row["EID"]!).ToArray();
            var actions = await db.Set<Dictionary<string, object?>>("DataScopeObjectAction").AsNoTracking()
                .Where(row => EF.Parameter(entries).Contains(EF.Property<string>(row, "EntryId"))).OrderBy(row => EF.Property<string>(row, "EID")).ToArrayAsync();
            var events = await db.Set<Dictionary<string, object?>>("BusinessChange").AsNoTracking()
                .Where(row => EF.Property<string>(row, "EventType") == "dataScope.object.changed" && EF.Parameter(keys).Contains(EF.Property<string>(row, "TargetId")))
                .OrderBy(row => EF.Property<string>(row, "EID")).ToArrayAsync();
            return JsonSerializer.Serialize(new { subjects, actions, events });
        }
        async Task<JsonElement> MeasureAsync(string phase, Func<Task<JsonElement>> action)
        {
            commands.Begin(); connections.Begin(); transactions.Begin();
            var allocated = GC.GetTotalAllocatedBytes(false); var watch = Stopwatch.StartNew();
            JsonElement result;
            try { result = await action(); }
            finally { commands.End(); connections.End(); transactions.End(); }
            watch.Stop();
            var allocatedBytes = GC.GetTotalAllocatedBytes(false) - allocated;
            var locks = commands.Items.Where(item => item.Sql.Contains("sp_getapplock", StringComparison.Ordinal)).ToArray();
            var evidence = new { stage, phase, sampleTargets = targetCount, parts = perResource, documents = perResource, folders = 1,
                sharedExtraLinks = 1, templateSelectorsPerResource = 4, matchingSelectorsPerAnchor = 3, ruleRoots = 3, ruleDepth = 4, ruleNodes = 12,
                elapsedMilliseconds = watch.Elapsed.TotalMilliseconds, allocatedBytes,
                outputBytes = System.Text.Encoding.UTF8.GetByteCount(result.GetRawText()), sqlCommands = commands.Items.Count,
                sqlExecuteMilliseconds = commands.Items.Sum(item => item.ExecuteMilliseconds), sqlParameters = commands.Items.Sum(item => item.Parameters), maxCommandParameters = commands.Items.Count == 0 ? 0 : commands.Items.Max(item => item.Parameters),
                connectionOpens = connections.Items.Count(item => item.Operation == "open"), connectionCloses = connections.Items.Count(item => item.Operation == "close"),
                connectionOpenMilliseconds = connections.Items.Where(item => item.Operation == "open").Sum(item => item.Milliseconds),
                connectionCloseMilliseconds = connections.Items.Where(item => item.Operation == "close").Sum(item => item.Milliseconds),
                transaction = transactions.Result, explicitLocks = locks.Length, lockHeldMilliseconds = locks.Length > 0 && transactions.Ended > 0
                    ? Stopwatch.GetElapsedTime(locks[0].CompletedTimestamp, transactions.Ended).TotalMilliseconds : (double?)null,
                result = phase == "all-targets-flushed-then-request-cancel" ? result : (JsonElement?)null, commands = commands.Items.ToArray() };
            output.WriteLine(JsonSerializer.Serialize(evidence));
            if (stage is not null)
            {
                var directory = new DirectoryInfo(AppContext.BaseDirectory);
                while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AGENTS.md"))) directory = directory.Parent;
                var run = Path.Combine(directory!.FullName, ".codex-temp", "issue-305", "318", "cost", stage, "targets-" + targetCount);
                Directory.CreateDirectory(run);
                await File.WriteAllTextAsync(Path.Combine(run, phase + ".json"), JsonSerializer.Serialize(evidence));
                // 配置在这三个只读请求前后不变；诊断连接/耗时/分配不混入原请求，也不冒充整请求IO。
                if (phase is "complete-graph-prepare" or "creator-owner-type-filter" or "qualified-current-whole-set")
                {
                    var samples = commands.Items.Select(item => item.ReadSample).OfType<ExampleExecutionSqlPlanTests.Sample>()
                        .DistinctBy(sample => sample.Sql).Take(3).ToArray();
                    Assert.NotEmpty(samples);
                    using var scope = fixture.Services.CreateScope();
                    var connectionString = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>().Database.GetConnectionString()!;
                    var plans = new List<ExampleExecutionSqlPlanTests.Measurement>();
                    foreach (var sample in samples)
                        plans.Add(await ExampleExecutionSqlPlanTests.ExplainAsync(connectionString, sample, requireIo: false));
                    await File.WriteAllTextAsync(Path.Combine(run, phase + "-read-plans.json"), JsonSerializer.Serialize(new {
                        stage, phase, scope = "at most3distinct DataScope SELECTs captured Before; unchanged permission configuration; not full-request IO", plans }));
                    output.WriteLine($"{stage}/targets-{targetCount}/{phase}: selectedReadPlans={plans.Count}, artifacts={run}");
                }
            }
            return result;
        }
    }
}
