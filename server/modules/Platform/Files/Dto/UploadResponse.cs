namespace ICP.Platform.Modules.Files.Dto;

/// <summary>
/// 文件上传响应
/// </summary>
public class UploadResponse
{
    public required string FileId { get; init; }
    public required string FileName { get; init; }
    public long FileSize { get; init; }
    public required string ContentType { get; init; }
    public DateTime UploadedAt { get; init; }
}
