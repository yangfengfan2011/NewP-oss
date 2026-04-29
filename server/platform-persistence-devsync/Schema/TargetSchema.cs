namespace ICP.Platform.Persistence.DevSync.Schema;

/// <summary>
/// 从模型 authoring 真源投影出的目标结构视图；它是 diff 输入，不承载执行顺序、回滚或冲突处理策略。
/// </summary>
/// <param name="Tables">当前受管模型对应的目标表集合。</param>
public sealed record TargetSchema(IReadOnlyList<TargetTableSchema> Tables);
