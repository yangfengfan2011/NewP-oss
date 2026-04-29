namespace ICP.Platform.Persistence.DevSync.Schema;

/// <summary>
/// 由模型文档投影出的目标表定义；表名冲突和 historyName 冲突由 diff 内核显式判断。
/// </summary>
/// <param name="ModuleId">模型所属模块 ID。</param>
/// <param name="Name">模型名称。</param>
/// <param name="TableName">目标数据库表名。</param>
/// <param name="HistoryName">旧表名；当新旧表同时存在时计划阶段会阻断。</param>
/// <param name="Columns">目标列集合。</param>
/// <param name="Indexes">目标索引集合；当前只作为后续扩展输入。</param>
public sealed record TargetTableSchema(
    string ModuleId,
    string Name,
    string TableName,
    string? HistoryName,
    IReadOnlyList<TargetColumnSchema> Columns,
    IReadOnlyList<TargetIndexSchema> Indexes);
