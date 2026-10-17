using System.Collections.Concurrent;
using System.Data;
using System.Security.Claims;
using System.Data.Common;
using System.Diagnostics;
using System.Net;
using System.Text.Json;
using ICP.Platform.Modules.Authorization.DTOs;
using ICP.Platform.Modules.Authorization.Services;
using ICP.Platform.Authorization.Contracts;
using Microsoft.AspNetCore.Http;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;
using static ICP.Platform.HostApi.Tests.EntityOwnershipBackfillSqlServerHttpApiTests;

namespace ICP.Platform.HostApi.Tests;

/// <summary>实际权限请求的成本资产，数量是测量样本，不是产品上限或通过预算。</summary>
public sealed partial class DataScopePermissionCostSqlServerTests(ITestOutputHelper output)
{
    [EnvironmentGatedFact("NEWP_RUN_ENTITY_PERMISSION_CAPACITY", "独占记录实际Document解释首次/同输入稳态请求构成，不重复整个功能旅程")]
    public async Task DocumentExplanation_RecordsFirstAndSteadyRequestSqlAllocationAndOutput()
    {
        var commands = new CostCommands();
        var connections = new CostConnections();
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(services =>
            services.AddDbContext<EfPlatformDbContext>((_, options) => options.AddInterceptors(commands, connections)), includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        await InitializeAsync(fixture, admin);
        var position = await ContainerAsync(fixture, admin);
        var document = await CreateAsync(fixture, admin, position, "Document", "documents", "createDocument", "Permission295CostDocument");
        var read = fixture.FeatureCatalog.Single(point => point.ModuleId == "Document" && point.Kind == "navigation" && point.Key == "document.documents");
        var manage = fixture.FeatureCatalog.Single(point => point.ModuleId == "Document" && point.Key == "authorize" && point.ActionScope == "list.rowActions");
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "Permission295Cost", "权限成本",
            "permission295-cost", "权限成本账号", read);
        using var ordinary = actor.Client;
        var path = "/api/document/documents/" + document.Id + "/permissions";
        using (var saved = await FeatureHttpTestRequests.SendAsync(admin, manage, HttpMethod.Put, path,
            new DataScopeObjectDto("document.document", document.Id,
                [new("Permission295CostAll", "all", null, 0, new Dictionary<string, string> { ["download"] = "deny" })])))
            Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);
        // 准备不计入请求成本；首次指本fixture首次解释，不称Host冷启动或全进程JIT冷态。
        JsonElement? first = null;
        foreach (var phase in new[] { "first-explanation-request", "same-input-steady-request" })
        {
            commands.Begin();
            connections.Begin();
            var allocated = GC.GetTotalAllocatedBytes(false);
            var watch = Stopwatch.StartNew();
            JsonElement result;
            try { result = await FeatureHttpTestRequests.ReadAsync(ordinary, read, path + "/explanation"); }
            finally { commands.End(); connections.End(); }
            watch.Stop();
            Assert.NotEmpty(commands.Items);
            var evidence = new
            {
                phase, preparedDocuments = 1, directSubjectRows = 1, directActionRows = 1,
                ruleNodes = 0, grantedOrdinaryRoles = 1,
                elapsedMilliseconds = watch.Elapsed.TotalMilliseconds,
                allocatedBytes = GC.GetTotalAllocatedBytes(false) - allocated,
                outputBytes = System.Text.Encoding.UTF8.GetByteCount(result.GetRawText()),
                actions = result.GetProperty("actions").GetArrayLength(),
                sqlCommands = commands.Items.Count,
                sqlExecuteMilliseconds = commands.Items.Sum(item => item.ExecuteMilliseconds),
                connectionOpens = connections.Items.Count(item => item.Operation == "open"),
                connectionCloses = connections.Items.Count(item => item.Operation == "close"),
                connectionOpenMilliseconds = connections.Items.Where(item => item.Operation == "open").Sum(item => item.Milliseconds),
                connectionCloseMilliseconds = connections.Items.Where(item => item.Operation == "close").Sum(item => item.Milliseconds),
                connections = connections.Items.ToArray(),
                commands = commands.Items.ToArray()
            };
            Assert.False(result.GetProperty("actions").EnumerateArray().Single(action => action.GetProperty("key").GetString() == "download")
                .GetProperty("allowed").GetBoolean());
            if (first is not null) Assert.Equal(first.Value.GetRawText(), result.GetRawText());
            first = result;
            Assert.Equal(connections.Items.Count(item => item.Operation == "open"), connections.Items.Count(item => item.Operation == "close"));
            output.WriteLine(JsonSerializer.Serialize(evidence));
        }
        using var scope = fixture.Services.CreateScope();
        var provider = scope.ServiceProvider;
        var db = provider.GetRequiredService<EfPlatformDbContext>();
        var accessor = provider.GetRequiredService<IHttpContextAccessor>();
        var previous = accessor.HttpContext;
        accessor.HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, actor.AccountId)], "test-trusted-account")) };
        try
        {
            var queries = provider.GetRequiredService<IDataScopeObjectPermissionQueries>();
            Assert.Equal(ConnectionState.Closed, db.Database.GetDbConnection().State);
            connections.Begin();
            try { Assert.NotNull(await queries.InspectAsync("document.document", document.Id, null)); }
            finally { connections.End(); }
            AssertOwnedRead();
            await db.Database.OpenConnectionAsync();
            try
            {
                connections.Begin();
                try { Assert.NotNull(await queries.InspectAsync("document.document", document.Id, null)); }
                finally { connections.End(); }
                Assert.Empty(connections.Items);
                Assert.Equal(ConnectionState.Open, db.Database.GetDbConnection().State);
            }
            finally { await db.Database.CloseConnectionAsync(); }
            connections.Begin();
            try { await Assert.ThrowsAsync<RolePermissionValidationException>(() => queries.InspectAsync("document.document", document.Id, new string('x', 129))); }
            finally { connections.End(); }
            AssertOwnedRead();
            using var cancellation = new CancellationTokenSource();
            commands.Begin(); connections.Begin(); commands.CancelOnCommand = cancellation;
            try { await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queries.InspectAsync("document.document", document.Id, null, cancellation.Token)); }
            finally { commands.CancelOnCommand = null; commands.End(); connections.End(); }
            AssertOwnedRead();
            output.WriteLine("inspectionConnectionOwnership: success/exception/cancel self-opened=1/closed=1; caller-opened preserved");
        }
        finally { accessor.HttpContext = previous; }

        void AssertOwnedRead()
        {
            Assert.Equal(1, connections.Items.Count(item => item.Operation == "open"));
            Assert.Equal(1, connections.Items.Count(item => item.Operation == "close"));
            Assert.Equal(ConnectionState.Closed, db.Database.GetDbConnection().State);
        }
    }

    private sealed record ConnectionTiming(Guid ConnectionId, string Operation, double Milliseconds);

    // EF连接事件只记录打开/关闭调用耗时，不把它等同于命令执行、读取行或数据库CPU。
    private sealed class CostConnections : DbConnectionInterceptor
    {
        private volatile bool active;
        internal ConcurrentQueue<ConnectionTiming> Items { get; } = new();
        internal void Begin() { Items.Clear(); active = true; }
        internal void End() => active = false;
        private void Record(ConnectionEndEventData data, string operation)
        {
            if (active) Items.Enqueue(new(data.ConnectionId, operation, data.Duration.TotalMilliseconds));
        }
        public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData data) => Record(data, "open");
        public override void ConnectionClosed(DbConnection connection, ConnectionEndEventData data) => Record(data, "close");
        public override Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData data, CancellationToken token = default)
        { Record(data, "open"); return Task.CompletedTask; }
        public override Task ConnectionClosedAsync(DbConnection connection, ConnectionEndEventData data)
        { Record(data, "close"); return Task.CompletedTask; }
    }

    private sealed class CostCommand(string sql, int parameters, Guid connectionId)
    {
        public string Sql { get; } = sql;
        public Guid ConnectionId { get; } = connectionId;
        public int Parameters { get; } = parameters;
        public double ExecuteMilliseconds { get; set; }
        public long CompletedTimestamp { get; set; }
    }

    private sealed class CostCommands : DbCommandInterceptor
    {
        private readonly ConcurrentDictionary<Guid, CostCommand> pending = new();
        private volatile bool active;
        internal ConcurrentQueue<CostCommand> Items { get; } = new();
        internal CancellationTokenSource? CancelOnCommand { get; set; }
        internal void Begin() { pending.Clear(); Items.Clear(); active = true; }
        internal void End() => active = false;
        private void Record(DbCommand command, CommandEventData data)
        {
            if (!active) return;
            var item = new CostCommand(command.CommandText, command.Parameters.Count, data.ConnectionId);
            pending[data.CommandId] = item; Items.Enqueue(item);
            CancelOnCommand?.Cancel();
        }
        private void Complete(CommandExecutedEventData data)
        {
            if (pending.TryRemove(data.CommandId, out var item))
            { item.ExecuteMilliseconds = data.Duration.TotalMilliseconds; item.CompletedTimestamp = Stopwatch.GetTimestamp(); }
        }
        public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData data,
            InterceptionResult<DbDataReader> result) { Record(command, data); return result; }
        public override DbDataReader ReaderExecuted(DbCommand command, CommandExecutedEventData data, DbDataReader result)
        { Complete(data); return result; }
        public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData data,
            InterceptionResult<int> result) { Record(command, data); return result; }
        public override int NonQueryExecuted(DbCommand command, CommandExecutedEventData data, int result)
        { Complete(data); return result; }
        public override InterceptionResult<object> ScalarExecuting(DbCommand command, CommandEventData data,
            InterceptionResult<object> result) { Record(command, data); return result; }
        public override object? ScalarExecuted(DbCommand command, CommandExecutedEventData data, object? result)
        { Complete(data); return result; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData data,
            InterceptionResult<DbDataReader> result, CancellationToken token = default) { Record(command, data); return ValueTask.FromResult(result); }
        public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData data, DbDataReader result,
            CancellationToken token = default) { Complete(data); return ValueTask.FromResult(result); }
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData data,
            InterceptionResult<int> result, CancellationToken token = default) { Record(command, data); return ValueTask.FromResult(result); }
        public override ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData data, int result,
            CancellationToken token = default) { Complete(data); return ValueTask.FromResult(result); }
        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command, CommandEventData data,
            InterceptionResult<object> result, CancellationToken token = default) { Record(command, data); return ValueTask.FromResult(result); }
        public override ValueTask<object?> ScalarExecutedAsync(DbCommand command, CommandExecutedEventData data, object? result,
            CancellationToken token = default) { Complete(data); return ValueTask.FromResult(result); }
    }
}
