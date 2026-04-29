namespace ICP.Platform.Persistence.DevSync.Planning;

/// <summary>
/// 结构同步计划摘要，供 Host/CLI 在小上下文中判断计划规模、风险分布和未托管对象数量。
/// </summary>
/// <param name="ManagedTableCount">由平台元数据真源管理的目标表数量。</param>
/// <param name="OperationCount">本次计划包含的结构操作总数。</param>
/// <param name="SafeOperationCount">可在显式确认后自动执行的安全操作数量。</param>
/// <param name="ConfirmOperationCount">需要人工审查且会阻断自动执行的高风险操作数量。</param>
/// <param name="UnmanagedTableCount">数据库中存在但不归当前平台元数据真源管理的表数量。</param>
public sealed record SchemaPlanSummary(
    int ManagedTableCount,
    int OperationCount,
    int SafeOperationCount,
    int ConfirmOperationCount,
    int UnmanagedTableCount);

/// <summary>
/// Host/CLI 边界使用的稳定结构同步计划视图。
/// 该类型只描述当前 diff 结果和可输出的 SQL 预览，不承载执行、回滚或冲突处理流程。
/// </summary>
/// <param name="Summary">计划摘要，用于快速判断规模和风险。</param>
/// <param name="ManagedObjects">由平台元数据真源管理的对象名称列表。</param>
/// <param name="Operations">需要执行或人工审查的结构操作列表。</param>
/// <param name="UnmanagedTables">数据库中未被当前真源接管的表列表，调用方不应隐式迁移或删除这些表。</param>
public sealed record SchemaPlan(
    SchemaPlanSummary Summary,
    IReadOnlyList<string> ManagedObjects,
    IReadOnlyList<SchemaOperation> Operations,
    IReadOnlyList<string> UnmanagedTables)
{
    /// <summary>
    /// 预计划阻断时使用的稳定空计划，避免 Host/CLI JSON envelope 出现 nullable plan 形态。
    /// </summary>
    public static SchemaPlan Empty { get; } = new(
        new SchemaPlanSummary(0, 0, 0, 0, 0),
        [],
        [],
        []);
}
