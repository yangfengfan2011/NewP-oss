namespace ICP.Platform.Modules.Files.Models;

/// <summary>
/// 文件元数据
/// </summary>
public class FileMetadata
{
    /// <summary>
    /// 文件唯一标识
    /// </summary>
    public required string FileId { get; init; }

    /// <summary>
    /// 原始文件名
    /// </summary>
    public required string FileName { get; init; }

    /// <summary>
    /// 文件大小（字节）
    /// </summary>
    public long FileSize { get; init; }

    /// <summary>
    /// 内容类型（MIME）
    /// </summary>
    public required string ContentType { get; init; }

    /// <summary>
    /// 存储路径或键
    /// </summary>
    public required string StoragePath { get; init; }

    /// <summary>
    /// 存储提供者标识
    /// </summary>
    public required string StorageProvider { get; init; }

    /// <summary>
    /// 文件哈希（用于去重和完整性校验）
    /// </summary>
    public string? FileHash { get; init; }

    /// <summary>
    /// 上传者用户 ID
    /// </summary>
    public string? UploadedBy { get; init; }

    /// <summary>
    /// 上传时间
    /// </summary>
    public DateTime UploadedAt { get; init; }

    /// <summary>
    /// 关联业务实体类型（可选）
    /// </summary>
    public string? EntityType { get; init; }

    /// <summary>
    /// 关联业务实体 ID（可选）
    /// </summary>
    public string? EntityId { get; init; }

    /// <summary>
    /// 扩展属性（JSON）
    /// </summary>
    public string? ExtendedProperties { get; init; }

    /// <summary>
    /// 是否已删除（软删除）
    /// </summary>
    public bool IsDeleted { get; init; }
}
