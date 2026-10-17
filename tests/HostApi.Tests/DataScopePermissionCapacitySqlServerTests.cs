using System.Data.Common;
using System.Diagnostics;
using System.Net;
using System.Security.Claims;
using System.Text.Json;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Modules.Authorization.DTOs;
using ICP.Platform.Modules.Authorization.Services;
using ICP.Platform.Persistence.Abstractions;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Testing;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static ICP.Platform.HostApi.Tests.EntityOwnershipBackfillSqlServerHttpApiTests;

namespace ICP.Platform.HostApi.Tests;

public sealed partial class DataScopePermissionCostSqlServerTests
{
    [EnvironmentGatedFact("NEWP_RUN_ENTITY_PERMISSION_CAPACITY", "独占测量32对象/角色/直接项与四层规则，不设产品上限")]
    public Task PartPermissions_Records32ObjectSubjectRuleAndReplacementCosts() => MeasurePartAsync(32);

    [EnvironmentGatedFact("NEWP_RUN_ENTITY_PERMISSION_CAPACITY", "独占测量128规模和跨100主体名称分片，不设产品上限")]
    public Task PartPermissions_Records128ObjectSubjectRuleAndReplacementCosts() => MeasurePartAsync(128);

    private async Task MeasurePartAsync(int size)
    {
        var commands = new CostCommands(); var connections = new CostConnections(); var transactions = new CostTransactions();
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(services =>
            services.AddDbContext<EfPlatformDbContext>((_, options) => options.AddInterceptors(commands, connections, transactions)), includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        await InitializeAsync(fixture, admin);
        var position = await ContainerAsync(fixture, admin);
        var anchor = await CreateAsync(fixture, admin, position, "Part", "parts", "createPart", "Capacity295Anchor");
        var read = fixture.FeatureCatalog.Single(point => point.ModuleId == "Part" && point.Kind == "navigation" && point.Key == "part.parts");
        var manage = fixture.FeatureCatalog.Single(point => point.ModuleId == "Part" && point.Key == "authorize" && point.ActionScope == "list.rowActions");
        var saveRules = fixture.FeatureCatalog.Single(point => point.BehaviorKey == "authorization.dataScope.save");
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "Capacity295", "容量角色", "capacity295", "容量账号", read, manage);
        using var ordinary = actor.Client;
        var roles = new List<string>();
        // 数据规模准备复制实际已创建的模型行；权限配置仍经正式API保存，不把128次对象创建成本混入解释。
        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
            var parts = db.Set<Dictionary<string, object?>>(db.ResolveModelKey("Part"));
            var template = await parts.AsNoTracking().SingleAsync(row => EF.Property<string>(row, "EID") == anchor.Id);
            for (var i = 1; i < size; i++)
                parts.Add(new(template) { ["EID"] = "Part_Capacity295_" + i, ["ECODE"] = "Capacity295-" + i, ["ENAME"] = "容量对象" + i });
            var roleRows = db.Set<Dictionary<string, object?>>(db.ResolveModelKey("Role"));
            var role = await roleRows.AsNoTracking().SingleAsync(row => EF.Property<string>(row, "EID") == actor.RoleId);
            var memberships = db.Set<Dictionary<string, object?>>(db.ResolveModelKey("UserRole"));
            var membership = await memberships.AsNoTracking().SingleAsync(row => EF.Property<string>(row, "RoleId") == actor.RoleId);
            for (var i = 0; i < size; i++)
            {
                var id = "Role_Capacity295_" + i; roles.Add(id);
                roleRows.Add(new(role) { ["EID"] = id, ["ECODE"] = "Capacity295Role" + i, ["ENAME"] = "容量角色" + i });
                memberships.Add(new(membership) { ["EID"] = "UserRole_Capacity295_" + i, ["RoleId"] = id });
            }
            await db.SaveChangesAsync();
        }
        using (var scope = fixture.Services.CreateScope())
        {
            var provider = scope.ServiceProvider;
            var facts = await provider.GetServices<IDataScopeIdentityProvider>().Single(item => item.SubjectTypes.Any(type => type.Key == "role"))
                .ResolveAsync(actor.AccountId, default);
            Assert.All(roles, role => Assert.True(facts.Matches("role", role, new())));
            Assert.False(facts.Matches("role", "unknown-role", new()));
            Assert.Equal(size, await provider.GetRequiredService<EfPlatformDbContext>().Set<Dictionary<string, object?>>("Part").CountAsync());
        }
        var rootCount = size / 8; const int depth = 4;
        for (var root = 0; root < rootCount; root++)
        {
            var rootId = "Capacity295Root" + root;
            using var response = await FeatureHttpTestRequests.SendAsync(admin, saveRules, HttpMethod.Put,
                "/api/authorization/data-scope/rules/" + rootId + "_0", Node(rootId, null, 0));
            Assert.True(response.StatusCode == HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
        }
        var path = "/api/part/parts/" + anchor.Id + "/permissions";
        var configuration = new DataScopeObjectDto("part.part", anchor.Id, roles.Select((id, index) => new DataScopeDirectSubjectDto(
            "Capacity295Subject" + index, "role", id, 0, new Dictionary<string, string> { ["read"] = "allow", ["edit"] = "deny" })).ToArray());
        using (var response = await FeatureHttpTestRequests.SendAsync(admin, manage, HttpMethod.Put, path, configuration))
            Assert.True(response.StatusCode == HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
        int historyBefore;
        using (var scope = fixture.Services.CreateScope())
            historyBefore = await scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>().Set<Dictionary<string, object?>>("BusinessChange").CountAsync();
        await MeasureAsync("catalog", () => FeatureHttpTestRequests.ReadAsync(ordinary, read, path + "/catalog"));
        var candidates = await MeasureAsync("role-candidates-page", () => FeatureHttpTestRequests.ReadAsync(ordinary, manage,
            path + "/subjects/role?query=" + Uri.EscapeDataString("容量角色") + "&page=1"));
        Assert.Equal(20, candidates.GetProperty("items").GetArrayLength());
        Assert.True(candidates.GetProperty("hasMore").GetBoolean());
        var stored = await MeasureAsync("configuration", () => FeatureHttpTestRequests.ReadAsync(ordinary, manage, path));
        Assert.Equal(size, stored.GetProperty("subjects").GetArrayLength());
        var inspection = await MeasureAsync("explanation", () => FeatureHttpTestRequests.ReadAsync(ordinary, read, path + "/explanation"));
        Assert.Equal(size, inspection.GetProperty("subjectNames").GetArrayLength());
        var readExplanation = inspection.GetProperty("actions").EnumerateArray().Single(action => action.GetProperty("key").GetString() == "read");
        Assert.Equal("object", readExplanation.GetProperty("reason").GetString());
        var sources = readExplanation.GetProperty("sources").EnumerateArray().ToArray();
        Assert.Equal(rootCount * depth, sources.Count(source => source.GetProperty("kind").GetString() == "rule"));
        Assert.Equal(size, Assert.Single(sources, source => source.GetProperty("kind").GetString() == "object").GetProperty("subjects").GetArrayLength());
        await MeasureAsync("sql-count-and-page", async () =>
        {
            using var scope = fixture.Services.CreateScope();
            var provider = scope.ServiceProvider; var accessor = provider.GetRequiredService<IHttpContextAccessor>();
            var previous = accessor.HttpContext;
            accessor.HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, actor.AccountId)], "test-trusted-account")) };
            try
            {
                var db = provider.GetRequiredService<EfPlatformDbContext>();
                var plan = await provider.GetRequiredService<IDataScopeReadPlanProvider>().GetReadPlanAsync("part.part");
                var query = db.Set<Dictionary<string, object?>>(db.ResolveModelKey("Part")).AsNoTracking();
                if (plan.Predicate is not null) query = query.Where(plan.Predicate);
                var count = await query.CountAsync();
                var page = await query.OrderBy(row => EF.Property<string>(row, "EID")).Take(20).Select(row => EF.Property<string>(row, "EID")).ToArrayAsync();
                Assert.Equal(size, count); Assert.Equal(Math.Min(size, 20), page.Length);
                return JsonSerializer.SerializeToElement(new { count, page });
            }
            finally { accessor.HttpContext = previous; }
        });
        var replacement = configuration with { Subjects = configuration.Subjects.Select(subject => subject with { Priority = 1, ExpiresAt = "2099-01-01T00:00:00Z" }).ToArray() };
        await MeasureAsync("full-replacement", async () =>
        {
            using var response = await FeatureHttpTestRequests.SendAsync(ordinary, manage, HttpMethod.Put, path, replacement);
            Assert.True(response.StatusCode == HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
            return JsonSerializer.SerializeToElement(new { status = (int)response.StatusCode });
        });
        var baseline = await FeatureHttpTestRequests.ReadAsync(admin, manage, path);
        var recordCount = await TargetRecordsAsync();
        var flushed = false;
        using (var scope = fixture.Services.CreateScope())
        using (var cancel = new CancellationTokenSource())
        using (var activity = new Activity("permission-capacity-cancel").Start())
        {
            var executor = scope.ServiceProvider.GetRequiredService<IPlatformTransactionExecutor>();
            fixture.Fault.AfterRegister = async (_, token) => { await executor.FlushAsync(token); flushed = true; cancel.Cancel(); token.ThrowIfCancellationRequested(); };
            try
            {
                await MeasureAsync("flush-then-cancel", async () =>
                {
                    await Assert.ThrowsAnyAsync<OperationCanceledException>(() => scope.ServiceProvider.GetRequiredService<DataScopeObjectGrants>()
                        .SaveAsync(replacement with { Subjects = replacement.Subjects.Select(subject => subject with { Priority = 2 }).ToArray() }, cancel.Token));
                    return JsonSerializer.SerializeToElement(new { flushed, cancelled = cancel.IsCancellationRequested });
                });
            }
            finally { fixture.Fault.AfterRegister = null; }
        }
        Assert.True(flushed);
        Assert.Equal(baseline.GetRawText(), (await FeatureHttpTestRequests.ReadAsync(admin, manage, path)).GetRawText());
        Assert.Equal(recordCount, await TargetRecordsAsync());

        DataScopeRuleDto Node(string rootId, string? parent, int level)
        {
            var id = rootId + "_" + level;
            return new(id, rootId + "_0", parent, "容量规则" + rootId + "_" + level, true, "part.part",
                new("attribute", "EID", "equal", JsonSerializer.SerializeToElement(anchor.Id)),
                [new(id + "Subject", "all", null, 0, new Dictionary<string, string> { ["read"] = level % 2 == 0 ? "deny" : "allow" })],
                level + 1 == depth ? [] : [Node(rootId, id, level + 1)]);
        }
        async Task<int> TargetRecordsAsync()
        {
            using var scope = fixture.Services.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>().Set<Dictionary<string, object?>>("BusinessChange")
                .CountAsync(row => EF.Property<string>(row, "EventType") == "dataScope.object.changed" && EF.Property<string>(row, "TargetId") == "part.part/" + anchor.Id);
        }
        async Task<JsonElement> MeasureAsync(string phase, Func<Task<JsonElement>> action)
        {
            commands.Begin(); connections.Begin(); transactions.Begin();
            var allocated = GC.GetTotalAllocatedBytes(false); var watch = Stopwatch.StartNew();
            JsonElement result;
            try { result = await action(); }
            finally { commands.End(); connections.End(); transactions.End(); }
            watch.Stop();
            var locks = commands.Items.Where(item => item.Sql.Contains("sp_getapplock", StringComparison.Ordinal)).ToArray();
            output.WriteLine(JsonSerializer.Serialize(new { phase, sampleSize = size, objects = size, directSubjects = size, directActions = size * 2,
                ruleRoots = rootCount, ruleDepth = depth, ruleNodes = rootCount * depth, matchingRoles = size + 1, historyRowsBeforeMeasurement = historyBefore,
                elapsedMilliseconds = watch.Elapsed.TotalMilliseconds, allocatedBytes = GC.GetTotalAllocatedBytes(false) - allocated,
                outputBytes = System.Text.Encoding.UTF8.GetByteCount(result.GetRawText()), sqlCommands = commands.Items.Count,
                sqlExecuteMilliseconds = commands.Items.Sum(item => item.ExecuteMilliseconds), sqlParameters = commands.Items.Sum(item => item.Parameters),
                maxCommandParameters = commands.Items.Count == 0 ? 0 : commands.Items.Max(item => item.Parameters),
                connectionOpens = connections.Items.Count(item => item.Operation == "open"), connectionCloses = connections.Items.Count(item => item.Operation == "close"),
                transaction = transactions.Result, lockHeldMilliseconds = locks.Length > 0 && transactions.Ended > 0
                    ? Stopwatch.GetElapsedTime(locks[0].CompletedTimestamp, transactions.Ended).TotalMilliseconds : (double?)null,
                commands = commands.Items.ToArray() }));
            return result;
        }
    }

    private sealed class CostTransactions : DbTransactionInterceptor
    {
        private bool active;
        internal long Ended { get; private set; }
        internal string? Result { get; private set; }
        internal void Begin() { Ended = 0; Result = null; active = true; }
        internal void End() => active = false;
        private void Record(string result) { if (active) { Ended = Stopwatch.GetTimestamp(); Result = result; } }
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData data, CancellationToken token = default)
        { Record("committed"); return Task.CompletedTask; }
        public override Task TransactionRolledBackAsync(DbTransaction transaction, TransactionEndEventData data, CancellationToken token = default)
        { Record("rolledBack"); return Task.CompletedTask; }
    }
}
