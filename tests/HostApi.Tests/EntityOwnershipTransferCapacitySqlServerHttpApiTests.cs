using System.Collections.Concurrent;
using System.Data.Common;
using System.Diagnostics;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;
using static ICP.Platform.HostApi.Tests.EntityOwnershipBackfillSqlServerHttpApiTests;
using static ICP.Platform.HostApi.Tests.EntityOwnershipTransferSqlServerHttpApiTests;

namespace ICP.Platform.HostApi.Tests;

public sealed class EntityOwnershipTransferCapacitySqlServerHttpApiTests(ITestOutputHelper output)
{
    [Fact]
    public void CostObserver_RecognizesTheActualGraphSql_AndDoesNotMisclassifyAclOrOtherCommands()
    {
        var repository = new DirectoryInfo(AppContext.BaseDirectory);
        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "AGENTS.md"))) repository = repository.Parent;
        Assert.NotNull(repository);
        var line = Assert.Single(File.ReadAllLines(Path.Combine(repository.FullName, "server", "modules", "Infor-Center", "Workspace", "WorkspaceContents.cs")),
            value => value.Contains("sp_getapplock", StringComparison.OrdinalIgnoreCase) && value.Contains("N'Workspace.FolderGraph'", StringComparison.Ordinal));
        var sql = line[(line.IndexOf('"') + 1)..line.LastIndexOf('"')];
        using var connection = new SqlConnection("Data Source=(local);Initial Catalog=TransferObservationOnly");
        foreach (var (text, resource, expected) in new (string, string?, bool)[] {
            (sql, null, true),
            ("EXEC sp_getapplock @Resource=@resource;", "Workspace.FolderGraph", true),
            ("EXEC sp_getapplock @Resource=@resource;", "Authorization.DataScopeObject:part.part:example", false),
            ("SELECT 1;", null, false) })
        {
            using var observation = new TransferCostObservation { Database = connection.Database };
            using var command = connection.CreateCommand(); command.CommandText = text;
            if (resource is not null) command.Parameters.AddWithValue("@resource", resource);
            observation.Begin();
            observation.OnNext(new KeyValuePair<string, object?>("Microsoft.Data.SqlClient.WriteCommandBefore", new { Command = command }));
            observation.OnNext(new KeyValuePair<string, object?>("Microsoft.Data.SqlClient.WriteCommandAfter", new { Command = command }));
            observation.End();
            Assert.Equal(1, observation.CommandCount);
            Assert.Equal(expected, observation.ObservedGraph);
        }
    }

    [EnvironmentGatedFact("NEWP_RUN_OWNERSHIP_TRANSFER_CAPACITY", "独占测量三模型3与103对象跨100分片的真实转交、接收、通知及末尾真实Flush后取消，不设业务上限")]
    public async Task ThreeModels_Transfer3And103RecordsCosts_AndLateCancellationRollsBackTheWholeLargeBatch()
    {
        using var observation = new TransferCostObservation();
        var commands = new EfCommands(observation);
        var cancellationProbe = new EntityOwnershipTransferTransactionSqlServerHttpApiTests.NotificationWriteProbe();
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(services =>
            services.AddDbContext<EfPlatformDbContext>((_, options) => options.AddInterceptors(observation, commands, cancellationProbe)), includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        await InitializeAsync(fixture, admin);
        var position = await ContainerAsync(fixture, admin);
        var read = Point(fixture, "workspace.spaces.access"); var transfer = Point(fixture, "workspace.folder.transfer");
        var owner = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "CapacityTransferOwner", "容量转交人",
            "capacity-transfer-owner", "容量转交人", read, transfer, Point(fixture, "workspace.folder.create"),
            Point(fixture, "workspace.folder.createPart"), Point(fixture, "workspace.folder.createDocument"));
        using var ownerClient = owner.Client;
        using (var setup = fixture.Services.CreateScope())
            observation.Database = setup.ServiceProvider.GetRequiredService<EfPlatformDbContext>().Database.GetDbConnection().Database;
        foreach (var size in new[] { 3, 103 })
        {
            var recipient = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "CapacityTransferRecipient" + size, "容量接收人",
                "capacity-transfer-recipient-" + size, "容量接收人", read, transfer);
            using var recipientClient = recipient.Client;
            // 正式API准备原对象；准备耗时及空间/角色建立不计入下方转交请求窗口。
            var targets = new List<Target>();
            foreach (var (model, kind, action) in Kinds)
                targets.Add(await CreateAsync(fixture, ownerClient, position, model, kind, action, "TRANSFER-COST-" + size + "-" + model));
            for (var index = 3; index < size; index++)
                targets.Add(await CreateAsync(fixture, ownerClient, position, "Part", "parts", "createPart", "TRANSFER-COST-" + size + "-P" + index));
            var input = new { position, linkIds = targets.Select(target => target.LinkId).ToArray(), recipientId = recipient.AccountId };
            var authorizationRows = new Dictionary<string, long>(); long previousHistory;
            using (var setup = fixture.Services.CreateScope())
            {
                var db = setup.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
                previousHistory = await db.Set<Dictionary<string, object?>>("BusinessChange").LongCountAsync();
                foreach (var model in db.PersistencePlan.Models.Where(model => model.OwnerModuleId == "Authorization"))
                    authorizationRows[model.LogicalKey] = await db.Set<Dictionary<string, object?>>(model.LogicalKey).LongCountAsync();
            }
            using var process = Process.GetCurrentProcess(); process.Refresh();
            var workingSet = process.WorkingSet64; var allocated = GC.GetTotalAllocatedBytes();
            commands.Reset(); observation.Begin();
            using var response = await FeatureHttpTestRequests.SendAsync(ownerClient, transfer, HttpMethod.Post, "/api/workspace/ownership/transfer", input);
            var receipt = await ReadOkAsync(response);
            var cost = observation.End(); process.Refresh();
            output.WriteLine(JsonSerializer.Serialize(new { scenario = "ordinary transfer", objects = size, folders = 1, documents = 1, parts = size - 2,
                depth = position.LinkIds.Length, authorizationRows, previousHistory, requestBytes = Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(input)),
                responseBytes = Encoding.UTF8.GetByteCount(receipt.GetRawText()), allocatedBytes = GC.GetTotalAllocatedBytes() - allocated,
                workingSetBefore = workingSet, workingSetAfter = process.WorkingSet64, peakWorkingSet = process.PeakWorkingSet64,
                efReaders = commands.Readers, efNonQueries = commands.NonQueries, efScalars = commands.Scalars, cost }));
            Assert.Equal(size, receipt.GetProperty("changed").GetInt32());
            Assert.True(observation.CommandCount > 0, "No actual SqlClient command was observed.");
            Assert.True(observation.ObservedGraph, "The actual Workspace graph lock command was not observed.");
            Assert.Equal("committed", observation.Outcome);
            using (var verification = fixture.Services.CreateScope())
            {
                var db = verification.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
                Assert.Equal(size, await db.Set<Dictionary<string, object?>>("WorkspaceTransferNotification").CountAsync(row => EF.Property<string>(row, "RecipientId") == recipient.AccountId));
                foreach (var group in targets.GroupBy(target => target.Model))
                {
                    var ids = group.Select(target => target.Id).ToArray();
                    Assert.Equal(ids.Length, await db.Set<Dictionary<string, object?>>(group.Key).CountAsync(row => ids.Contains(EF.Property<string>(row, "EID")) && EF.Property<string>(row, "OwnerId") == recipient.AccountId));
                }
            }
            if (size != 103) continue;
            var snapshotModels = new[] { "WorkspaceSpace", "WorkspaceFolder", "WorkspaceFolderContent", "Part", "Document", "BusinessChange", "WorkspaceTransferNotification" };
            var beforeCancel = await SnapshotAsync(fixture, snapshotModels);
            using var cancellation = new CancellationTokenSource();
            cancellationProbe.Arm("cancel", cancellation);
            commands.Reset(); observation.Begin();
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, "/api/workspace/ownership/transfer")
                { Content = JsonContent.Create(new { position, linkIds = input.linkIds, recipientId = owner.AccountId }) };
                request.Headers.Add("X-NewP-Feature", transfer.Position);
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => recipientClient.SendAsync(request, cancellation.Token));
                Assert.True(cancellationProbe.NotificationSqlCompleted); Assert.Equal(size, cancellationProbe.SavedNotifications);
            }
            finally { cancellationProbe.Disarm(); }
            var cancelCost = observation.End();
            Assert.Equal("rolledBack", observation.Outcome);
            Assert.Equal(beforeCancel, await SnapshotAsync(fixture, snapshotModels));
            output.WriteLine(JsonSerializer.Serialize(new { scenario = "cancel after actual notification SQL", objects = size,
                notificationRowsObserved = cancellationProbe.SavedNotifications, efReaders = commands.Readers, efNonQueries = commands.NonQueries,
                cancellation = "requested after SavedChanges verified every notification in the same transaction", cost = cancelCost }));
        }
    }

    // 与既有归属成本资产相同的SqlClient诊断入口，覆盖EF及原生图/权限SQL；不输出SQL参数或连接凭据。
    private sealed class TransferCostObservation : DbTransactionInterceptor, IObserver<DiagnosticListener>, IObserver<KeyValuePair<string, object?>>, IDisposable
    {
        private readonly List<IDisposable> subscriptions = [];
        private readonly ConcurrentQueue<string> commands = new();
        private readonly Stopwatch clock = new();
        private readonly IDisposable all;
        private double? graphRequested; private double? graphAcquired; private double? ended;
        internal string Database { get; set; } = "";
        internal bool Active { get; private set; }
        internal string? Outcome { get; private set; }
        internal int CommandCount => commands.Count;
        internal bool ObservedGraph => graphAcquired is not null;
        internal TransferCostObservation() => all = DiagnosticListener.AllListeners.Subscribe(this);
        internal void Begin() { commands.Clear(); graphRequested = null; graphAcquired = null; ended = null; Outcome = null; clock.Restart(); Active = true; }
        internal object End()
        {
            Active = false; clock.Stop();
            return new { elapsedMs = clock.Elapsed.TotalMilliseconds, sqlCommands = commands.Count,
                selectionReadChunk = 100, graphAcquireCommandMs = graphRequested is { } requested && graphAcquired is { } acquired ? acquired - requested : (double?)null,
                graphRequestToTransactionEndMs = graphRequested is { } start && ended is { } end ? end - start : (double?)null,
                transactionOutcome = Outcome, lockObservation = "uncontended; acquire includes command/network time, hold interval ends at commit or rollback" };
        }
        public void OnNext(DiagnosticListener listener) { if (listener.Name == "SqlClientDiagnosticListener") subscriptions.Add(listener.Subscribe(this)); }
        public void OnNext(KeyValuePair<string, object?> value)
        {
            if (!Active || value.Value?.GetType().GetProperty("Command")?.GetValue(value.Value) is not DbCommand command || command.Connection?.Database != Database) return;
            if (value.Key.EndsWith("WriteCommandAfter", StringComparison.Ordinal)) commands.Enqueue(command.CommandText);
            if (!command.CommandText.Contains("sp_getapplock", StringComparison.OrdinalIgnoreCase)) return;
            var graph = command.Parameters.Cast<DbParameter>().Any(parameter => Equals(parameter.Value, "Workspace.FolderGraph"))
                || Regex.IsMatch(command.CommandText, @"@Resource\s*=\s*N?'Workspace\.FolderGraph'", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (!graph) return;
            if (value.Key.EndsWith("WriteCommandBefore", StringComparison.Ordinal)) graphRequested ??= clock.Elapsed.TotalMilliseconds;
            if (value.Key.EndsWith("WriteCommandAfter", StringComparison.Ordinal)) graphAcquired ??= clock.Elapsed.TotalMilliseconds;
        }
        private void Finish(string outcome) { if (Active) { ended = clock.Elapsed.TotalMilliseconds; Outcome = outcome; } }
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default) { Finish("committed"); return Task.CompletedTask; }
        public override Task TransactionRolledBackAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default) { Finish("rolledBack"); return Task.CompletedTask; }
        public void OnCompleted() { }
        public void OnError(Exception error) { }
        public void Dispose() { Active = false; foreach (var subscription in subscriptions) subscription.Dispose(); all.Dispose(); }
    }

    // EF分类仅补充总SQL计数，原生读取已由上方SqlClient入口统一计入。
    private sealed class EfCommands(TransferCostObservation observation) : DbCommandInterceptor
    {
        internal int Readers { get; private set; } internal int NonQueries { get; private set; } internal int Scalars { get; private set; }
        internal void Reset() { Readers = 0; NonQueries = 0; Scalars = 0; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        { if (observation.Active) Readers++; return ValueTask.FromResult(result); }
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        { if (observation.Active) NonQueries++; return ValueTask.FromResult(result); }
        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<object> result, CancellationToken cancellationToken = default)
        { if (observation.Active) Scalars++; return ValueTask.FromResult(result); }
    }
}
