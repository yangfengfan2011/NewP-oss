namespace ICP.Platform.Authorization.Contracts;

/// <summary>对象owner在原保护时点取得的真实旧事实；不是已允许证明，也不能替代现有对象裁决。</summary>
public sealed record DataExecutionWriteFacts(string ResourceKey, string ObjectId, IReadOnlyDictionary<string, object?> Values);
