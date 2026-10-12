using ICP.Platform.Modeling.Metadata;
using System.Security.Cryptography;
using System.Text;

namespace ICP.Platform.Modules.MetadataConfigCenter.Storage;

/// <summary>
/// 元数据存储访问入口。
/// 这里统一解析仓库 authoring 根和 runtime 投影根，避免调用方各自拼路径导致边界漂移。
/// </summary>
public class ModuleMetadataStore
{
    private readonly string _repositoryRoot;
    private readonly string? _runtimeRoot;

    public ModuleMetadataStore()
        : this(ResolveRepositoryRoot(), ResolveRuntimeRoot())
    {
    }

    public ModuleMetadataStore(string repositoryRoot, string? runtimeRoot = null)
    {
        _repositoryRoot = Path.GetFullPath(repositoryRoot);
        _runtimeRoot = string.IsNullOrWhiteSpace(runtimeRoot)
            ? null
            : Path.GetFullPath(runtimeRoot);
    }

    // 读取当前可见 metadata，保持 runtime 优先与仓库新根回退。
    public string ResolveMetadataDirectory(string moduleId, string sourcePath)
    {
        // 读取时优先命中 runtime 根，再回退到仓库根；
        // 这样调用方拿到的是“当前可见元数据”，而不是固定绑死某一个物理目录。
        var effectiveRoot = ResolvePreferredMetadataRoot(moduleId, sourcePath) ?? _repositoryRoot;
        return Path.Combine(ModuleAuthoringPaths.ResolveModuleRoot(effectiveRoot, moduleId), "metadata", sourcePath);
    }

    public string ResolveMetadataFilePath(string moduleId, string sourcePath, string sourceFile)
    {
        return Path.Combine(ResolveMetadataDirectory(moduleId, sourcePath), sourceFile);
    }

    public string ResolveMetadataRelativeFilePath(string moduleId, string relativeFilePath)
    {
        foreach (var root in EnumerateMetadataRoots())
        {
            var candidatePath = ResolveMetadataRelativePath(root, moduleId, relativeFilePath);
            if (File.Exists(candidatePath))
            {
                return candidatePath;
            }
        }

        return ResolveMetadataRelativePath(
            ResolvePreferredMetadataRoot(moduleId, string.Empty) ?? _repositoryRoot,
            moduleId,
            relativeFilePath);
    }

    // 写入优先回到仓库实际 authoring 根。
    public string ResolveAuthoringMetadataDirectory(string moduleId, string sourcePath)
    {
        // authoring 写入默认优先回到仓库根；只有仓库根不存在该模块元数据时，
        // 才允许以 runtime 根作为现存来源做兼容读取/写入。
        var effectiveRoot = ResolvePreferredAuthoringRoot(moduleId, sourcePath);
        return Path.Combine(ModuleAuthoringPaths.ResolveModuleRoot(effectiveRoot, moduleId), "metadata", sourcePath);
    }

    public string ResolvePlatformMetadataFilePath(string fileName)
    {
        return Path.Combine(_repositoryRoot, "server", "platform", "metadata", fileName);
    }

    public string ResolvePlatformMetadataDirectory()
    {
        return Path.Combine(_repositoryRoot, "server", "platform", "metadata");
    }

    public string ResolveAuthoringMetadataFilePath(string moduleId, string sourcePath, string sourceFile)
    {
        return Path.Combine(ResolveAuthoringMetadataDirectory(moduleId, sourcePath), sourceFile);
    }

    public string ResolveAuthoringMetadataRelativeFilePath(string moduleId, string relativeFilePath)
    {
        return ResolveMetadataRelativePath(
            ResolvePreferredAuthoringRoot(moduleId, string.Empty),
            moduleId,
            relativeFilePath);
    }

    // 定位运行时输出中的单个 metadata 文件。
    public string? ResolveRuntimeMetadataFilePath(string moduleId, string sourcePath, string sourceFile)
    {
        if (string.IsNullOrWhiteSpace(_runtimeRoot))
        {
            return null;
        }

        return Path.Combine(ModuleAuthoringPaths.ResolveModuleRoot(_runtimeRoot, moduleId), "metadata", sourcePath, sourceFile);
    }

    public string? ResolveRuntimeMetadataRelativeFilePath(string moduleId, string relativeFilePath)
    {
        if (string.IsNullOrWhiteSpace(_runtimeRoot))
        {
            return null;
        }

        return ResolveMetadataRelativePath(_runtimeRoot, moduleId, relativeFilePath);
    }

    // 定位按模块身份组织的运行时 metadata 输出。
    public string? ResolveRuntimeMetadataDirectory(string moduleId, string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(_runtimeRoot))
        {
            return null;
        }

        return Path.Combine(ModuleAuthoringPaths.ResolveModuleRoot(_runtimeRoot, moduleId), "metadata", sourcePath);
    }

    public IReadOnlyList<string> EnumerateMetadataFiles(string moduleId, string sourcePath, string searchPattern)
    {
        var directory = ResolveMetadataDirectory(moduleId, sourcePath);
        if (!Directory.Exists(directory))
        {
            return [];
        }

        return Directory
            .EnumerateFiles(directory, searchPattern, SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public Task<string> ReadTextAsync(string filePath, CancellationToken cancellationToken = default)
    {
        return File.ReadAllTextAsync(filePath, cancellationToken);
    }

    public string ComputeVersionSummary(string content)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(hash);
    }

    public async Task WriteTextAtomicallyAsync(string filePath, string content, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException($"Unable to resolve directory for {filePath}.");
        }

        Directory.CreateDirectory(directory);

        // 写入采用临时文件 + move，避免调用方在写一半时读到截断内容。
        var tempFile = Path.Combine(directory, $"{Path.GetFileName(filePath)}.{Guid.NewGuid():N}.tmp");
        await File.WriteAllTextAsync(tempFile, content, cancellationToken);
        File.Move(tempFile, filePath, overwrite: true);
    }

    private static string ResolveRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "ICP-Platform.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Unable to locate repository root for metadata storage.");
    }

    // 按 runtime 与 repository 顺序选择实际包含 metadata 的根。
    private string? ResolvePreferredMetadataRoot(string moduleId, string sourcePath)
    {
        foreach (var root in EnumerateMetadataRoots())
        {
            var sourceDirectory = Path.Combine(ModuleAuthoringPaths.ResolveModuleRoot(root, moduleId), "metadata", sourcePath);
            if (Directory.Exists(sourceDirectory))
            {
                return root;
            }
        }

        foreach (var root in EnumerateMetadataRoots())
        {
            var metadataDirectory = Path.Combine(ModuleAuthoringPaths.ResolveModuleRoot(root, moduleId), "metadata");
            if (Directory.Exists(metadataDirectory))
            {
                return root;
            }
        }

        return null;
    }

    private string ResolvePreferredAuthoringRoot(string moduleId, string sourcePath)
    {
        if (MetadataPathExists(_repositoryRoot, moduleId, sourcePath) || MetadataRootExists(_repositoryRoot, moduleId))
        {
            return _repositoryRoot;
        }

        // 这里保留对 runtime 根的兼容兜底，目的是让尚未回迁到仓库真源的历史数据仍可被编辑，
        // 但默认真源仍然应该逐步回到 repository root。
        if (!string.IsNullOrWhiteSpace(_runtimeRoot)
            && (MetadataPathExists(_runtimeRoot, moduleId, sourcePath) || MetadataRootExists(_runtimeRoot, moduleId)))
        {
            return _runtimeRoot;
        }

        return _repositoryRoot;
    }

    // 检查指定根下的真实模块 metadata 路径。
    private static bool MetadataPathExists(string root, string moduleId, string sourcePath)
    {
        return Directory.Exists(Path.Combine(ModuleAuthoringPaths.ResolveModuleRoot(root, moduleId), "metadata", sourcePath));
    }

    // 检查指定根是否包含该模块的 metadata。
    private static bool MetadataRootExists(string root, string moduleId)
    {
        return Directory.Exists(Path.Combine(ModuleAuthoringPaths.ResolveModuleRoot(root, moduleId), "metadata"));
    }

    // 在模块 metadata 边界内解析相对路径，拒绝越界。
    private static string ResolveMetadataRelativePath(string root, string moduleId, string relativeFilePath)
    {
        if (Path.IsPathRooted(relativeFilePath))
        {
            throw new InvalidOperationException($"Metadata relative file path must not be rooted: {relativeFilePath}");
        }

        var metadataRoot = Path.GetFullPath(Path.Combine(ModuleAuthoringPaths.ResolveModuleRoot(root, moduleId), "metadata"));
        var candidatePath = Path.GetFullPath(Path.Combine(metadataRoot, relativeFilePath));
        if (!candidatePath.StartsWith(metadataRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(candidatePath, metadataRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Metadata relative file path resolves outside metadata root: {relativeFilePath}");
        }

        return candidatePath;
    }

    private IEnumerable<string> EnumerateMetadataRoots()
    {
        if (!string.IsNullOrWhiteSpace(_runtimeRoot))
        {
            yield return _runtimeRoot;
        }

        yield return _repositoryRoot;
    }

    private static string ResolveRuntimeRoot()
    {
        var configuredInstallRoot = Environment.GetEnvironmentVariable("Platform__PluginInstallRoot");
        var installRoot = string.IsNullOrWhiteSpace(configuredInstallRoot)
            ? Path.Combine(AppContext.BaseDirectory, "plugins")
            : Path.IsPathRooted(configuredInstallRoot)
                ? configuredInstallRoot
                : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, configuredInstallRoot));

        return Path.Combine(installRoot, "runtime");
    }
}
