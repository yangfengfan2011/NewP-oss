using System.Buffers;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using ICP.Platform.Modules.Files.Abstractions;
using ICP.Platform.Modules.Files.Contracts;

namespace ICP.Platform.Modules.Files.Storage;

/// <summary>单 Host 的私有本地存储；服务器键是扁平身份，不能携带客户路径。</summary>
public sealed partial class LocalStorageProvider : IStorageProvider, IFileContentStore
{
    private readonly string _basePath;
    public string ProviderName => "Local";

    /// <summary>要求显式绝对根，并拒绝根及其祖先中的链接逃逸。</summary>
    public LocalStorageProvider(string basePath)
    {
        _basePath = ValidateRootPath(basePath);
        EnsureRoot();
    }

    /// <inheritdoc/>
    public async Task<FileContentMeasurement> WritePartialAsync(string key, Stream source, long maximumBytes, CancellationToken token)
    {
        if (!source.CanRead) throw new ArgumentException("Input stream must be readable.", nameof(source));
        if (maximumBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        var path = Resolve(key, partial: true);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = ArrayPool<byte>.Shared.Rent(65536);
        try
        {
            await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            long count = 0;
            while (true)
            {
                var read = await source.ReadAsync(buffer.AsMemory(0, 65536), token);
                if (read == 0) break;
                if (read > maximumBytes - count) throw new FileLifecycleException(413, "Files.UploadTooLarge");
                count += read;
                hash.AppendData(buffer, 0, read);
                await output.WriteAsync(buffer.AsMemory(0, read), token);
            }
            if (count == 0) throw new FileLifecycleException(400, "Files.EmptyContent");
            await output.FlushAsync(token);
            return new(count, Convert.ToHexString(hash.GetHashAndReset()));
        }
        finally { ArrayPool<byte>.Shared.Return(buffer); }
    }

    /// <inheritdoc/>
    public Task CompleteAsync(string key, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        File.Move(Resolve(key, partial: true), Resolve(key), overwrite: false);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<Stream> OpenAsync(string key, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        Stream stream = new FileStream(Resolve(key), FileMode.Open, FileAccess.Read, FileShare.Read, 65536,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Task.FromResult(stream);
    }

    /// <inheritdoc/>
    public Task DeleteContentAsync(string key, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        File.Delete(Resolve(key, partial: true));
        File.Delete(Resolve(key));
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task<string> SaveAsync(Stream fileStream, string fileName, CancellationToken cancellationToken = default)
    {
        var key = Guid.NewGuid().ToString("N");
        await WritePartialAsync(key, fileStream, long.MaxValue, cancellationToken);
        await CompleteAsync(key, cancellationToken);
        return key;
    }

    /// <inheritdoc/>
    public Task<Stream> ReadAsync(string storagePath, CancellationToken cancellationToken = default) => OpenAsync(storagePath, cancellationToken);
    /// <inheritdoc/>
    public Task DeleteAsync(string storagePath, CancellationToken cancellationToken = default) => DeleteContentAsync(storagePath, cancellationToken);
    /// <inheritdoc/>
    public Task<bool> ExistsAsync(string storagePath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(File.Exists(Resolve(storagePath)));
    }

    // 每次操作重新核对根/叶链接；路径不是通过业务文件名拼接产生。
    private string Resolve(string key, bool partial = false)
    {
        if (!StorageKeyPattern().IsMatch(key)) throw new ArgumentException("Invalid private storage key.", nameof(key));
        EnsureRoot();
        var path = Path.GetFullPath(Path.Combine(_basePath, key + (partial ? ".part" : "")));
        if (!path.StartsWith(_basePath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new ArgumentException("Storage path escaped its private root.", nameof(key));
        RejectLink(new FileInfo(path));
        return path;
    }

    // 根及祖先任一 reparse point 都不是受支持的私有存储位置。
    private void EnsureRoot()
    {
        ValidateRootPath(_basePath);
        Directory.CreateDirectory(_basePath);
        ValidateRootPath(_basePath);
    }

    // Options 启动校验共享只读路径验证，不能因校验创建或改写私有根/其祖先。
    internal static string ValidateRootPath(string basePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(basePath);
        if (!Path.IsPathFullyQualified(basePath)) throw new ArgumentException("Storage root must be absolute.", nameof(basePath));
        var root = Path.GetFullPath(basePath);
        if (root.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(segment => segment.Equals("wwwroot", StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Storage root cannot be under wwwroot.", nameof(basePath));
        for (var directory = new DirectoryInfo(root); directory is not null; directory = directory.Parent) RejectLink(directory);
        return root;
    }

    // 兼容 dangling symlink；不存在的普通路径无需事先创建。
    private static void RejectLink(FileSystemInfo info)
    {
        if (info.LinkTarget is not null || (info.Exists && (info.Attributes & FileAttributes.ReparsePoint) != 0))
            throw new IOException("Files storage cannot traverse a filesystem link.");
    }

    // 已知键由服务端生成，拒绝目录、扩展和大小写变体。
    [GeneratedRegex("^[0-9a-f]{32}$", RegexOptions.CultureInvariant)]
    private static partial Regex StorageKeyPattern();
}
