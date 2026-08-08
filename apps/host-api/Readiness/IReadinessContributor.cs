namespace ICP.Platform.HostApi.Readiness;

/// <summary>
/// 为 Host readiness 提供单个、可组合且无等待循环的阶段观察。
/// </summary>
public interface IReadinessContributor
{
    /// <summary>
    /// 获取稳定阶段名。
    /// </summary>
    string Name { get; }

    /// <summary>
    /// 获取聚合顺序；相同顺序按阶段名排序。
    /// </summary>
    int Order { get; }

    /// <summary>
    /// 获取该阶段是否阻塞整体 ready。
    /// </summary>
    bool IsRequired { get; }

    /// <summary>
    /// 执行一次当前状态观察；实现不得在此轮询等待状态变化。
    /// </summary>
    ValueTask<ReadinessContribution> CheckAsync(CancellationToken cancellationToken);
}

/// <summary>
/// 聚合已注册的 readiness contributors。
/// </summary>
public interface IReadinessEvaluator
{
    /// <summary>
    /// 对当前 contributors 执行一次有序快照检查。
    /// </summary>
    Task<ReadinessEvaluation> EvaluateAsync(CancellationToken cancellationToken);
}
