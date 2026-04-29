namespace ICP.Platform.Persistence.DevSync.Schema;

/// <summary>
/// 由模型字段投影出的目标列定义；历史名称只用于显式重命名候选，不参与隐式覆盖。
/// </summary>
/// <param name="Name">模型字段名称。</param>
/// <param name="StoreName">目标数据库列名。</param>
/// <param name="HistoryName">旧列名；当新旧列同时存在时计划阶段会阻断。</param>
/// <param name="StoreType">目标存储类型。</param>
/// <param name="Required">模型契约是否要求非空。</param>
public sealed record TargetColumnSchema(
    string Name,
    string StoreName,
    string? HistoryName,
    string StoreType,
    bool Required);
