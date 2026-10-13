using ICP.Platform.Modules.Files.Models;

namespace ICP.Platform.Modules.Files.Abstractions;

/// <summary>
/// 文件服务接口
/// </summary>
public interface IFileService
{
    /// <summary>
    /// 上传文件
    /// </summary>
    Task<FileMetadata> UploadAsync(UploadRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// 下载文件
    /// </summary>
    Task<DownloadResult> DownloadAsync(string fileId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取文件元数据
    /// </summary>
    Task<FileMetadata?> GetMetadataAsync(string fileId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 删除文件（软删除）
    /// </summary>
    Task<bool> DeleteAsync(string fileId, string? deletedBy = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 物理删除文件
    /// </summary>
    Task<bool> PermanentDeleteAsync(string fileId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 查询文件列表
    /// </summary>
    Task<IReadOnlyList<FileMetadata>> QueryAsync(string? entityType = null, string? entityId = null, bool includeDeleted = false, CancellationToken cancellationToken = default);
}
