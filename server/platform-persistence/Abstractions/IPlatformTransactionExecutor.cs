namespace ICP.Platform.Persistence.Abstractions;

/// <summary>
/// 在共享平台持久化作用域内统一保存并提交跨上下文变化。
/// </summary>
public interface IPlatformTransactionExecutor
{
    /// <summary>
    /// 在一个数据库事务中执行参与者登记，统一调用一次保存并提交结果。
    /// 参与者不得自行保存或提交。
    /// </summary>
    ValueTask<TResult> ExecuteAsync<TResult>(
        Func<CancellationToken, ValueTask<TResult>> registerChanges,
        CancellationToken cancellationToken = default);
}
