namespace ICP.Platform.Modules.Files.Dto;

/// <summary>
/// 文件信息响应
/// </summary>
public class FileInfoResponse
{
    public required string FileId { get; init; }
    public required string FileName { get; init; }
    public long FileSize { get; init; }
    public required string ContentType { get; init; }
    public string? UploadedBy { get; init; }
    public DateTime UploadedAt { get; init; }
    public string? EntityType { get; init; }
    public string? EntityId { get; init; }
    public bool IsDeleted { get; init; }
}
