using System.Text.Json;
using ICP.Platform.Modules.MetadataConfigCenter.Models;

namespace ICP.Platform.Modules.MetadataConfigCenter.Services;

public class MetadataManifestService : IMetadataManifestService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly string _repositoryRoot;

    public MetadataManifestService()
        : this(ResolveRepositoryRoot())
    {
    }

    public MetadataManifestService(string repositoryRoot)
    {
        _repositoryRoot = repositoryRoot;
    }

    public async Task<IReadOnlyList<MetadataModuleManifest>> GetModuleManifestsAsync(CancellationToken cancellationToken = default)
    {
        var modulesRoot = Path.Combine(_repositoryRoot, "modules");
        if (!Directory.Exists(modulesRoot))
        {
            return [];
        }

        var manifestPaths = Directory
            .EnumerateFiles(modulesRoot, "metadata.manifest.json", SearchOption.AllDirectories)
            .Where(path => path.Contains($"{Path.DirectorySeparatorChar}metadata{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var manifests = new List<MetadataModuleManifest>(manifestPaths.Count);
        foreach (var manifestPath in manifestPaths)
        {
            await using var stream = File.OpenRead(manifestPath);
            var manifest = await JsonSerializer.DeserializeAsync<MetadataModuleManifest>(stream, JsonOptions, cancellationToken);
            if (manifest is null || string.IsNullOrWhiteSpace(manifest.ModuleId))
            {
                continue;
            }

            manifests.Add(manifest);
        }

        return manifests;
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

        throw new DirectoryNotFoundException("Unable to locate repository root for metadata manifest discovery.");
    }
}
