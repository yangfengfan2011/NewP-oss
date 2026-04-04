using System.Collections.Concurrent;
using ICP.Platform.Modules.Files.Abstractions;
using ICP.Platform.Modules.Files.Models;

namespace ICP.Platform.Modules.Files.Repositories;

/// <summary>
/// 内存文件元数据仓储（用于演示和测试）
/// </summary>
public class InMemoryFileMetadataRepository : IFileMetadataRepository
{
    private readonly ConcurrentDictionary<string, FileMetadata> _storage = new();

    public Task SaveAsync(FileMetadata metadata, CancellationToken cancellationToken = default)
    {
        _storage[metadata.FileId] = metadata;
        return Task.CompletedTask;
    }

    public Task<FileMetadata?> GetByIdAsync(string fileId, CancellationToken cancellationToken = default)
    {
        _storage.TryGetValue(fileId, out var metadata);
        return Task.FromResult(metadata);
    }

    public Task MarkAsDeletedAsync(string fileId, string? deletedBy, CancellationToken cancellationToken = default)
    {
        if (_storage.TryGetValue(fileId, out var metadata))
        {
            var updated = new FileMetadata
            {
                FileId = metadata.FileId,
                FileName = metadata.FileName,
                FileSize = metadata.FileSize,
                ContentType = metadata.ContentType,
                StoragePath = metadata.StoragePath,
                StorageProvider = metadata.StorageProvider,
                FileHash = metadata.FileHash,
                UploadedBy = metadata.UploadedBy,
                UploadedAt = metadata.UploadedAt,
                EntityType = metadata.EntityType,
                EntityId = metadata.EntityId,
                ExtendedProperties = metadata.ExtendedProperties,
                IsDeleted = true
            };
            _storage[fileId] = updated;
        }

        return Task.CompletedTask;
    }

    public Task DeleteAsync(string fileId, CancellationToken cancellationToken = default)
    {
        _storage.TryRemove(fileId, out _);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<FileMetadata>> QueryAsync(
        string? entityType,
        string? entityId,
        bool includeDeleted,
        CancellationToken cancellationToken = default)
    {
        var query = _storage.Values.AsEnumerable();

        if (!includeDeleted)
        {
            query = query.Where(m => !m.IsDeleted);
        }

        if (!string.IsNullOrEmpty(entityType))
        {
            query = query.Where(m => m.EntityType == entityType);
        }

        if (!string.IsNullOrEmpty(entityId))
        {
            query = query.Where(m => m.EntityId == entityId);
        }

        var result = query.OrderByDescending(m => m.UploadedAt).ToList();
        return Task.FromResult<IReadOnlyList<FileMetadata>>(result);
    }
}
