using System.Data;
using System.Data.Common;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ICP.Platform.Modules.Audit.Contracts;
using ICP.Platform.Modules.Authorization.DTOs;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static ICP.Platform.HostApi.Tests.EntityOwnershipBackfillSqlServerHttpApiTests;

namespace ICP.Platform.HostApi.Tests;

public sealed partial class DataScopePermissionCostSqlServerTests
{
    [EnvironmentGatedFact("NEWP_RUN_OBJECT_HISTORY_CAPACITY", "独占测量256履历样本的来源/筛选/全输出与真实结果读取取消，不设业务上限")]
    public Task History_Records256SafeQueryAndFullOutputCosts() => MeasureHistoryAsync(256);

    [EnvironmentGatedFact("NEWP_RUN_OBJECT_HISTORY_CAPACITY", "独占测量2048履历样本的SQL/连接/过程分配/输出和取消成本，不设业务上限")]
    public Task History_Records2048SafeQueryAndFullOutputCosts() => MeasureHistoryAsync(2048);

    [EnvironmentGatedFact("NEWP_RUN_OBJECT_HISTORY_CAPACITY", "实际超过SQL参数边界的全部/选中安全集合保持完整，外对象记录整批拒绝")]
    public Task History_MoreThan2100SelectedAndAllRecordsKeepExactMembership() => MeasureHistoryAsync(2112, verifyMembershipBoundary: true);

    private async Task MeasureHistoryAsync(int size, bool verifyMembershipBoundary = false)
    {
        var commands = new CostCommands(); var connections = new CostConnections(); var transactions = new CostTransactions();
        var cancellation = new HistoryPageCancellation();
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(services =>
            services.AddDbContext<EfPlatformDbContext>((_, options) => options.AddInterceptors(commands, connections, transactions, cancellation)), includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        await InitializeAsync(fixture, admin);
        var position = await ContainerAsync(fixture, admin);
        var anchor = await CreateAsync(fixture, admin, position, "Part", "parts", "createPart", "HistoryCapacity296");
        var unrelated = await CreateAsync(fixture, admin, position, "Part", "parts", "createPart", "HistoryUnrelatedCapacity296");
        var otherFolder = await CreateAsync(fixture, admin, position, "WorkspaceFolder", "folders", "create", "HistorySecondPath296");
        var secondary = position with { LinkIds = [.. position.LinkIds, otherFolder.LinkId] };
        using (var added = await FeatureHttpTestRequests.SendAsync(admin, Point(fixture, "workspace.folder.add"), HttpMethod.Post,
            "/api/workspace/content/references", new { position = secondary, target = new { modelKey = "Part", id = anchor.Id } })) await ReadOkAsync(added);
        var history = Point(fixture, "workspace.folder.history");
        var independentHistory = Point(fixture, "part.part.history");
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "HistoryCapacity296", "履历容量", "history-capacity296", "履历容量普通账号",
            Point(fixture, "workspace.spaces.access"), history, independentHistory);
        using var ordinary = actor.Client;
        var config = new DataScopeObjectDto("part.part", anchor.Id,
            [new("history-capacity-user", "user", actor.AccountId, 1, new Dictionary<string, string> { ["read"] = "allow", ["history"] = "allow" })]);
        using (var saved = await FeatureHttpTestRequests.SendAsync(admin, Point(fixture, "part.part.authorize"), HttpMethod.Put,
            "/api/part/parts/" + anchor.Id + "/permissions", config)) Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);
        var now = fixture.Services.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime;
        int totalRows;
        bool pooling;
        var matchedIds = new List<string>();
        string? unrelatedRecordId = null;
        string secondaryLinkId;
        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
            pooling = new SqlConnectionStringBuilder(db.Database.GetConnectionString()!).Pooling;
            var rows = db.Set<Dictionary<string, object?>>("BusinessChange");
            var prototype = await rows.AsNoTracking().SingleAsync(row => EF.Property<string>(row, "EventType") == "part.created" && EF.Property<string>(row, "TargetId") == anchor.Id);
            // 规模准备基于实际writer形状；不把数千次业务创建或目录发布成本计入查询/输出。
            for (var index = 0; index < size + size / 4; index++)
            {
                var occurred = now.AddSeconds(-index);
                var recordId = "BusinessChange_" + Guid.NewGuid().ToString("N");
                if (index < size) matchedIds.Add(recordId); else unrelatedRecordId ??= recordId;
                rows.Add(new(prototype) { ["EID"] = recordId, ["EventType"] = "part.updated",
                    ["TargetId"] = index < size ? anchor.Id : unrelated.Id, ["ActorId"] = actor.AccountId,
                    ["OccurredAt"] = occurred, ["CreatedAt"] = occurred, ["UpdatedAt"] = occurred, ["CreatedBy"] = actor.AccountId, ["UpdatedBy"] = actor.AccountId,
                    ["IpAddress"] = index % 2 == 0 ? "192.0.2.42" : null, ["TraceId"] = Guid.NewGuid().ToString("N") });
            }
            await db.SaveChangesAsync();
            Assert.Equal(size, await rows.CountAsync(row => EF.Property<string>(row, "EventType") == "part.updated" && EF.Property<string>(row, "TargetId") == anchor.Id));
            Assert.Equal(2, await db.Set<Dictionary<string, object?>>("WorkspaceFolderContent").CountAsync(row => EF.Property<string>(row, "CEID") == anchor.Id));
            secondaryLinkId = await db.Set<Dictionary<string, object?>>("WorkspaceFolderContent")
                .Where(row => EF.Property<string>(row, "PEID") == otherFolder.Id && EF.Property<string>(row, "CEID") == anchor.Id)
                .Select(row => EF.Property<string>(row, "LID")).SingleAsync();
            totalRows = await rows.CountAsync();
        }
        var scopeQuery = "?queryScope=" + Uri.EscapeDataString(JsonSerializer.Serialize(position, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var source = "/api/workspace/content/" + anchor.LinkId + "/history";
        var filter = new ObjectHistoryFilter(EventType: "part.updated");
        var query = scopeQuery + "&eventType=part.updated";
        if (verifyMembershipBoundary)
        {
            var responses = new List<(string Mode, HttpStatusCode Status, byte[] Bytes, int MaxParameters)>();
            foreach (var mode in new[] { "all", "selected" })
            {
                commands.Begin();
                try
                {
                    using var response = await FeatureHttpTestRequests.SendAsync(ordinary, history, HttpMethod.Post, source + "/print" + scopeQuery,
                        new ObjectHistoryOutputRequest(mode, filter, mode == "selected" ? matchedIds : null));
                    responses.Add((mode, response.StatusCode, await response.Content.ReadAsByteArrayAsync(), commands.Items.Max(item => item.Parameters)));
                }
                finally { commands.End(); }
            }
            // 两种模式均实际发到真实provider，再断言，首败不能掩盖另一模式的参数边界。
            foreach (var response in responses)
            {
                output.WriteLine(JsonSerializer.Serialize(new { boundaryMode = response.Mode, expectedRows = size, status = (int)response.Status,
                    maxSqlParameters = response.MaxParameters }));
            }
            foreach (var response in responses)
            {
                Assert.True(response.Status == HttpStatusCode.OK, response.Mode + ": " + System.Text.Encoding.UTF8.GetString(response.Bytes));
                var items = JsonSerializer.Deserialize<JsonElement>(response.Bytes).GetProperty("items").EnumerateArray().ToArray();
                Assert.Equal(size, items.Length);
                Assert.Equal(matchedIds, items.Select(item => item.GetProperty("id").GetString()!));
                Assert.True(response.MaxParameters < 2100);
            }
            using var forged = await FeatureHttpTestRequests.SendAsync(ordinary, history, HttpMethod.Post, source + "/print" + scopeQuery,
                new ObjectHistoryOutputRequest("selected", filter, [.. matchedIds.Take(size - 1), unrelatedRecordId!]));
            Assert.Equal(HttpStatusCode.NotFound, forged.StatusCode);
            // 保留原LID入口，真实移除第二条引用；消费另一处SubjectField事实的单集合参数翻译。
            using (var removed = await FeatureHttpTestRequests.SendAsync(admin, Point(fixture, "workspace.folder.remove"), HttpMethod.Delete,
                "/api/workspace/content/references/" + secondaryLinkId, new { position = secondary })) await ReadOkAsync(removed);
            var related = await FeatureHttpTestRequests.ReadAsync(ordinary, history, source + scopeQuery + "&eventType=workspace.reference.removed");
            Assert.Equal(1, related.GetProperty("total").GetInt32());
            var relation = Assert.Single(related.GetProperty("items").EnumerateArray());
            Assert.Equal("Part", relation.GetProperty("objectType").GetString());
            Assert.Equal(anchor.Name, relation.GetProperty("target").GetProperty("name").GetString());
            using (var scope = fixture.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
                var actual = await db.Set<Dictionary<string, object?>>("BusinessChange")
                    .Where(row => EF.Property<string>(row, "EventType") == "workspace.reference.removed" && EF.Property<string>(row, "TargetId") == secondaryLinkId)
                    .Select(row => EF.Property<string>(row, "EID")).SingleAsync();
                Assert.Equal(actual, relation.GetProperty("id").GetString());
            }
            Assert.DoesNotContain(secondaryLinkId, related.GetRawText(), StringComparison.Ordinal);
            return;
        }
        var folderPath = "/api/workspace/content/" + otherFolder.LinkId + "/history" + scopeQuery + "&eventType=workspace.folder.created";
        var folderFirst = await MeasureAsync("folder-first-single-event", () => ReadAsync(folderPath), 1);
        Assert.Equal(1, folderFirst.Page!.Value.GetProperty("total").GetInt32());
        var folderSteady = await MeasureAsync("folder-steady-single-event", () => ReadAsync(folderPath), 1);
        Assert.Equal(folderFirst.Page.Value.GetRawText(), folderSteady.Page!.Value.GetRawText());
        var first = await MeasureAsync("source-first-count-page", () => ReadAsync(source + query));
        Assert.Equal(size, first.Page!.Value.GetProperty("total").GetInt32());
        Assert.Equal(20, first.Page.Value.GetProperty("items").GetArrayLength());
        var steady = await MeasureAsync("source-steady-count-page", () => ReadAsync(source + query));
        Assert.Equal(first.Page.Value.GetRawText(), steady.Page!.Value.GetRawText());
        await MeasureAsync("independent-count-page", async () =>
        {
            var page = await FeatureHttpTestRequests.ReadAsync(ordinary, independentHistory, "/api/part/parts/" + anchor.Id + "/history?eventType=part.updated");
            Assert.Equal(size, page.GetProperty("total").GetInt32());
            return new(System.Text.Encoding.UTF8.GetByteCount(page.GetRawText()), page);
        });
        var filtered = await MeasureAsync("actor-type-visible-description-ip-filter", () => ReadAsync(source + query + "&actorId=" + actor.AccountId
            + "&objectType=Part&description=" + Uri.EscapeDataString("修改对象") + "&ipAddress=192.0.2.42"));
        Assert.Equal(size / 2, filtered.Page!.Value.GetProperty("total").GetInt32());
        var selectedIds = first.Page.Value.GetProperty("items").EnumerateArray().Take(2).Select(item => item.GetProperty("id").GetString()!).ToArray();
        var selected = await MeasureAsync("selected-xlsx", () => OutputAsync("export", new("selected", filter, selectedIds)), 2);
        AssertHistoryXlsxRows(selected.Content!, 2);
        var all = await MeasureAsync("all-xlsx", () => OutputAsync("export", new("all", filter)));
        AssertHistoryXlsxRows(all.Content!, size);
        var print = await MeasureAsync("all-print-data", () => OutputAsync("print", new("all", filter)));
        Assert.Equal(size, print.Page!.Value.GetProperty("items").GetArrayLength());
        using (var canceled = new CancellationTokenSource())
        {
            cancellation.Cancel = canceled;
            try
            {
                await MeasureAsync("cancel-after-real-history-page-reader", async () =>
                {
                    using var request = new HttpRequestMessage(HttpMethod.Post, source + "/print" + scopeQuery) { Content = JsonContent.Create(new ObjectHistoryOutputRequest("all", filter)) };
                    request.Headers.Add("X-NewP-Feature", history.Position);
                    await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => { using var response = await ordinary.SendAsync(request, canceled.Token); });
                    Assert.True(cancellation.Fired);
                    return new(0, null);
                });
            }
            finally { cancellation.Cancel = null; }
        }
        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
            Assert.Equal(totalRows, await db.Set<Dictionary<string, object?>>("BusinessChange").CountAsync());
            Assert.Equal(size, await db.Set<Dictionary<string, object?>>("BusinessChange").CountAsync(row => EF.Property<string>(row, "EventType") == "part.updated" && EF.Property<string>(row, "TargetId") == anchor.Id));
        }

        // 同一查询单元只持有短只读连接；调用者已持有连接时不夺取其生命周期。
        using (var scope = fixture.Services.CreateScope())
        {
            var provider = scope.ServiceProvider;
            var db = provider.GetRequiredService<EfPlatformDbContext>();
            var accessor = provider.GetRequiredService<IHttpContextAccessor>();
            var previous = accessor.HttpContext;
            accessor.HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, actor.AccountId)], "test-trusted-account")) };
            try
            {
                var historyQuery = provider.GetRequiredService<IObjectHistoryQuery>();
                Assert.Equal(ConnectionState.Closed, db.Database.GetDbConnection().State);
                connections.Begin();
                try { Assert.NotNull(await historyQuery.ListAsync("part.part", anchor.Id, filter)); }
                finally { connections.End(); }
                AssertOwnedConnection();
                await db.Database.OpenConnectionAsync();
                try
                {
                    connections.Begin();
                    try { Assert.NotNull(await historyQuery.ListAsync("part.part", anchor.Id, filter)); }
                    finally { connections.End(); }
                    Assert.Empty(connections.Items);
                    Assert.Equal(ConnectionState.Open, db.Database.GetDbConnection().State);
                }
                finally { await db.Database.CloseConnectionAsync(); }
                connections.Begin();
                try { await Assert.ThrowsAsync<ArgumentException>(() => historyQuery.ListAsync("part.part", anchor.Id, new(PageIndex: 0))); }
                finally { connections.End(); }
                AssertOwnedConnection();
                using var canceled = new CancellationTokenSource();
                commands.Begin(); connections.Begin(); commands.CancelOnCommand = canceled;
                try { await Assert.ThrowsAnyAsync<OperationCanceledException>(() => historyQuery.ListAsync("part.part", anchor.Id, filter, canceled.Token)); }
                finally { commands.CancelOnCommand = null; commands.End(); connections.End(); }
                AssertOwnedConnection();
                output.WriteLine("historyConnectionOwnership: success/invalid-filter/real-command-cancel self-opened=1/closed=1; caller-opened preserved");
            }
            finally { accessor.HttpContext = previous; }
            void AssertOwnedConnection()
            {
                Assert.Equal(1, connections.Items.Count(item => item.Operation == "open"));
                Assert.Equal(1, connections.Items.Count(item => item.Operation == "close"));
                Assert.Equal(ConnectionState.Closed, db.Database.GetDbConnection().State);
            }
        }

        async Task<HistoryMeasurement> ReadAsync(string path)
        {
            var page = await FeatureHttpTestRequests.ReadAsync(ordinary, history, path);
            return new(System.Text.Encoding.UTF8.GetByteCount(page.GetRawText()), page);
        }
        async Task<HistoryMeasurement> OutputAsync(string kind, ObjectHistoryOutputRequest request)
        {
            using var response = await FeatureHttpTestRequests.SendAsync(ordinary, history, HttpMethod.Post, source + "/" + kind + scopeQuery, request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var bytes = await response.Content.ReadAsByteArrayAsync();
            if (kind == "print")
            {
                var page = JsonSerializer.Deserialize<JsonElement>(bytes);
                return new(bytes.Length, page);
            }
            return new(bytes.Length, null, bytes);
        }
        // 内容语义在阶段计时结束后校验，不把XML解析/断言成本算入HTTP输出测量。
        static void AssertHistoryXlsxRows(byte[] bytes, int expectedRows)
        {
            using var archive = new System.IO.Compression.ZipArchive(new MemoryStream(bytes));
            using var xmlStream = archive.GetEntry("xl/worksheets/sheet1.xml")!.Open();
            var xml = System.Xml.Linq.XDocument.Load(xmlStream);
            Assert.Equal(expectedRows + 1, xml.Descendants(System.Xml.Linq.XName.Get("row", "http://schemas.openxmlformats.org/spreadsheetml/2006/main")).Count());
        }
        async Task<HistoryMeasurement> MeasureAsync(string phase, Func<Task<HistoryMeasurement>> action, int? measuredExpectedRows = null)
        {
            commands.Begin(); connections.Begin(); transactions.Begin();
            var allocated = GC.GetTotalAllocatedBytes(false); var watch = Stopwatch.StartNew();
            HistoryMeasurement result;
            try { result = await action(); }
            finally { commands.End(); connections.End(); transactions.End(); }
            watch.Stop();
            output.WriteLine(JsonSerializer.Serialize(new { historyPhase = phase, historyRows = size, measuredExpectedRows = measuredExpectedRows ?? size, unrelatedHistoryRows = size / 4,
                totalHistoryRows = totalRows, objects = 2, graphDepth = 2, targetOccurrences = 2, directSubjects = 1, rules = 0, ordinaryAccount = true,
                pooling,
                elapsedMilliseconds = watch.Elapsed.TotalMilliseconds, allocatedBytes = GC.GetTotalAllocatedBytes(false) - allocated,
                outputBytes = result.Bytes, sqlCommands = commands.Items.Count, sqlExecuteMilliseconds = commands.Items.Sum(item => item.ExecuteMilliseconds),
                sqlParameters = commands.Items.Sum(item => item.Parameters), connectionOpens = connections.Items.Count(item => item.Operation == "open"),
                connectionCloses = connections.Items.Count(item => item.Operation == "close"),
                connectionOpenMilliseconds = connections.Items.Where(item => item.Operation == "open").Sum(item => item.Milliseconds),
                connectionCloseMilliseconds = connections.Items.Where(item => item.Operation == "close").Sum(item => item.Milliseconds),
                cancellationToReceiptMilliseconds = cancellation.CanceledAt == 0 ? (double?)null : Stopwatch.GetElapsedTime(cancellation.CanceledAt).TotalMilliseconds,
                explicitAppLocks = commands.Items.Count(item => item.Sql.Contains("sp_getapplock", StringComparison.Ordinal)), transaction = transactions.Result,
                commands = commands.Items.ToArray() }));
            return result;
        }
    }

    private sealed record HistoryMeasurement(int Bytes, JsonElement? Page, byte[]? Content = null);
    private sealed class HistoryPageCancellation : DbCommandInterceptor
    {
        internal CancellationTokenSource? Cancel { get; set; }
        internal bool Fired { get; private set; }
        internal long CanceledAt { get; private set; }
        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData data, DbDataReader result,
            CancellationToken token = default)
        {
            if (Cancel is { } source && command.CommandText.Contains("[BusinessChange]", StringComparison.Ordinal)
                && command.CommandText.Contains("ORDER BY", StringComparison.Ordinal) && command.CommandText.Contains("[OccurredAt]", StringComparison.Ordinal))
            {
                Fired = true; CanceledAt = Stopwatch.GetTimestamp(); source.Cancel();
                await result.DisposeAsync();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            }
            return result;
        }
    }
}
