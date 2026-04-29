namespace ICP.Platform.Persistence.DevSync.Planning;

/// <summary>
/// 结构同步操作类型常量，供计划生成器、风险分类器和 Host/CLI 输出保持一致。
/// </summary>
public static class SchemaOperationKind
{
    /// <summary>创建由平台元数据真源管理的新表。</summary>
    public const string CreateTable = nameof(CreateTable);

    /// <summary>删除数据库中已存在的表，属于需要人工确认的高风险操作。</summary>
    public const string DropTable = nameof(DropTable);

    /// <summary>向托管表新增列。</summary>
    public const string AddColumn = nameof(AddColumn);

    /// <summary>删除托管表中的列，属于需要人工确认的高风险操作。</summary>
    public const string DropColumn = nameof(DropColumn);

    /// <summary>表名变更候选操作，当前仅作为显式计划语义使用，不依赖隐式匹配。</summary>
    public const string RenameTable = nameof(RenameTable);

    /// <summary>列名变更候选操作，当前仅作为显式计划语义使用，不依赖隐式匹配。</summary>
    public const string RenameColumn = nameof(RenameColumn);

    /// <summary>列存储类型变更操作，可能影响数据兼容性。</summary>
    public const string AlterColumnType = nameof(AlterColumnType);

    /// <summary>列可空性变更操作，可能受现有数据约束影响。</summary>
    public const string AlterNullability = nameof(AlterNullability);
}

/// <summary>
/// 结构同步风险级别常量；协调器只允许 Safe 操作在显式确认后自动执行。
/// </summary>
public static class RiskLevel
{
    /// <summary>安全级操作，仍需调用方显式确认后才可执行。</summary>
    public const string Safe = nameof(Safe);

    /// <summary>需要人工审查的操作，开发态自动执行路径会阻断启动。</summary>
    public const string Confirm = nameof(Confirm);
}

/// <summary>
/// 单个结构同步操作的稳定边界模型，供计划输出、风险阻断和 SQL 预览复用。
/// </summary>
/// <param name="Kind">操作类型，取值来自 <see cref="SchemaOperationKind"/>。</param>
/// <param name="RiskLevel">风险级别，取值来自 <see cref="Planning.RiskLevel"/>。</param>
/// <param name="TableName">操作目标表名。</param>
/// <param name="ColumnName">操作目标列名；表级操作为空。</param>
/// <param name="FromName">重命名或迁移候选操作的来源名称；非此类操作为空。</param>
/// <param name="Description">面向开发者的操作说明，用于定位计划差异。</param>
/// <param name="SqlPreview">已生成的 SQL 预览；执行路径会把空预览视为失败信号。</param>
public sealed record SchemaOperation(
    string Kind,
    string RiskLevel,
    string TableName,
    string? ColumnName,
    string? FromName,
    string Description,
    IReadOnlyList<string> SqlPreview);
