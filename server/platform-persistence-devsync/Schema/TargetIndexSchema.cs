namespace ICP.Platform.Persistence.DevSync.Schema;

/// <summary>
/// 由模型投影出的目标索引定义；当前任务只保留计划输入，索引执行策略仍需后续设计。
/// </summary>
/// <param name="Name">目标索引名称。</param>
/// <param name="Columns">索引包含的目标列名。</param>
/// <param name="IsUnique">是否唯一索引。</param>
public sealed record TargetIndexSchema(
    string Name,
    IReadOnlyList<string> Columns,
    bool IsUnique);
