namespace ICP.Platform.Persistence.Abstractions;

/// <summary>
/// 在共享平台持久化作用域内统一保存并提交跨上下文变化。
/// </summary>
public interface IPlatformTransactionExecutor
{
    /// <summary>
    /// 在一个数据库事务中执行参与者登记，统一调用一次保存并提交结果。
    /// 参与者可保存但不得独立提交，缓存由最终提交统一发布。
    /// </summary>
    ValueTask<TResult> ExecuteAsync<TResult>(
        Func<CancellationToken, ValueTask<TResult>> registerChanges,
        CancellationToken cancellationToken = default);
    /// <summary>标识当前执行流是否已经处于统一事务内。</summary>
    bool IsActive => false;
    /// <summary>查询模型是否有需要提前协调的提交参与者。</summary>
    bool HasParticipants(string modelKey) => false;
    /// <summary>声明本次可能影响的模型，在业务写入前取得全部对应协调权。</summary>
    ValueTask<TResult> ExecuteAsync<TResult>(IReadOnlyCollection<string> modelKeys,
        Func<CancellationToken, ValueTask<TResult>> registerChanges, CancellationToken cancellationToken = default) =>
        ExecuteAsync(registerChanges, cancellationToken);
    /// <summary>特殊数据库操作在当前事务内登记实际变更，绝不立即发布。</summary>
    void RegisterChange(ICP.Platform.Persistence.Transactions.PlatformRecordChange change) =>
        throw new InvalidOperationException("Transaction change registration is unavailable.");
}
