using ICP.Platform.Modules.Files.Models;

namespace ICP.Platform.Modules.Files.Abstractions;

/// <summary>
/// 文件元数据仓储接口
/// </summary>
public interface IFileMetadataRepository
{
    /// <summary>
    /// 保存文件元数据
    /// </summary>
    Task SaveAsync(FileMetadata metadata, CancellationToken cancellationToken = default);

    /// <summary>
    /// 根据 ID 获取元数据
    /// </summary>
    Task<FileMetadata?> GetByIdAsync(string fileId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 标记为已删除
    /// </summary>
    Task MarkAsDeletedAsync(string fileId, string? deletedBy, CancellationToken cancellationToken = default);

    /// <summary>
    /// 物理删除元数据
    /// </summary>
    Task DeleteAsync(string fileId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 查询文件列表
    /// </summary>
    Task<IReadOnlyList<FileMetadata>> QueryAsync(string? entityType, string? entityId, bool includeDeleted, CancellationToken cancellationToken = default);
}
