using System.Security.Cryptography;
using System.Text.Json;
using ICP.Platform.Modules.Files.Abstractions;
using ICP.Platform.Modules.Files.Models;

namespace ICP.Platform.Modules.Files.Services;

/// <summary>
/// 文件服务实现
/// </summary>
public class FileService : IFileService
{
    private readonly IStorageProvider _storageProvider;
    private readonly IFileMetadataRepository _metadataRepository;

    public FileService(IStorageProvider storageProvider, IFileMetadataRepository metadataRepository)
    {
        _storageProvider = storageProvider ?? throw new ArgumentNullException(nameof(storageProvider));
        _metadataRepository = metadataRepository ?? throw new ArgumentNullException(nameof(metadataRepository));
    }

    public async Task<FileMetadata> UploadAsync(UploadRequest request, CancellationToken cancellationToken = default)
    {
        if (request.FileStream == null || !request.FileStream.CanRead)
        {
            throw new ArgumentException("无效的文件流", nameof(request));
        }

        var fileId = Guid.NewGuid().ToString("N");
        var fileHash = await ComputeHashAsync(request.FileStream, cancellationToken);
        request.FileStream.Position = 0;

        var storagePath = await _storageProvider.SaveAsync(request.FileStream, request.FileName, cancellationToken);

        var metadata = new FileMetadata
        {
            FileId = fileId,
            FileName = request.FileName,
            FileSize = request.FileStream.Length,
            ContentType = request.ContentType,
            StoragePath = storagePath,
            StorageProvider = _storageProvider.ProviderName,
            FileHash = fileHash,
            UploadedBy = request.UploadedBy,
            UploadedAt = DateTime.UtcNow,
            EntityType = request.EntityType,
            EntityId = request.EntityId,
            ExtendedProperties = request.ExtendedProperties != null
                ? JsonSerializer.Serialize(request.ExtendedProperties)
                : null,
            IsDeleted = false
        };

        await _metadataRepository.SaveAsync(metadata, cancellationToken);

        return metadata;
    }

    public async Task<DownloadResult> DownloadAsync(string fileId, CancellationToken cancellationToken = default)
    {
        var metadata = await _metadataRepository.GetByIdAsync(fileId, cancellationToken);

        if (metadata == null || metadata.IsDeleted)
        {
            throw new FileNotFoundException($"文件不存在: {fileId}");
        }

        var fileStream = await _storageProvider.ReadAsync(metadata.StoragePath, cancellationToken);

        return new DownloadResult
        {
            FileStream = fileStream,
            FileName = metadata.FileName,
            ContentType = metadata.ContentType,
            FileSize = metadata.FileSize
        };
    }

    public async Task<FileMetadata?> GetMetadataAsync(string fileId, CancellationToken cancellationToken = default)
    {
        return await _metadataRepository.GetByIdAsync(fileId, cancellationToken);
    }

    public async Task<bool> DeleteAsync(string fileId, string? deletedBy = null, CancellationToken cancellationToken = default)
    {
        var metadata = await _metadataRepository.GetByIdAsync(fileId, cancellationToken);

        if (metadata == null)
        {
            return false;
        }

        await _metadataRepository.MarkAsDeletedAsync(fileId, deletedBy, cancellationToken);
        return true;
    }

    public async Task<bool> PermanentDeleteAsync(string fileId, CancellationToken cancellationToken = default)
    {
        var metadata = await _metadataRepository.GetByIdAsync(fileId, cancellationToken);

        if (metadata == null)
        {
            return false;
        }

        await _storageProvider.DeleteAsync(metadata.StoragePath, cancellationToken);
        await _metadataRepository.DeleteAsync(fileId, cancellationToken);

        return true;
    }

    public async Task<IReadOnlyList<FileMetadata>> QueryAsync(
        string? entityType = null,
        string? entityId = null,
        bool includeDeleted = false,
        CancellationToken cancellationToken = default)
    {
        return await _metadataRepository.QueryAsync(entityType, entityId, includeDeleted, cancellationToken);
    }

    private static async Task<string> ComputeHashAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var sha256 = SHA256.Create();
        var hashBytes = await sha256.ComputeHashAsync(stream, cancellationToken);
        return Convert.ToHexString(hashBytes);
    }
}
