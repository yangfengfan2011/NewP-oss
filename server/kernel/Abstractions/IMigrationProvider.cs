namespace ICP.Platform.Kernel.Abstractions;

/// <summary>
/// 迁移/导入声明契约
/// </summary>
public interface IMigrationProvider
{
    /// <summary>
    /// 获取模块支持的迁移任务
    /// </summary>
    IReadOnlyList<MigrationDescriptor> GetMigrations();
}

/// <summary>
/// 迁移描述符
/// </summary>
public record MigrationDescriptor
{
    /// <summary>
    /// 迁移任务 ID
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// 迁移任务显示名称
    /// </summary>
    public required string DisplayName { get; init; }

    /// <summary>
    /// 迁移描述
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// 源系统类型（如 "OldP", "External" 等）
    /// </summary>
    public required string SourceType { get; init; }

    /// <summary>
    /// 所属模块 ID
    /// </summary>
    public required string ModuleId { get; init; }

    /// <summary>
    /// 迁移处理器类型全名
    /// </summary>
    public required string HandlerType { get; init; }
}

/// <summary>
/// 迁移处理器接口
/// </summary>
public interface IMigrationHandler
{
    /// <summary>
    /// 执行迁移
    /// </summary>
    Task<MigrationResult> ExecuteAsync(MigrationContext context, CancellationToken cancellationToken = default);
}

/// <summary>
/// 迁移上下文
/// </summary>
public record MigrationContext
{
    /// <summary>
    /// 迁移任务 ID
    /// </summary>
    public required string MigrationId { get; init; }

    /// <summary>
    /// 源连接字符串或配置
    /// </summary>
    public required string SourceConnection { get; init; }

    /// <summary>
    /// 附加参数
    /// </summary>
    public Dictionary<string, object>? Parameters { get; init; }
}

/// <summary>
/// 迁移结果
/// </summary>
public record MigrationResult
{
    /// <summary>
    /// 是否成功
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// 处理的记录数
    /// </summary>
    public int ProcessedCount { get; init; }

    /// <summary>
    /// 错误消息
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// 详细日志
    /// </summary>
    public List<string>? Logs { get; init; }
}
