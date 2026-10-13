namespace ICP.Platform.Modules.Files.Models;

/// <summary>
/// 文件上传请求
/// </summary>
public class UploadRequest
{
    /// <summary>
    /// 文件流
    /// </summary>
    public required Stream FileStream { get; init; }

    /// <summary>
    /// 文件名
    /// </summary>
    public required string FileName { get; init; }

    /// <summary>
    /// 内容类型
    /// </summary>
    public required string ContentType { get; init; }

    /// <summary>
    /// 关联业务实体类型（可选）
    /// </summary>
    public string? EntityType { get; init; }

    /// <summary>
    /// 关联业务实体 ID（可选）
    /// </summary>
    public string? EntityId { get; init; }

    /// <summary>
    /// 上传者用户 ID
    /// </summary>
    public string? UploadedBy { get; init; }

    /// <summary>
    /// 扩展属性
    /// </summary>
    public Dictionary<string, string>? ExtendedProperties { get; init; }
}
