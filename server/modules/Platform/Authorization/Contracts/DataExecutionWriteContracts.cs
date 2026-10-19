namespace ICP.Platform.Authorization.Contracts;

/// <summary>服务端选定的单次写来源；资源和目标不可随业务字段替换，内部来源必须独立登记。</summary>
public sealed record DataExecutionWriteContext(DataExecutionBindingReference Binding, string ResourceKey, string? ObjectId)
{
    public DataExecutionInternalBinding? Internal { get; init; }
}

/// <summary>对象owner在原保护时点取得的真实旧事实；不是已允许证明，也不能替代现有对象裁决。</summary>
public sealed record DataExecutionWriteFacts(string ResourceKey, string ObjectId, IReadOnlyDictionary<string, object?> Values);
