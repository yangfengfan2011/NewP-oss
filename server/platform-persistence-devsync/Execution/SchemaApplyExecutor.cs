using Microsoft.Data.SqlClient;
using ICP.Platform.Persistence.DevSync.Planning;

namespace ICP.Platform.Persistence.DevSync.Execution;

/// <summary>
/// 结构同步执行结果，供 Host/CLI 判断是否已执行、是否由确认策略取消，以及实际影响的操作/语句数量。
/// </summary>
/// <param name="Applied">是否完成执行；为 false 时调用方应结合 Cancelled 或上层 diagnostics 定位失败信号。</param>
/// <param name="Cancelled">是否被确认策略取消；取消必须阻断开发态启动。</param>
/// <param name="ExecutedOperationCount">已执行的计划操作数量。</param>
/// <param name="ExecutedStatementCount">已执行的 SQL 语句数量。</param>
public sealed record ApplyResult(
    bool Applied,
    bool Cancelled,
    int ExecutedOperationCount,
    int ExecutedStatementCount);

/// <summary>
/// 结构同步 SQL 执行器；执行职责收口在代码内核，调用方必须先完成计划审查和显式确认。
/// </summary>
public sealed class SchemaApplyExecutor
{
    private readonly Func<SchemaPlan, bool> _confirmAll;
    private readonly Func<IReadOnlyList<SchemaOperation>, bool> _confirmRisk;
    private readonly Func<IReadOnlyList<string>, CancellationToken, Task<int>> _executeSqlAsync;

    /// <summary>
    /// 创建可注入确认策略和 SQL 执行委托的执行器；测试可借此验证阻断路径不会触达数据库。
    /// </summary>
    /// <param name="confirmAll">整体计划确认策略。</param>
    /// <param name="confirmRisk">Confirm 风险操作确认策略；任务 7 正常路径不会放行 Confirm 操作。</param>
    /// <param name="executeSqlAsync">SQL 批量执行委托。</param>
    public SchemaApplyExecutor(
        Func<SchemaPlan, bool> confirmAll,
        Func<IReadOnlyList<SchemaOperation>, bool> confirmRisk,
        Func<IReadOnlyList<string>, CancellationToken, Task<int>> executeSqlAsync)
    {
        _confirmAll = confirmAll ?? throw new ArgumentNullException(nameof(confirmAll));
        _confirmRisk = confirmRisk ?? throw new ArgumentNullException(nameof(confirmRisk));
        _executeSqlAsync = executeSqlAsync ?? throw new ArgumentNullException(nameof(executeSqlAsync));
    }

    /// <summary>
    /// 使用 SQL Server 批量执行器创建默认执行器；真实执行统一收口到单事务中，避免半成功状态难以恢复。
    /// </summary>
    public static SchemaApplyExecutor CreateForSqlServer(
        string connectionString,
        Func<SchemaPlan, bool> confirmAll,
        Func<IReadOnlyList<SchemaOperation>, bool> confirmRisk)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        return new SchemaApplyExecutor(
            confirmAll,
            confirmRisk,
            (sqlBatch, cancellationToken) => ExecuteSqlServerBatchAsync(connectionString, sqlBatch, cancellationToken));
    }

    /// <summary>
    /// 执行已生成 SQL 预览的结构计划；缺少 SQL 预览会抛出异常，供上层 diagnostics 暴露失败信号。
    /// </summary>
    /// <param name="plan">已完成风险分类和 SQL 预览生成的结构计划。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>执行结果，包含取消状态和执行计数。</returns>
    public async Task<ApplyResult> ApplyAsync(SchemaPlan plan, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);

        if (plan.Operations.Count == 0)
        {
            return new ApplyResult(true, false, 0, 0);
        }

        if (!_confirmAll(plan))
        {
            return new ApplyResult(false, true, 0, 0);
        }

        var confirmOperations = plan.Operations
            .Where(operation => string.Equals(operation.RiskLevel, RiskLevel.Confirm, StringComparison.Ordinal))
            .ToArray();

        if (confirmOperations.Length > 0 && !_confirmRisk(confirmOperations))
        {
            return new ApplyResult(false, true, 0, 0);
        }

        var missingSqlPreviewOperation = plan.Operations.FirstOrDefault(static operation => operation.SqlPreview.Count == 0);
        if (missingSqlPreviewOperation is not null)
        {
            throw new InvalidOperationException($"操作 '{missingSqlPreviewOperation.Kind}' 尚未生成 SQL 预览，无法执行。");
        }

        var sqlBatch = plan.Operations
            .SelectMany(static operation => operation.SqlPreview)
            .Where(static sql => !string.IsNullOrWhiteSpace(sql))
            .ToArray();

        var executedStatementCount = await _executeSqlAsync(sqlBatch, cancellationToken);
        return new ApplyResult(true, false, plan.Operations.Count, executedStatementCount);
    }

    private static async Task<int> ExecuteSqlServerBatchAsync(
        string connectionString,
        IReadOnlyList<string> sqlBatch,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sqlBatch);

        if (sqlBatch.Count == 0)
        {
            return 0;
        }

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            var executedStatementCount = 0;

            foreach (var sql in sqlBatch)
            {
                await using var command = connection.CreateCommand();
                command.Transaction = (SqlTransaction)transaction;
                command.CommandText = sql;
                await command.ExecuteNonQueryAsync(cancellationToken);
                executedStatementCount++;
            }

            await transaction.CommitAsync(cancellationToken);
            return executedStatementCount;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }
}
