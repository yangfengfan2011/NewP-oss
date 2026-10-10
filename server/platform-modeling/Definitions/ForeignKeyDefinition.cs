namespace ICP.Platform.Modeling.Definitions;

/// <summary>
/// 外键删除行为的稳定运行时投影。
/// </summary>
public enum ForeignKeyDeleteBehavior
{
    Restrict,
    Cascade,
    SetNull,
    NoAction
}

/// <summary>
/// 一个模型拥有的显式外键定义。
/// </summary>
public sealed record ForeignKeyDefinition(
    string Name,
    IReadOnlyList<string> Fields,
    string PrincipalModelKey,
    IReadOnlyList<string> PrincipalFields,
    ForeignKeyDeleteBehavior DeleteBehavior)
{
    /// <summary>显式 principalModuleId 的独立归属约束；缺省时按全局目录解析实际 owner。</summary>
    public string? PrincipalOwnerModuleId { get; init; }
}
