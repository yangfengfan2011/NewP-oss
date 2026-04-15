using Microsoft.Data.SqlClient;
using ICP.Platform.Tools.DevSchemaSync.Planning;

namespace ICP.Platform.Tools.DevSchemaSync.Execution;

public sealed record ApplyResult(
    bool Applied,
    bool Cancelled,
    int ExecutedOperationCount,
    int ExecutedStatementCount);

public sealed class SchemaApplyExecutor
{
    private readonly Func<SchemaPlan, bool> _confirmAll;
    private readonly Func<IReadOnlyList<SchemaOperation>, bool> _confirmRisk;
    private readonly Func<IReadOnlyList<string>, CancellationToken, Task<int>> _executeSqlAsync;

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
