using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Net;
using System.Text.Json;
using ICP.Platform.Modules.Files;
using ICP.Platform.Modules.Files.Services;
using ICP.Platform.Modules.Workspace;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed partial class DocumentFilesSqlServerHttpApiTests
{
    [EnvironmentGatedFact("NEWP_RUN_ENTITY_PERMISSION_CAPACITY", "独占比较1/3文件真实整请求释放的SQL、连接及原事务锁区间，不夹带诊断SQL")]
    public Task FilesRelease_RecordsOneAndThreeFileCosts() => MeasureFileReleaseAsync(diagnosticIo: false);

    [EnvironmentGatedFact("NEWP_RUN_ENTITY_PERMISSION_CAPACITY", "独立诊断轮在实际删除事务采集STATISTICS IO，不回放删除后SELECT或声明XML计划")]
    public Task FilesRelease_CollectsLiveSqlStatisticsIo() => MeasureFileReleaseAsync(diagnosticIo: true);

    private async Task MeasureFileReleaseAsync(bool diagnosticIo)
    {
        var capture = new FileReleaseCommands();
        var connections = new FileCostConnections();
        var io = new FileReleaseIoConnections();
        var transactionClock = new FileReleaseTransactions(capture);
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(services =>
        {
            services.Remove(Assert.Single(services, item => item.ServiceType == typeof(IHostedService) && item.ImplementationType == typeof(FileCleanupWorker)));
            services.Configure<FilesOptions>(options => options.MaxReadBatchSize = 2);
            services.AddDbContext<EfPlatformDbContext>((_, options) => options.AddInterceptors(capture, connections, transactionClock, io));
        }, includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        var position = await ContentAsync(fixture, admin);
        var deletion = Point(fixture, "workspace.folder.deleteObject");
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "ReleaseCost313", "文件释放成本", "release-cost313", "释放成本普通主体",
            Point(fixture, "workspace.spaces.access"), Point(fixture, "document.document.read"), deletion);
        using var ordinary = actor.Client;
        var stage = Environment.GetEnvironmentVariable("NEWP_FILES_COST_STAGE") ?? "candidate";
        var run = Path.Combine(CostRoot(), ".codex-temp", "issue-305", "313", "cost", stage + "-release-" + (diagnosticIo ? "io-" : "main-") + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(run);
        foreach (var count in new[] { 1, 3 })
        {
            var warm = await PrepareReleaseDocumentAsync(fixture, admin, position, count, "WARM");
            var sample = await PrepareReleaseDocumentAsync(fixture, admin, position, count, "MEASURE");
            // 相同文件数的暖删除与真实准备均在采集外；原Workspace/Document/Files链不替换、不代理。
            using (var warmed = await FeatureHttpTestRequests.SendAsync(ordinary, deletion, HttpMethod.Delete, "/api/workspace/content/" + warm, new { position }))
                Assert.True(warmed.StatusCode == HttpStatusCode.OK, await warmed.Content.ReadAsStringAsync());
            capture.Clear(); connections.Items.Clear(); io.Messages.Clear();
            capture.Active = true; connections.Active = true; io.Active = diagnosticIo;
            var watch = Stopwatch.StartNew(); byte[] bytes;
            try
            {
                using var response = await FeatureHttpTestRequests.SendAsync(ordinary, deletion, HttpMethod.Delete, "/api/workspace/content/" + sample, new { position });
                bytes = await response.Content.ReadAsByteArrayAsync();
                Assert.True(response.StatusCode == HttpStatusCode.OK, System.Text.Encoding.UTF8.GetString(bytes));
            }
            finally
            {
                watch.Stop(); capture.Active = false; connections.Active = false; io.Active = false;
                // 失败或取消同样归还诊断会话；不能用带有残留设置的连接继续测下一份样本。
                await io.ResetRemainingAsync();
            }
            Assert.Equal(count, capture.Locks.Count(item => item.Kind == "Files"));
            Assert.All(capture.Transactions.Values, item => Assert.NotEqual(0, item.Ended));
            if (diagnosticIo) Assert.Contains(io.Messages, item => item.Contains("logical reads", StringComparison.OrdinalIgnoreCase) || item.Contains("逻辑读取", StringComparison.Ordinal));
            var operation = $"files{count}-release";
            await File.WriteAllTextAsync(Path.Combine(run, operation + ".json"), JsonSerializer.Serialize(new
            {
                stage, operation, fileCount = count, maxReadBatchSize = 2, diagnosticIo,
                responseBytes = bytes.Length, sqlCommands = capture.Commands.Count, commands = capture.Commands,
                elapsedMilliseconds = diagnosticIo ? (double?)null : watch.Elapsed.TotalMilliseconds,
                connections = connections.Items.ToArray(),
                transactions = diagnosticIo ? null : capture.TransactionMeasurements(),
                locks = diagnosticIo ? null : capture.LockMeasurements(),
                statisticsIo = io.Messages.ToArray(),
                note = diagnosticIo ? "Actual original DELETE statements; connection SET ON/OFF is diagnostic only. No replay or XML plan. Diagnostic elapsed/lock intervals excluded."
                    : "Whole Workspace DELETE, including existing authorization and Files release. Warm/delete preparation excluded. Lock acquire/hold are client-observed intervals, not SQL engine lock ticks."
            }, new JsonSerializerOptions { WriteIndented = true }));
            output.WriteLine($"{stage}/{operation}/{(diagnosticIo ? "io" : "main")}: SQL={capture.Commands.Count}, IO={io.Messages.Count}, artifacts={run}");
        }
    }

    private static async Task<string> PrepareReleaseDocumentAsync(BusinessAuditHostFixture fixture, HttpClient admin, WorkspacePosition position, int count, string suffix)
    {
        var values = new { ECODE = $"RELEASE-COST313-{count}-{suffix}", ENAME = "释放成本样本" };
        var uploads = new List<string>();
        for (var index = 0; index < count; index++)
        {
            var staged = await StageAsync(fixture, admin, position, values, [0x25, 0x50, 0x44, 0x46, (byte)index], $"release-{index}.pdf");
            uploads.Add(staged.GetProperty("uploadId").GetString()!);
        }
        using var created = await CreateDocumentAsync(fixture, admin, new { position, values, uploads = new { mainUploadId = uploads[0], attachmentUploadIds = uploads.Skip(1).ToArray() } });
        Assert.True(created.StatusCode == HttpStatusCode.OK, await created.Content.ReadAsStringAsync());
        var identity = await ReadCreatedAsync(fixture, values.ECODE);
        using var scope = fixture.Services.CreateScope();
        var row = await scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>().Set<Dictionary<string, object?>>("WorkspaceFolderContent").AsNoTracking()
            .SingleAsync(item => EF.Property<string>(item, "ChildModelKey") == "Document" && EF.Property<string>(item, "CEID") == identity.DocumentId);
        return (string)row["LID"]!;
    }

    private sealed class FileReleaseCommands : DbCommandInterceptor
    {
        internal bool Active;
        internal List<object> Commands { get; } = [];
        internal Dictionary<DbTransaction, ReleaseTransaction> Transactions { get; } = [];
        internal List<ReleaseLock> Locks { get; } = [];
        private readonly Dictionary<DbCommand, ReleaseLock> pending = [];
        internal void Clear() { Commands.Clear(); Transactions.Clear(); Locks.Clear(); pending.Clear(); }
        private void Observe(DbCommand command)
        {
            if (!Active) return;
            Commands.Add(new { sql = command.CommandText, parameters = command.Parameters.Cast<DbParameter>().Select(item => new { item.ParameterName, type = item.DbType.ToString(), item.Size }).ToArray() });
            if (!command.CommandText.Contains("sp_getapplock", StringComparison.OrdinalIgnoreCase) || command.Transaction is not { } transaction) return;
            var kind = command.Parameters.Cast<DbParameter>().Any(item => item.Value is string key && key.StartsWith("Files.", StringComparison.Ordinal)) ? "Files" : "Business";
            var sample = new ReleaseLock(kind, transaction, Stopwatch.GetTimestamp()); Locks.Add(sample); pending.Add(command, sample);
        }
        private void Executed(DbCommand command) { if (Active && pending.Remove(command, out var item)) item.Acquired = Stopwatch.GetTimestamp(); }
        internal object[] TransactionMeasurements() => Transactions.Values.Select(item => (object)new { item.Outcome, elapsedMilliseconds = Stopwatch.GetElapsedTime(item.Started, item.Ended).TotalMilliseconds }).ToArray();
        internal object[] LockMeasurements() => Locks.Select(item => (object)new { item.Kind,
            acquireMilliseconds = item.Acquired > 0 ? Stopwatch.GetElapsedTime(item.Started, item.Acquired).TotalMilliseconds : (double?)null,
            holdObservedMilliseconds = item.Acquired > 0 && Transactions.TryGetValue(item.Transaction, out var transaction) && transaction.Ended > 0
                ? Stopwatch.GetElapsedTime(item.Acquired, transaction.Ended).TotalMilliseconds : (double?)null }).ToArray();
        public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData data, InterceptionResult<DbDataReader> result) { Observe(command); return result; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData data, InterceptionResult<DbDataReader> result, CancellationToken token = default) { Observe(command); return ValueTask.FromResult(result); }
        public override InterceptionResult<object> ScalarExecuting(DbCommand command, CommandEventData data, InterceptionResult<object> result) { Observe(command); return result; }
        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command, CommandEventData data, InterceptionResult<object> result, CancellationToken token = default) { Observe(command); return ValueTask.FromResult(result); }
        public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData data, InterceptionResult<int> result) { Observe(command); return result; }
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData data, InterceptionResult<int> result, CancellationToken token = default) { Observe(command); return ValueTask.FromResult(result); }
        public override int NonQueryExecuted(DbCommand command, CommandExecutedEventData data, int result) { Executed(command); return result; }
        public override ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData data, int result, CancellationToken token = default) { Executed(command); return ValueTask.FromResult(result); }
    }
    private sealed class ReleaseTransaction(long started)
    {
        internal long Started = started; internal long Ended; internal string? Outcome;
    }
    private sealed class ReleaseLock(string kind, DbTransaction transaction, long started)
    {
        internal string Kind = kind; internal DbTransaction Transaction = transaction; internal long Started = started; internal long Acquired;
    }
    private sealed class FileReleaseTransactions(FileReleaseCommands commands) : DbTransactionInterceptor
    {
        public override ValueTask<DbTransaction> TransactionStartedAsync(DbConnection connection, TransactionEndEventData data, DbTransaction result, CancellationToken token = default)
        { if (commands.Active) commands.Transactions.Add(result, new(Stopwatch.GetTimestamp())); return ValueTask.FromResult(result); }
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData data, CancellationToken token = default) { End(transaction, "committed"); return Task.CompletedTask; }
        public override Task TransactionRolledBackAsync(DbTransaction transaction, TransactionEndEventData data, CancellationToken token = default) { End(transaction, "rolledBack"); return Task.CompletedTask; }
        private void End(DbTransaction transaction, string outcome)
        { if (commands.Active && commands.Transactions.TryGetValue(transaction, out var item)) { item.Ended = Stopwatch.GetTimestamp(); item.Outcome = outcome; } }
    }

    // IO只在单独诊断轮启用，实际业务命令不改写；归还连接前清除设置，失败则丢弃该精确连接。
    private sealed class FileReleaseIoConnections : DbConnectionInterceptor
    {
        internal bool Active;
        internal List<string> Messages { get; } = [];
        private readonly Dictionary<SqlConnection, (DbContext? Context, SqlInfoMessageEventHandler Handler)> tracked = [];
        private void Enable(DbConnection connection, ConnectionEndEventData data)
        {
            if (!Active) return;
            var sql = (SqlConnection)connection;
            SqlInfoMessageEventHandler handler = (_, args) => Messages.Add(args.Message);
            tracked.Add(sql, (data.Context, handler)); sql.InfoMessage += handler;
            using var command = sql.CreateCommand(); command.CommandText = "SET STATISTICS IO ON;"; command.ExecuteNonQuery();
        }
        private void Reset(SqlConnection sql)
        {
            if (!tracked.Remove(sql, out var item)) return;
            try
            {
                if (sql.State == ConnectionState.Open)
                {
                    using var command = sql.CreateCommand(); command.CommandText = "SET STATISTICS IO OFF;";
                    command.Transaction = item.Context?.Database.CurrentTransaction?.GetDbTransaction() as SqlTransaction;
                    command.ExecuteNonQuery();
                }
            }
            catch { SqlConnection.ClearPool(sql); throw; }
            finally { sql.InfoMessage -= item.Handler; }
        }
        internal Task ResetRemainingAsync() { foreach (var sql in tracked.Keys.ToArray()) Reset(sql); return Task.CompletedTask; }
        public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData data) => Enable(connection, data);
        public override Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData data, CancellationToken token = default) { Enable(connection, data); return Task.CompletedTask; }
        public override InterceptionResult ConnectionClosing(DbConnection connection, ConnectionEventData data, InterceptionResult result) { Reset((SqlConnection)connection); return result; }
        public override ValueTask<InterceptionResult> ConnectionClosingAsync(DbConnection connection, ConnectionEventData data, InterceptionResult result) { Reset((SqlConnection)connection); return ValueTask.FromResult(result); }
    }
}
