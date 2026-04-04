using System.Security.Cryptography;
using ICP.Platform.Modules.Files.Abstractions;

namespace ICP.Platform.Modules.Files.Storage;

/// <summary>
/// 本地文件系统存储提供者
/// </summary>
public class LocalStorageProvider : IStorageProvider
{
    private readonly string _basePath;

    public string ProviderName => "Local";

    public LocalStorageProvider(string basePath)
    {
        _basePath = basePath ?? throw new ArgumentNullException(nameof(basePath));

        if (!Directory.Exists(_basePath))
        {
            Directory.CreateDirectory(_basePath);
        }
    }

    public async Task<string> SaveAsync(Stream fileStream, string fileName, CancellationToken cancellationToken = default)
    {
        var fileId = Guid.NewGuid().ToString("N");
        var datePath = DateTime.UtcNow.ToString("yyyy/MM/dd");
        var directory = Path.Combine(_basePath, datePath);

        if (!Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var extension = Path.GetExtension(fileName);
        var storagePath = Path.Combine(datePath, $"{fileId}{extension}");
        var fullPath = Path.Combine(_basePath, storagePath);

        using var fileStreamOut = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true);
        await fileStream.CopyToAsync(fileStreamOut, cancellationToken);

        return storagePath;
    }

    public Task<Stream> ReadAsync(string storagePath, CancellationToken cancellationToken = default)
    {
        var fullPath = Path.Combine(_basePath, storagePath);

        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"文件不存在: {storagePath}");
        }

        Stream stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
        return Task.FromResult(stream);
    }

    public Task DeleteAsync(string storagePath, CancellationToken cancellationToken = default)
    {
        var fullPath = Path.Combine(_basePath, storagePath);

        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
        }

        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string storagePath, CancellationToken cancellationToken = default)
    {
        var fullPath = Path.Combine(_basePath, storagePath);
        return Task.FromResult(File.Exists(fullPath));
    }
}
