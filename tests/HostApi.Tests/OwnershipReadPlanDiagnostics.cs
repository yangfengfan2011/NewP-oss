using System.Collections.Concurrent;
using System.Data.Common;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Xunit;
using Xunit.Abstractions;

namespace ICP.Platform.HostApi.Tests;

// 只补成功请求的关键只读查询计划；SQL计数仍由原Queue observer负责，无完成配对或取消协议。
internal sealed class OwnershipReadPlanDiagnostics : IObserver<DiagnosticListener>, IObserver<KeyValuePair<string, object?>>, IDisposable
{
    private readonly ConcurrentQueue<ExampleExecutionSqlPlanTests.Sample> reads = new();
    private readonly ConcurrentBag<IDisposable> subscriptions = [];
    private readonly IDisposable all;
    private readonly ITestOutputHelper output;
    private readonly bool diagnostic;
    private readonly string? directory;
    private volatile bool active;
    private bool unchangedTargets;
    private string operation = "";
    internal string Database { get; set; } = "";
    internal bool Enabled { get; }
    internal string OutputDirectory => directory ?? throw new InvalidOperationException("Diagnostic measurement is disabled.");
    internal IReadOnlyList<ExampleExecutionSqlPlanTests.Sample> Captured => reads.ToArray();

    internal OwnershipReadPlanDiagnostics(ITestOutputHelper output, string family)
    {
        this.output = output;
        var stage = Environment.GetEnvironmentVariable("NEWP_OWNERSHIP_COST_STAGE");
        Enabled = stage is not null;
        diagnostic = Enabled && Environment.GetEnvironmentVariable("NEWP_OWNERSHIP_COST_IO") == "1";
        if (diagnostic)
        {
            Assert.Contains(stage, new[] { "baseline", "candidate" });
            var root = new DirectoryInfo(AppContext.BaseDirectory);
            while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
            Assert.NotNull(root);
            directory = Path.Combine(root.FullName, ".codex-temp", "issue-305", "319", "cost", stage + "-" + family + "-key-read-plan-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
        }
        all = DiagnosticListener.AllListeners.Subscribe(this);
    }

    internal void Begin(string name, bool currentReadOnly = false)
    {
        operation = name; unchangedTargets = currentReadOnly; reads.Clear(); active = diagnostic;
    }

    // 请求已成功且Before同步回调均随命令返回；只重放当前仍稳定的SELECT，不等待未知终结事件。
    internal async Task EndAsync(int responseBytes, int successfulSqlCommands, string connectionString)
    {
        active = false;
        if (!diagnostic) return;
        var selected = reads.ToArray().OrderBy(sample => sample.Sql.Contains("[DataScope", StringComparison.Ordinal) ? 0 : 1)
            .DistinctBy(sample => sample.Sql).Take(3).ToArray();
        var plans = new List<ExampleExecutionSqlPlanTests.Measurement>();
        foreach (var sample in selected)
            plans.Add((await ExampleExecutionSqlPlanTests.ExplainAsync(connectionString, sample, requireIo: false)) with {
                Timing = unchangedTargets ? "after-successful-read-unchanged-current-facts" : "after-successful-mutation-unchanged-acl-role-configuration-only" });
        var result = new { operation, mode = "selected-stable-read-query-diagnostic", responseBytes,
            successfulSqlCommands, countSource = "existing-concurrent-queue-successful-After-observer", plans,
            scope = "at most3representative SELECTs; not full-request IO, not mutation-prestate plans; diagnostic timing excluded" };
        await File.WriteAllTextAsync(Path.Combine(directory!, operation + ".json"), JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        output.WriteLine(JsonSerializer.Serialize(new { operation, successfulSqlCommands, selectedPlans = plans.Count, directory }));
    }

    public void OnNext(DiagnosticListener listener)
    {
        if (listener.Name == "SqlClientDiagnosticListener") subscriptions.Add(listener.Subscribe(this));
    }
    public void OnNext(KeyValuePair<string, object?> value)
    {
        if (!active || !value.Key.EndsWith("WriteCommandBefore", StringComparison.Ordinal)
            || value.Value?.GetType().GetProperty("Command")?.GetValue(value.Value) is not DbCommand command
            || command.Connection?.Database != Database || !StableRead(command.CommandText)) return;
        // Before时命令尚未交还EF，克隆实际参数只供本次独立诊断，不落参数值或凭据。
        reads.Enqueue(new(command.CommandText, command.Parameters.Cast<SqlParameter>().Select(parameter => (SqlParameter)((ICloneable)parameter).Clone()).ToArray()));
    }

    private bool StableRead(string sql)
    {
        if (!sql.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)
            || sql.Contains("[Authentication", StringComparison.Ordinal) || sql.Contains("[Identity", StringComparison.Ordinal)) return false;
        if (unchangedTargets) return true;
        // 转交/补齐已经改变业务行，不能用之后的0行或新owner冒充写前计划。
        return !new[] { "[Part]", "[Document]", "[Workspace" }.Any(table => sql.Contains(table, StringComparison.Ordinal))
            && new[] { "[DataScope", "[Role]", "[UserRole]" }.Any(table => sql.Contains(table, StringComparison.Ordinal));
    }
    public void OnCompleted() { }
    public void OnError(Exception error) => throw new InvalidOperationException("SqlClient read-plan observation failed.", error);
    public void Dispose() { active = false; foreach (var subscription in subscriptions) subscription.Dispose(); all.Dispose(); }
}
