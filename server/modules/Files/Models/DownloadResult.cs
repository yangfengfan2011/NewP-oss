namespace ICP.Platform.Modules.Files.Models;

/// <summary>
/// 文件下载结果
/// </summary>
public class DownloadResult
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
    /// 文件大小
    /// </summary>
    public long FileSize { get; init; }
}
