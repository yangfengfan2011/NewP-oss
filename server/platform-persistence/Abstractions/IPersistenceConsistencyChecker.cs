namespace ICP.Platform.Persistence.Abstractions;

/// <summary>
/// 在 Host migration 完成后读取统一 history 与实际数据库结构，确认它们仍与当前候选计划一致。
/// </summary>
public interface IPersistenceConsistencyChecker
{
    /// <summary>
    /// 运行只读一致性检查。实现不得生成或执行 DDL，也不得写入 history 或业务数据。
    /// </summary>
    Task CheckAsync(CancellationToken cancellationToken = default);
}
