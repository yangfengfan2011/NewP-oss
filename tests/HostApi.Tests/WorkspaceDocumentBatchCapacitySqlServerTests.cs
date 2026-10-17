using System.Collections;
using System.Collections.Concurrent;
using System.Data.Common;
using System.Diagnostics;
using System.Text.Json;
using ICP.Platform.Modules.Files.Storage;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using DocFiles = ICP.Platform.HostApi.Tests.DocumentFilesSqlServerHttpApiTests;

namespace ICP.Platform.HostApi.Tests;

public sealed partial class WorkspaceClipboardSqlServerHttpApiTests
{
    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_CAPACITY", "20/50文档真实文件流与SQL往返/逐事务锁区间，双请求与整批取消实际回收成本，无业务硬阈值")]
    public async Task ObjectCopy_DocumentBatchMeasuresTwentyFiftyConcurrencyAndReclamation()
    {
        var costs = new BatchSqlCosts(); var clock = new BatchLockClock(); var transactions = new BatchTransactionClock(clock); var io = new CopyIo();
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(services => {
            services.AddDbContext<EfPlatformDbContext>((_, options) => options.AddInterceptors(costs, clock, transactions));
            services.AddSingleton<IFileContentStore>(provider => { io.Inner = provider.GetRequiredService<LocalStorageProvider>(); return io; });
        }, includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync(); var root = await Root(fixture, admin);
        var source = await Folder(fixture, admin, root, "文件规模来源"); var target = await Folder(fixture, admin, root, "文件规模目标");
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "BatchCostActor", "批次成本主体", "batch-cost-actor", "批次成本账号",
            Point(fixture, "workspace.spaces.access"), Point(fixture, "workspace.folder.pasteObjectCopy"), Point(fixture, "workspace.folder.resolveObjectCopy"));
        using var ordinary = actor.Client;
        var artifactRoot = Environment.GetEnvironmentVariable("NEWP_COPY_COST_ARTIFACT_ROOT") ?? Path.Combine(Path.GetTempPath(), "newp-document-copy-cost-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(artifactRoot);
        var sourceLinks = new List<string>();
        foreach (var count in new[] { 20, 50 })
        {
            for (var index = sourceLinks.Count; index < count; index++)
            {
                var values = new { ECODE = "COST-SOURCE-" + index, ENAME = "实测文档 " + index }; var uploads = new string[2];
                for (var file = 0; file < 2; file++)
                {
                    var bytes = new byte[file == 0 ? 1024 * 1024 : 256 * 1024]; Array.Fill(bytes, (byte)(index + 1));
                    uploads[file] = (await DocFiles.StageAsync(fixture, admin, source, values, bytes, $"cost-{index}-{file}.pdf")).GetProperty("uploadId").GetString()!;
                }
                await Command(fixture, admin, "workspace.folder.createDocument", "/api/workspace/content/documents", new { position = source, values, uploads = new { mainUploadId = uploads[0], attachmentUploadIds = uploads[1..] } });
                var document = Assert.Single(await Rows(fixture, "Document"), row => Equals(row["ECODE"], values.ECODE));
                sourceLinks.Add((string)Assert.Single(await Rows(fixture, "WorkspaceFolderContent"), row => Equals(row["CEID"], document["EID"]))["LID"]!);
            }
            var prepared = await PrepareDocumentBatch(fixture, ordinary, source, target, sourceLinks.ToArray());
            Assert.Equal(count * 2, prepared.GetProperty("fileCount").GetInt32());
            await Measure("documents-" + count, count, 1, async () => {
                var result = await Command(fixture, ordinary, "workspace.folder.pasteObjectCopy", "/api/workspace/content/objects/copy", new { operationId = prepared.GetProperty("operationId").GetString(), values = BatchValues(sourceLinks, "COST-" + count) });
                Assert.Equal(count, result.GetProperty("items").GetArrayLength());
            });
        }
        var selection = sourceLinks.Take(2).ToArray(); var reversed = selection.Reverse().ToArray();
        var first = await PrepareDocumentBatch(fixture, ordinary, source, target, selection); var second = await PrepareDocumentBatch(fixture, ordinary, source, target, reversed);
        await Measure("two-requests-reverse-sources", 2, 2, async () => {
            var responses = await Task.WhenAll(
                Command(fixture, ordinary, "workspace.folder.pasteObjectCopy", "/api/workspace/content/objects/copy", new { operationId = first.GetProperty("operationId").GetString(), values = BatchValues(selection, "CONCURRENT-A") }),
                Command(fixture, ordinary, "workspace.folder.pasteObjectCopy", "/api/workspace/content/objects/copy", new { operationId = second.GetProperty("operationId").GetString(), values = BatchValues(reversed, "CONCURRENT-B") }));
            Assert.All(responses, response => Assert.True(response.GetProperty("succeeded").GetBoolean()));
        });
        var cancelling = await PrepareDocumentBatch(fixture, ordinary, source, target, sourceLinks.Take(20).ToArray()); var cancellingId = cancelling.GetProperty("operationId").GetString();
        io.Arm("none", 0); io.PauseAtWrite = 3;
        var pending = Send(fixture, ordinary, "workspace.folder.pasteObjectCopy", "objects/copy", new { operationId = cancellingId, values = BatchValues(sourceLinks.Take(20).ToArray(), "CANCEL-COST") });
        try
        {
            await io.Reached.Task.WaitAsync(TimeSpan.FromSeconds(60));
            var storageBeforeCancel = Directory.EnumerateFiles(fixture.StorageRoot).Sum(path => new FileInfo(path).Length);
            await Measure("cancel-twenty-partially-prepared", 20, 1, async () => {
                var cancelled = await Command(fixture, ordinary, "workspace.folder.pasteObjectCopy", "/api/workspace/content/objects/copy/cancel", new { operationId = cancellingId });
                Assert.Equal("cancelled", cancelled.GetProperty("state").GetString());
                using var response = await pending; Assert.False(response.IsSuccessStatusCode);
            });
            await Measure("reclaim-cancelled-twenty", 20, 1, () => CleanDocumentCopies(fixture));
            var storageAfterCancel = Directory.EnumerateFiles(fixture.StorageRoot).Sum(path => new FileInfo(path).Length);
            Assert.True(storageAfterCancel < storageBeforeCancel);
            Assert.DoesNotContain(await Rows(fixture, "FileCopyLease"), row => ((string)row["RequestKey"]!).StartsWith(cancellingId + ".", StringComparison.Ordinal)
                && (Equals(row["State"], "prepared") || Equals(row["State"], "confirmed")));
        }
        finally { io.Release.TrySetResult(); (await pending).Dispose(); }
        output.WriteLine("DOCUMENT_COPY_CAPACITY_ARTIFACT_ROOT " + artifactRoot);

        async Task Measure(string scenario, int documents, int requestConcurrency, Func<Task> action)
        {
            var samples = new List<object>(); var sqlWaitSamples = new List<object>(); using var stopping = new CancellationTokenSource(); using var process = Process.GetCurrentProcess();
            string observerConnectionString;
            using (var scope = fixture.Services.CreateScope()) observerConnectionString = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>().Database.GetConnectionString()!;
            await using var observer = new SqlConnection(new SqlConnectionStringBuilder(observerConnectionString) { InitialCatalog = "master", Pooling = false, ApplicationName = "NewP.CopyCost.ReadOnly" }.ConnectionString);
            await observer.OpenAsync();
            var baselineFileCount = Directory.EnumerateFiles(fixture.StorageRoot).Count();
            var baselineBytes = Directory.EnumerateFiles(fixture.StorageRoot).Sum(path => new FileInfo(path).Length);
            var monitor = Task.Run(async () => {
                var tick = 0;
                while (!stopping.IsCancellationRequested)
                {
                    process.Refresh(); long disk = 0, partial = 0;
                    foreach (var path in Directory.EnumerateFiles(fixture.StorageRoot))
                        try { var length = new FileInfo(path).Length; disk += length; if (path.EndsWith(".part", StringComparison.Ordinal)) partial += length; } catch (FileNotFoundException) { }
                    samples.Add(new { at = DateTime.UtcNow, process.PrivateMemorySize64, process.WorkingSet64, diskBytes = disk, partialBytes = partial });
                    if (++tick % 5 == 0)
                    {
                        var waiting = clock.Transactions.Values.Where(row => row.GraphStarted > 0 && row.GraphAcquired == 0 && row.Ended == 0 && row.SessionId > 0).Select(row => row.SessionId).Distinct().ToArray();
                        if (waiting.Length > 0)
                        {
                            await using var query = observer.CreateCommand();
                            query.CommandText = "SELECT session_id,wait_type,wait_time,blocking_session_id,wait_resource FROM sys.dm_exec_requests WHERE session_id IN (" + string.Join(",", waiting) + ");";
                            try
                            {
                                await using var reader = await query.ExecuteReaderAsync(stopping.Token);
                                while (await reader.ReadAsync(stopping.Token)) sqlWaitSamples.Add(new { at = DateTime.UtcNow, sessionId = reader.GetInt16(0),
                                    waitType = reader.IsDBNull(1) ? null : reader.GetString(1), waitTimeMs = reader.GetInt32(2), blockingSessionId = reader.GetInt16(3), waitResource = reader.GetString(4) });
                            }
                            catch (OperationCanceledException) when (stopping.IsCancellationRequested) { }
                        }
                    }
                    try { await Task.Delay(20, stopping.Token); } catch (OperationCanceledException) when (stopping.IsCancellationRequested) { }
                }
            });
            costs.Begin(); clock.Begin(); var watch = Stopwatch.StartNew();
            Exception? failure = null;
            try { await action(); }
            catch (Exception exception) { failure = exception; throw; }
            finally
            {
                watch.Stop(); costs.Active = false; clock.Active = false; stopping.Cancel(); await monitor;
                var sql = costs.End();
                var finalFiles = Directory.EnumerateFiles(fixture.StorageRoot).Select(path => new FileInfo(path)).ToArray();
                var finalBytes = finalFiles.Sum(file => file.Length);
                var evidence = new { scenario, documents, requestConcurrency, filesPerDocument = 2, singleLengths = new[] { 1048576, 262144 }, totalBytes = documents * 1310720L * requestConcurrency,
                    elapsedMs = watch.Elapsed.TotalMilliseconds, bytesPerSecond = failure is null && !scenario.StartsWith("cancel", StringComparison.Ordinal) && !scenario.StartsWith("reclaim", StringComparison.Ordinal) ? (double?)(documents * 1310720L * requestConcurrency / watch.Elapsed.TotalSeconds) : null,
                    succeeded = failure is null, failureType = failure?.GetType().Name, baselineFileCount, baselineBytes, finalFileCount = finalFiles.Length, finalBytes, measuredByteDelta = finalBytes - baselineBytes, samples, sqlWaitSamples, sql, transactions = clock.Snapshot(), environment = new { runtime = Environment.Version.ToString(), processors = Environment.ProcessorCount,
                        os = Environment.OSVersion.ToString(), processId = Environment.ProcessId, pooling = false, clientTimeoutMs = ordinary.Timeout.TotalMilliseconds, host = "real HTTP TestServer / real configured SQL Server / LocalStorageProvider / Release" },
                    measurement = "Copy excludes source uploads/prepare/readback; process memory includes test host/client, not isolated published Host. Graph acquire elapsed includes SQL/network; hold interval is acquire-return through transaction-return, not engine-only ticks. Own graph-session DMV snapshots target100ms and are outside product SQL statistics; observed wait_time is not cumulative engine wait. Cancel/reclaim bytes describe selected input, not transferred throughput." };
                await File.WriteAllTextAsync(Path.Combine(artifactRoot, scenario + ".json"), JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }));
                output.WriteLine("DOCUMENT_COPY_CAPACITY " + JsonSerializer.Serialize(new { scenario, documents, watch.Elapsed.TotalMilliseconds, sql }));
            }
        }
    }

    // 复用既有SqlClient statistics机制；每个实际连接分别重置，独立短事务的往返不会漏计或当作SQL语句数。
    private sealed class BatchSqlCosts : DbCommandInterceptor
    {
        internal volatile bool Active; private readonly object gate = new(); private readonly HashSet<SqlConnection> connections = []; private long commands;
        internal void Begin() { lock (gate) { connections.Clear(); commands = 0; Active = true; } }
        private void Observe(DbCommand command)
        {
            if (!Active) return;
            lock (gate) { commands++; if (command.Connection is SqlConnection connection && connections.Add(connection)) { connection.StatisticsEnabled = true; connection.ResetStatistics(); } }
        }
        internal object End()
        {
            lock (gate)
            {
                var raw = connections.Select(connection => connection.RetrieveStatistics().Cast<DictionaryEntry>().ToDictionary(entry => (string)entry.Key, entry => Convert.ToInt64(entry.Value))).ToArray();
                return new { observedEfCommands = commands, serverRoundtrips = raw.Sum(row => row.GetValueOrDefault("ServerRoundtrips")), bytesSent = raw.Sum(row => row.GetValueOrDefault("BytesSent")), bytesReceived = raw.Sum(row => row.GetValueOrDefault("BytesReceived")), connections = raw };
            }
        }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData data, InterceptionResult<DbDataReader> result, CancellationToken token = default) { Observe(command); return ValueTask.FromResult(result); }
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData data, InterceptionResult<int> result, CancellationToken token = default) { Observe(command); return ValueTask.FromResult(result); }
        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command, CommandEventData data, InterceptionResult<object> result, CancellationToken token = default) { Observe(command); return ValueTask.FromResult(result); }
    }
    private sealed class BatchLockClock : DbCommandInterceptor
    {
        internal volatile bool Active;
        internal readonly ConcurrentDictionary<DbTransaction, BatchTransactionTimes> Transactions = new();
        internal void Begin() { Transactions.Clear(); Active = true; }
        internal object Snapshot() => Transactions.Values.Select(row => new { row.Started, row.GraphStarted, row.GraphAcquired, row.Ended, row.Outcome, row.SessionId,
            transactionMs = row.Ended > 0 ? Stopwatch.GetElapsedTime(row.Started, row.Ended).TotalMilliseconds : (double?)null,
            graphAcquireMs = row.GraphAcquired > 0 ? Stopwatch.GetElapsedTime(row.GraphStarted, row.GraphAcquired).TotalMilliseconds : (double?)null,
            graphHoldObservedMs = row.Ended > 0 && row.GraphAcquired > 0 ? Stopwatch.GetElapsedTime(row.GraphAcquired, row.Ended).TotalMilliseconds : (double?)null }).ToArray();
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData data, InterceptionResult<int> result, CancellationToken token = default)
        { if (Active && command.Transaction is { } transaction && command.CommandText.Contains("Workspace.FolderGraph", StringComparison.Ordinal))  { var sample = Transactions.GetOrAdd(transaction, _ => new()); sample.GraphStarted = Stopwatch.GetTimestamp(); sample.SessionId = (command.Connection as SqlConnection)?.ServerProcessId ?? 0; } return ValueTask.FromResult(result); }
        public override ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData data, int result, CancellationToken token = default)
        { if (Active && command.Transaction is { } transaction && command.CommandText.Contains("Workspace.FolderGraph", StringComparison.Ordinal)) Transactions.GetOrAdd(transaction, _ => new()).GraphAcquired = Stopwatch.GetTimestamp(); return ValueTask.FromResult(result); }
    }
    private sealed class BatchTransactionTimes { internal int SessionId; internal long Started = Stopwatch.GetTimestamp(); internal long GraphStarted; internal long GraphAcquired; internal long Ended; internal string? Outcome; }
    private sealed class BatchTransactionClock(BatchLockClock clock) : DbTransactionInterceptor
    {
        public override ValueTask<DbTransaction> TransactionStartedAsync(DbConnection connection, TransactionEndEventData data, DbTransaction result, CancellationToken token = default)
        { if (clock.Active) clock.Transactions.TryAdd(result, new()); return ValueTask.FromResult(result); }
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData data, CancellationToken token = default) { End(transaction, "committed"); return Task.CompletedTask; }
        public override Task TransactionRolledBackAsync(DbTransaction transaction, TransactionEndEventData data, CancellationToken token = default) { End(transaction, "rolledBack"); return Task.CompletedTask; }
        private void End(DbTransaction transaction, string outcome) { if (clock.Active && clock.Transactions.TryGetValue(transaction, out var row)) { row.Ended = Stopwatch.GetTimestamp(); row.Outcome = outcome; } }
    }
}