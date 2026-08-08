using System.Text.Json;
using ICP.Platform.Kernel.ModulePlanning;

namespace ICP.Platform.Kernel.PluginPackages;

public class FrontendRuntimeManifestBuilder
{
    private const string DefaultPublicBaseUrl = "/plugin-assets";
    private static readonly JsonSerializerOptions ManifestJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly string _publicBaseUrl;

    public string PublicBaseUrl => _publicBaseUrl;

    public FrontendRuntimeManifestBuilder()
        : this(DefaultPublicBaseUrl)
    {
    }

    public FrontendRuntimeManifestBuilder(string publicBaseUrl)
    {
        _publicBaseUrl = publicBaseUrl;
    }

    public string Build(IEnumerable<InstalledPluginRecord> records)
    {
        var manifest = new
        {
            plugins = records
                .Select(CreateEntry)
                .OrderBy(entry => entry.Url, StringComparer.OrdinalIgnoreCase)
                .Select(entry => new { url = entry.Url })
                .ToArray()
        };

        return JsonSerializer.Serialize(manifest, new JsonSerializerOptions
        {
            WriteIndented = true
        }) + Environment.NewLine;
    }

    public string Build(CanonicalModulePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var manifest = new
        {
            snapshotId = plan.SnapshotId,
            projectionHash = plan.PlanHash,
            plugins = plan.Modules
                .Where(static module => !string.IsNullOrWhiteSpace(module.FrontendEntry))
                .Select(module => module.Provenance.SourceKind == "built-in"
                    ? (object)new
                    {
                        moduleId = module.FrontendEntry,
                        canonicalModuleId = module.ModuleId,
                        version = module.Version,
                        behaviorKey = module.FrontendBehaviorKey,
                        bindingMode = module.FrontendBindingMode,
                        bindings = module.FrontendBindings
                    }
                    : new
                    {
                        url = CombinePublicPath(
                            _publicBaseUrl,
                            $"{module.ModuleId}/{module.Version}/{module.FrontendEntry!.TrimStart('/')}")
                    })
                .ToArray()
        };

        return JsonSerializer.Serialize(manifest, new JsonSerializerOptions
        {
            WriteIndented = true
        }) + Environment.NewLine;
    }

    private RuntimePluginEntry CreateEntry(InstalledPluginRecord record)
    {
        var frontendRoot = Path.GetFullPath(Path.Combine(record.PackageRoot, "frontend"));
        var frontendManifestPath = Path.Combine(frontendRoot, "plugin.frontend.json");
        var frontendManifest = JsonSerializer.Deserialize<FrontendPackageManifest>(File.ReadAllText(frontendManifestPath), ManifestJsonOptions)
            ?? throw new JsonException($"Frontend manifest '{frontendManifestPath}' deserialized to null.");

        var runtimeManifestPath = ResolveContainedFilePath(frontendRoot, frontendManifest.EntryManifest, "EntryManifest");
        var runtimeManifest = JsonSerializer.Deserialize<FrontendEntryManifest>(File.ReadAllText(runtimeManifestPath), ManifestJsonOptions)
            ?? throw new JsonException($"Frontend runtime manifest '{runtimeManifestPath}' deserialized to null.");

        ResolveContainedFilePath(frontendRoot, runtimeManifest.Entry, "Entry");

        var pluginBaseUrl = CombinePublicPath(_publicBaseUrl, $"{record.ModuleId}/{record.Version}");
        return new RuntimePluginEntry(CombinePublicPath(pluginBaseUrl, runtimeManifest.Entry));
    }

    private static string CombinePublicPath(string publicBasePath, string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new JsonException("Frontend runtime manifest Entry must not be empty.");
        }

        return $"{publicBasePath.TrimEnd('/')}/{relativePath.TrimStart('/')}";
    }

    private static string ResolveContainedPath(string rootPath, string? relativePath, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new JsonException($"Frontend manifest {propertyName} must not be empty.");
        }

        if (Path.IsPathRooted(relativePath))
        {
            throw new JsonException($"Frontend manifest {propertyName} resolves to an unsafe path.");
        }

        var fullRootPath = Path.GetFullPath(rootPath);
        var fullCandidatePath = Path.GetFullPath(Path.Combine(fullRootPath, relativePath));
        if (!fullCandidatePath.StartsWith(fullRootPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(fullCandidatePath, fullRootPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new JsonException($"Frontend manifest {propertyName} resolves to an unsafe path.");
        }

        return fullCandidatePath;
    }

    private static string ResolveContainedFilePath(string rootPath, string? relativePath, string propertyName)
    {
        var candidatePath = ResolveContainedPath(rootPath, relativePath, propertyName);
        if (!File.Exists(candidatePath))
        {
            throw new FileNotFoundException($"Frontend manifest {propertyName} file not found.", relativePath);
        }

        return candidatePath;
    }

    private sealed class FrontendPackageManifest
    {
        public string EntryManifest { get; set; } = string.Empty;
    }

    private sealed class FrontendEntryManifest
    {
        public string Entry { get; set; } = string.Empty;
    }

    private sealed record RuntimePluginEntry(string Url);
}
