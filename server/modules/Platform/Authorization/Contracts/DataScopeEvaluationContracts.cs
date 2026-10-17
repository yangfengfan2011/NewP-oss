namespace ICP.Platform.Authorization.Contracts;

/// <summary>一次逻辑权限操作共享服务端 UTC 观察时刻，不缓存身份、规则或授权结果。</summary>
public interface IDataScopeEvaluationScope
{
    /// <summary>读取已开启操作的观察时刻；SQL 表达式应捕获该值，而非延迟读取此属性。</summary>
    DateTimeOffset ObservedAt { get; }
    /// <summary>顶层开启操作，嵌套调用复用当前观察；Dispose 恢复调用前范围。</summary>
    IDisposable BeginOperation();
    /// <summary>仅在写入已成功提交后开启独立读取观察，内层复用新值；Dispose 恢复外层范围。</summary>
    IDisposable BeginReadAfterWrite();
}
