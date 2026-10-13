using ICP.Platform.Modules.Files.Models;

namespace ICP.Platform.Modules.Files.Abstractions;

/// <summary>
/// 存储提供者抽象接口
/// </summary>
public interface IStorageProvider
{
    /// <summary>
    /// 提供者名称
    /// </summary>
    string ProviderName { get; }

    /// <summary>
    /// 保存文件
    /// </summary>
    Task<string> SaveAsync(Stream fileStream, string fileName, CancellationToken cancellationToken = default);

    /// <summary>
    /// 读取文件
    /// </summary>
    Task<Stream> ReadAsync(string storagePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// 删除文件
    /// </summary>
    Task DeleteAsync(string storagePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// 检查文件是否存在
    /// </summary>
    Task<bool> ExistsAsync(string storagePath, CancellationToken cancellationToken = default);
}
