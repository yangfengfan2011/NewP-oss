using System.Text.Json;
using System.Text.RegularExpressions;
using ICP.Platform.Kernel.ModulePlanning;

// CLI 和 MSBuild 编译入口 消费同一源码发现器；前端无需维护另一份身份或成员映射。
try
{
    if (args.Length is 6 or 7 or 8 && args[0] == "msbuild")
    {
        ComponentSourceBuildMapping.Write(args[1], args[2], args[3], args[4], bool.Parse(args[5]), args.Length >= 7 ? args[6] : null, args.Length == 8 ? args[7] : null);
        return 0;
    }
    if (args.Length < 2 || args[0] is not ("frontend-entries" or "describe"))
        throw new ArgumentException("Usage: component-source-build <frontend-entries|describe> <repository-root> [group-module-id]");
    var repository = Path.GetFullPath(args[1]);
    var catalog = ComponentSourceCatalog.Discover(repository);
    var excludeIndex = Array.IndexOf(args, "--exclude-modules");
    var excluded = (excludeIndex >= 0 ? args[excludeIndex + 1] : "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(StringComparer.Ordinal);
    var components = (args.Length > 2 && args[2] != "--exclude-modules" ? catalog.GetGroup(args[2]) : catalog.Components).Where(component => !excluded.Contains(component.ModuleId)).ToArray();
    if (args[0] == "frontend-entries")
    {
        var entries = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var component in components) AddFrontend(component.Capsule, component.Root, component.CapsulePath, entries);
        var legacyRoot = Path.Combine(repository, "apps", "host-api", "modules");
        if (Directory.Exists(legacyRoot))
        {
            foreach (var manifestPath in Directory.EnumerateFiles(legacyRoot, "plugin.json", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
            {
                using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
                if (catalog.Components.Any(component => component.ModuleId == document.RootElement.GetProperty("moduleId").GetString())) continue;
                var id = document.RootElement.GetProperty("moduleId").GetString()!;
                AddFrontend(document.RootElement, Path.Combine(repository, "frontend", "modules", Slug(id)), manifestPath, entries);
            }
        }
        Console.WriteLine(JsonSerializer.Serialize(entries));
    }
    else
    {
        Console.WriteLine(JsonSerializer.Serialize(components.Select(component => new
        {
            component.ModuleId, component.Version, component.Root, component.CapsulePath, component.ParentModuleId,
            BackendProject = component.Capsule.TryGetProperty("entryAssembly", out var entry)
                ? catalog.ResolveProject(repository, component.ModuleId, entry.GetString()!) : null,
            Assets = catalog.EnumerateOwnFiles(component.ModuleId).Select(path => Path.GetRelativePath(component.Root, path).Replace('\\', '/')).ToArray()
        })));
    }
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception.Message);
    return 1;
}

// 前端 entry 是稳定键；真实入口从拥有该 Capsule 的源码根解析。
static void AddFrontend(JsonElement capsule, string root, string sourcePath, IDictionary<string, string> entries)
{
    if (!capsule.TryGetProperty("frontend", out var frontend) || frontend.TryGetProperty("enabled", out var enabled) && enabled.ValueKind == JsonValueKind.False) return;
    var entry = frontend.TryGetProperty("entry", out var value) && value.ValueKind == JsonValueKind.String
        ? value.GetString()! : Slug(capsule.GetProperty("moduleId").GetString()!);
    var path = Path.Combine(root, "src", "index.ts");
    if (!File.Exists(path)) throw new FileNotFoundException($"COMPONENT_FRONTEND_MISSING: '{sourcePath}' entry='{entry}' is missing.", path);
    if (entries.ContainsKey(entry)) throw new InvalidOperationException($"COMPONENT_FRONTEND_DUPLICATE: '{sourcePath}' entry='{entry}' is duplicated.");
    entries.Add(entry, path);
}

// 保留已有平面历史入口的 slug 约定；可移动组件只通过其 Capsule 定位。
static string Slug(string moduleId) => Regex.Replace(moduleId, "([a-z0-9])([A-Z])", "$1-$2").ToLowerInvariant();