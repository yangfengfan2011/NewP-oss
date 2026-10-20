using System.Collections.Concurrent;
using System.Data.Common;
using System.Text.Json;
using ICP.Platform.Persistence.EfCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed partial class WorkspaceClipboardSqlServerHttpApiTests
{
    // 原请求成本先End；只重放当前稳定的ACL/角色SELECT，不拿副本写后业务行冒充写前计划。
    private async Task SavePartCopyCostAsync(BusinessAuditHostFixture fixture, ObjectCopyCost commands,
        ObjectCopyCostConnections connections, string operation, object requestCost)
    {
        var stage = Environment.GetEnvironmentVariable("NEWP_PART_OBJECT_COPY_COST_STAGE");
        if (stage is null) return;
        Assert.Contains(stage, new[] { "baseline", "candidate" });
        var selected = commands.Reads.OrderBy(sample => sample.Sql.Contains("[DataScope", StringComparison.Ordinal) ? 0 : 1)
            .DistinctBy(sample => sample.Sql).Take(3).ToArray();
        Assert.NotEmpty(selected);
        using var scope = fixture.Services.CreateScope();
        var connection = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>().Database.GetConnectionString()!;
        var plans = new List<ExampleExecutionSqlPlanTests.Measurement>();
        foreach (var sample in selected)
            plans.Add((await ExampleExecutionSqlPlanTests.ExplainAsync(connection, sample, requireIo: false)) with
            { Timing = "after-successful-request-stable-acl-role-configuration-only" });
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
        Assert.NotNull(root);
        var directory = Path.Combine(root.FullName, ".codex-temp", "issue-305", "316", "cost");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, stage + "-" + operation + "-" + Guid.NewGuid().ToString("N") + ".json");
        var result = new { operation, requestCost, commands = commands.Sql.ToArray(), connections = connections.Items.ToArray(), plans,
            scope = "Original single plus concurrency4 with ordinary actor; default full cancel-race unchanged. Independent selected SELECT IO/XML, not full-request IO or mutation-prestate plan; diagnostics excluded from request timing/counts." };
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        output.WriteLine(JsonSerializer.Serialize(new { operation, path, commands = commands.Sql.Count, selectedPlans = plans.Count,
            opens = connections.Items.Count(item => item.Operation == "open"), closes = connections.Items.Count(item => item.Operation == "close") }));
    }

    private sealed record ObjectCopyConnectionTiming(string Operation, double Milliseconds);

    // 复用现有成本资产的EF连接事件口径；诊断Explain使用独立连接，原请求End后不再记录。
    private sealed class ObjectCopyCostConnections : DbConnectionInterceptor
    {
        private volatile bool active;
        internal ConcurrentQueue<ObjectCopyConnectionTiming> Items { get; } = new();
        internal void Begin() { Items.Clear(); active = true; }
        internal void End() => active = false;
        private void Record(ConnectionEndEventData data, string operation)
        { if (active) Items.Enqueue(new(operation, data.Duration.TotalMilliseconds)); }
        public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData data) => Record(data, "open");
        public override void ConnectionClosed(DbConnection connection, ConnectionEndEventData data) => Record(data, "close");
        public override Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData data, CancellationToken token = default)
        { Record(data, "open"); return Task.CompletedTask; }
        public override Task ConnectionClosedAsync(DbConnection connection, ConnectionEndEventData data)
        { Record(data, "close"); return Task.CompletedTask; }
    }
}