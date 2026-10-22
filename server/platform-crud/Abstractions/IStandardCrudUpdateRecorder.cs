using ICP.Platform.Crud.Descriptors;

namespace ICP.Platform.Crud.Abstractions;

/// <summary>资源拥有者从事务保护的真实旧值与规范新值中登记变化。</summary>
public interface IStandardCrudUpdateRecorder
{
    string ResourceKey { get; }

    /// <summary>登记本次实际替换的业务差异；无变化不登记，不自行保存。</summary>
    ValueTask RegisterAsync(IReadOnlyDictionary<string, object?> before,
        IReadOnlyDictionary<string, object?> after, CancellationToken cancellationToken = default);
}

/// <summary>更新参与者在外层事务中保护旧值并跟踪新值，保存与提交仍归唯一执行器。</summary>
public interface IStandardCrudUpdateParticipant
{
    /// <summary>读取本次实际替换的旧值，并保持写入互斥直到当前事务结束；不存在时返回 null。</summary>
    Task<IReadOnlyDictionary<string, object?>?> ReadForUpdateAsync(StandardCrudDescriptor descriptor,
        string id, CancellationToken cancellationToken = default);

    /// <summary>仅跟踪已保护行的规范更新并返回新值，不保存。</summary>
    Task<IReadOnlyDictionary<string, object?>> RegisterUpdateAsync(StandardCrudDescriptor descriptor,
        string id, IReadOnlyDictionary<string, object?> values, CancellationToken cancellationToken = default);
}
