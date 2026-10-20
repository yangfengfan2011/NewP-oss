using System.Diagnostics;
using System.Data.Common;
using System.Net;
using System.Text.Json;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

/// <summary>正式执行成本复用的SQL统计与实际计划诊断资产；历史迁移比较见各固定提交证据。</summary>
public sealed partial class ExampleExecutionSqlPlanTests
{
    [EnvironmentGatedFact("NEWP_RUN_ENTITY_PERMISSION_CAPACITY", "先验证SQL统计观察器能捕获真实IO与实际计划")]
    public async Task PermissionCostObservation_CapturesLogicalReadsAndActualPlan()
    {
        await using var database = await ReusableSqlServerDatabase.AcquireAsync();
        using var capture = new Capture { Active = true };
        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sys.objects /* DataScope instrumentation */";
        await using (var reader = await command.ExecuteReaderAsync()) while (await reader.ReadAsync()) { }
        capture.Active = false;
        var sample = Assert.Single(capture.Samples);
        var observed = await ExplainAsync(database.ConnectionString, sample);
        Assert.NotEmpty(observed.Statistics);
        Assert.NotEmpty(observed.Plans);
    }

    internal sealed record Measurement(string Sql, string[] Statistics, string[] Plans, int ReturnedRows)
    {
        public string Timing { get; init; } = "after-request-unchanged-permission-configuration";
    }
    internal sealed record Sample(string Sql, SqlParameter[] Parameters);
    // 与现有Workspace成本资产同样观察SqlClient，覆盖EF和直接ADO命令，不记录参数或凭据。
    private sealed class Capture : IObserver<DiagnosticListener>, IObserver<KeyValuePair<string, object?>>, IDisposable
    {
        private readonly IDisposable all;
        private readonly List<IDisposable> subscriptions = [];
        internal bool Active;
        internal string ConnectionString = "";
        internal List<Sample> Samples { get; } = [];
        internal List<Measurement> EntityReads { get; } = [];
        internal Capture() => all = DiagnosticListener.AllListeners.Subscribe(this);
        public void OnNext(DiagnosticListener listener)
        {
            if (listener.Name == "SqlClientDiagnosticListener") subscriptions.Add(listener.Subscribe(this));
        }
        public void OnNext(KeyValuePair<string, object?> value)
        {
            if (!Active || !value.Key.EndsWith("WriteCommandBefore", StringComparison.Ordinal)
                || value.Value?.GetType().GetProperty("Command")?.GetValue(value.Value) is not DbCommand command) return;
            if (!command.CommandText.Contains("DataScope", StringComparison.Ordinal) || command.CommandText.Contains("[ExampleRecord]", StringComparison.Ordinal)
                || !command.CommandText.TrimStart().StartsWith("SELECT", StringComparison.Ordinal)) return;
            Samples.Add(new(command.CommandText, command.Parameters.Cast<SqlParameter>().Select(parameter => (SqlParameter)((ICloneable)parameter).Clone()).ToArray()));
        }
        public void OnCompleted() { }
        public void OnError(Exception error) { }
        public void Dispose()
        {
            Active = false;
            foreach (var subscription in subscriptions) subscription.Dispose();
            all.Dispose();
        }
    }
    // 业务对象查询必须在原查询前取证，delete之后不能把对象消失的0行当成删除前成本。
    private sealed class EntityReadPlans(Capture capture) : DbCommandInterceptor
    {
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (capture.Active && command.CommandText.Contains("[ExampleRecord]", StringComparison.Ordinal)
                && command.CommandText.TrimStart().StartsWith("SELECT", StringComparison.Ordinal))
            {
                var sample = new Sample(command.CommandText, command.Parameters.Cast<SqlParameter>().Select(parameter => (SqlParameter)((ICloneable)parameter).Clone()).ToArray());
                capture.EntityReads.Add((await ExplainAsync(capture.ConnectionString, sample)) with { Timing = "before-original-entity-read" });
            }
            return result;
        }
    }
    // 权限配置不被CRUD写入改变；按捕获的同SQL/参数定向重放只读查询，取得真实IO、返回规模与实际计划。
    // 请求本身的命令/连接成本保留原测量；诊断重放的往返不混入运行时成本。
    internal static async Task<Measurement> ExplainAsync(string connectionString, Sample sample, bool requireIo = true)
    {
        await using var connection = new SqlConnection(connectionString);
        var statistics = new List<string>();
        connection.InfoMessage += (_, args) => statistics.Add(args.Message);
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = "SET STATISTICS IO ON; SET STATISTICS XML ON; " + sample.Sql + " SET STATISTICS XML OFF; SET STATISTICS IO OFF;";
        command.Parameters.AddRange(sample.Parameters.Select(parameter => (SqlParameter)((ICloneable)parameter).Clone()).ToArray());
        await using var reader = await command.ExecuteReaderAsync();
        var plans = new List<string>();
        var rows = 0;
        do
        {
            var plan = reader.FieldCount == 1 && reader.GetName(0).Contains("Showplan", StringComparison.OrdinalIgnoreCase);
            while (await reader.ReadAsync())
                if (plan) plans.Add(reader.GetString(0)); else rows++;
        } while (await reader.NextResultAsync());
        Assert.NotEmpty(plans);
        if (requireIo) Assert.NotEmpty(statistics);
        return new(sample.Sql, statistics.ToArray(), plans.ToArray(), rows);
    }
    private static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AGENTS.md"))) directory = directory.Parent;
        return directory!.FullName;
    }
}
