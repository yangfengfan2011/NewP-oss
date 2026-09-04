namespace ICP.Platform.Modeling.Definitions;

/// <summary>
/// 规范索引定义；IncludeNullValues 显式覆盖 provider 对 nullable unique index 的默认过滤。
/// </summary>
public sealed record IndexDefinition(
    string Name,
    IReadOnlyList<string> Fields,
    bool IsUnique,
    bool IncludeNullValues = false);
