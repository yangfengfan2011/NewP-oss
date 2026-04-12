using System.Security.Cryptography;
using System.Text;

namespace ICP.Platform.Modules.MetadataConfigCenter.Storage;

public class ModuleMetadataStore
{
    private readonly string _repositoryRoot;

    public ModuleMetadataStore()
        : this(ResolveRepositoryRoot())
    {
    }

    public ModuleMetadataStore(string repositoryRoot)
    {
        _repositoryRoot = repositoryRoot;
    }

    public string ResolveMetadataDirectory(string moduleId, string sourcePath)
    {
        return Path.Combine(_repositoryRoot, "modules", moduleId, "metadata", sourcePath);
    }

    public string ResolveMetadataFilePath(string moduleId, string sourcePath, string sourceFile)
    {
        return Path.Combine(ResolveMetadataDirectory(moduleId, sourcePath), sourceFile);
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
}
