namespace ICP.Platform.Persistence.DevSync.Database;

/// <summary>
/// 数据库真实列快照，只承载结构 diff 所需的最小字段，不保存默认值、索引或执行流程。
/// </summary>
/// <param name="Name">数据库列名。</param>
/// <param name="StoreType">数据库报告的存储类型；类型等价折叠由计划内核统一处理。</param>
/// <param name="IsNullable">数据库列当前是否允许 NULL。</param>
public sealed record DatabaseColumnSchema(
    string Name,
    string StoreType,
    bool IsNullable);
