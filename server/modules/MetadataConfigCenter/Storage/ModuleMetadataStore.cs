using System.Security.Cryptography;
using System.Text;

namespace ICP.Platform.Modules.MetadataConfigCenter.Storage;

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

    public string ResolveMetadataDirectory(string moduleId, string sourcePath)
    {
        var effectiveRoot = ResolvePreferredMetadataRoot(moduleId, sourcePath) ?? _repositoryRoot;
        return Path.Combine(effectiveRoot, "modules", moduleId, "metadata", sourcePath);
    }

    public string ResolveMetadataFilePath(string moduleId, string sourcePath, string sourceFile)
    {
        return Path.Combine(ResolveMetadataDirectory(moduleId, sourcePath), sourceFile);
    }

    public string ResolveAuthoringMetadataDirectory(string moduleId, string sourcePath)
    {
        var effectiveRoot = ResolvePreferredAuthoringRoot(moduleId, sourcePath);
        return Path.Combine(effectiveRoot, "modules", moduleId, "metadata", sourcePath);
    }

    public string ResolveAuthoringMetadataFilePath(string moduleId, string sourcePath, string sourceFile)
    {
        return Path.Combine(ResolveAuthoringMetadataDirectory(moduleId, sourcePath), sourceFile);
    }

    public string? ResolveRuntimeMetadataFilePath(string moduleId, string sourcePath, string sourceFile)
    {
        if (string.IsNullOrWhiteSpace(_runtimeRoot))
        {
            return null;
        }

        return Path.Combine(_runtimeRoot, "modules", moduleId, "metadata", sourcePath, sourceFile);
    }

    public string? ResolveRuntimeMetadataDirectory(string moduleId, string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(_runtimeRoot))
        {
            return null;
        }

        return Path.Combine(_runtimeRoot, "modules", moduleId, "metadata", sourcePath);
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

    private string? ResolvePreferredMetadataRoot(string moduleId, string sourcePath)
    {
        foreach (var root in EnumerateMetadataRoots())
        {
            var sourceDirectory = Path.Combine(root, "modules", moduleId, "metadata", sourcePath);
            if (Directory.Exists(sourceDirectory))
            {
                return root;
            }
        }

        foreach (var root in EnumerateMetadataRoots())
        {
            var metadataDirectory = Path.Combine(root, "modules", moduleId, "metadata");
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

        if (!string.IsNullOrWhiteSpace(_runtimeRoot)
            && (MetadataPathExists(_runtimeRoot, moduleId, sourcePath) || MetadataRootExists(_runtimeRoot, moduleId)))
        {
            return _runtimeRoot;
        }

        return _repositoryRoot;
    }

    private static bool MetadataPathExists(string root, string moduleId, string sourcePath)
    {
        return Directory.Exists(Path.Combine(root, "modules", moduleId, "metadata", sourcePath));
    }

    private static bool MetadataRootExists(string root, string moduleId)
    {
        return Directory.Exists(Path.Combine(root, "modules", moduleId, "metadata"));
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
