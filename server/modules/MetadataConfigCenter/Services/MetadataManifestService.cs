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
    private readonly string? _runtimeRoot;

    public MetadataManifestService()
        : this(ResolveRepositoryRoot(), ResolveRuntimeRoot())
    {
    }

    public MetadataManifestService(string repositoryRoot, string? runtimeRoot = null)
    {
        _repositoryRoot = Path.GetFullPath(repositoryRoot);
        _runtimeRoot = string.IsNullOrWhiteSpace(runtimeRoot)
            ? null
            : Path.GetFullPath(runtimeRoot);
    }

    public async Task<IReadOnlyList<MetadataModuleManifest>> GetModuleManifestsAsync(CancellationToken cancellationToken = default)
    {
        var manifestsByModuleId = new Dictionary<string, MetadataModuleManifest>(StringComparer.OrdinalIgnoreCase);
        foreach (var modulesRoot in EnumerateModulesRoots())
        {
            if (!Directory.Exists(modulesRoot))
            {
                continue;
            }

            var manifestPaths = Directory
                .EnumerateFiles(modulesRoot, "metadata.manifest.json", SearchOption.AllDirectories)
                .Where(path => path.Contains($"{Path.DirectorySeparatorChar}metadata{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase);

            foreach (var manifestPath in manifestPaths)
            {
                await using var stream = File.OpenRead(manifestPath);
                var manifest = await JsonSerializer.DeserializeAsync<MetadataModuleManifest>(stream, JsonOptions, cancellationToken);
                if (manifest is null || string.IsNullOrWhiteSpace(manifest.ModuleId) || manifestsByModuleId.ContainsKey(manifest.ModuleId))
                {
                    continue;
                }

                manifestsByModuleId[manifest.ModuleId] = manifest;
            }
        }

        return manifestsByModuleId.Values
            .OrderBy(manifest => manifest.ModuleId, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private IEnumerable<string> EnumerateModulesRoots()
    {
        if (!string.IsNullOrWhiteSpace(_runtimeRoot))
        {
            yield return Path.Combine(_runtimeRoot, "modules");
        }

        yield return Path.Combine(_repositoryRoot, "modules");
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
