using System.Text.Json;
using ICP.Platform.Kernel.PluginPackages;

namespace ICP.Platform.Kernel.ModulePlanning;

/// <summary>
/// 把内建 Module Capsule 与其 metadata owner 读取为纯编译候选。
/// </summary>
public sealed class ModuleCapsuleCandidateReader
{
    /// <summary>
    /// 读取构建输出中的内建模块胶囊。目录枚举顺序不进入编译语义。
    /// </summary>
    public IReadOnlyList<ModuleCapsuleCandidate> ReadBuiltIn(string modulesRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modulesRoot);
        if (!Directory.Exists(modulesRoot))
        {
            throw new DirectoryNotFoundException($"Built-in module root not found: {modulesRoot}");
        }

        return Directory.GetDirectories(modulesRoot)
            .OrderBy(static path => path, StringComparer.Ordinal)
            .Select(ReadBuiltInModule)
            .ToArray();
    }

    /// <summary>
    /// 把已安装包的当前启用版本适配为同一编译输入。
    /// </summary>
    public IReadOnlyList<ModuleCapsuleCandidate> ReadInstalled(
        IEnumerable<InstalledPluginRecord> records)
    {
        ArgumentNullException.ThrowIfNull(records);
        return records
            .OrderBy(static record => record.ModuleId, StringComparer.Ordinal)
            .ThenBy(static record => record.Version, StringComparer.Ordinal)
            .Select(ReadInstalledModule)
            .ToArray();
    }

    private static ModuleCapsuleCandidate ReadBuiltInModule(string moduleRoot)
    {
        var manifestPath = Path.Combine(moduleRoot, "plugin.json");
        if (!File.Exists(manifestPath))
        {
            throw new FileNotFoundException("Built-in module capsule root is missing.", manifestPath);
        }

        using var manifestDocument = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var root = manifestDocument.RootElement;
        var moduleId = RequiredString(root, "moduleId", manifestPath);
        var version = RequiredString(root, "version", manifestPath);
        var displayName = RequiredString(root, "displayName", manifestPath);
        var description = OptionalString(root, "description");
        var backendEntry = OptionalString(root, "entryAssembly");
        var dependencies = ReadStringArray(root, "dependencies");

        var backendBehaviorKey = TryGetObject(root, "backend", out var backend)
            ? OptionalString(backend, "behaviorKey")
            : null;
        var frontendBehaviorKey = default(string);
        var frontendEntry = ToKebabCase(moduleId);
        var frontendBindingMode = "legacy-module";
        IReadOnlyList<string> frontendBindings = Array.Empty<string>();
        if (TryGetObject(root, "frontend", out var frontend))
        {
            if (OptionalBoolean(frontend, "enabled") == false)
            {
                frontendEntry = null;
            }
            else
            {
                frontendBehaviorKey = OptionalString(frontend, "behaviorKey");
                frontendEntry = OptionalString(frontend, "entry") ?? frontendEntry;
                frontendBindingMode = OptionalString(frontend, "bindingMode") ?? frontendBindingMode;
                frontendBindings = ReadStringArray(frontend, "bindings");
            }
        }

        var permissionCodes = TryGetObject(root, "security", out var security)
            && security.TryGetProperty("permissions", out var permissions)
            && permissions.ValueKind == JsonValueKind.Array
                ? permissions.EnumerateArray()
                    .Select(permission => RequiredString(permission, "code", manifestPath))
                    .ToArray()
                : Array.Empty<string>();

        var metadata = ReadMetadata(Path.Combine(moduleRoot, "metadata"), moduleId);
        if (permissionCodes.Length == 0)
        {
            // 未完成 capsule cutover 的内建模块通过显式 legacy adapter 保持现有权限引用可编译；
            // adopter 必须在根 security owner 中声明权限，不能依赖该兼容路径。
            permissionCodes = metadata.PermissionReferences
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        }

        if (frontendBindings.Count == 0)
        {
            frontendBindings = metadata.FrontendBindingReferences;
        }

        return new ModuleCapsuleCandidate(
            moduleId,
            version,
            displayName,
            description,
            Path.GetFullPath(moduleRoot),
            backendEntry,
            backendBehaviorKey,
            frontendEntry,
            frontendBehaviorKey,
            frontendBindingMode,
            dependencies,
            metadata.Routes,
            frontendBindings,
            metadata.FrontendBindingReferences,
            permissionCodes,
            metadata.PermissionReferences,
            metadata.ModelKeys,
            new ModulePlanProvenance(
                "built-in",
                Path.GetFullPath(manifestPath),
                "/"),
            ReadPersistence(root, manifestPath));
    }

    private static ModuleCapsuleCandidate ReadInstalledModule(InstalledPluginRecord record)
    {
        var packageRoot = Path.GetFullPath(record.PackageRoot);
        var manifestPath = Path.Combine(packageRoot, "plugin.json");
        var backendManifestPath = Path.Combine(packageRoot, "backend", "plugin.backend.json");
        var frontendManifestPath = Path.Combine(packageRoot, "frontend", "plugin.frontend.json");
        using var manifestDocument = JsonDocument.Parse(File.ReadAllText(manifestPath));
        using var backendDocument = JsonDocument.Parse(File.ReadAllText(backendManifestPath));
        using var frontendDocument = JsonDocument.Parse(File.ReadAllText(frontendManifestPath));
        var root = manifestDocument.RootElement;
        var backend = backendDocument.RootElement;
        var frontend = frontendDocument.RootElement;
        var moduleId = RequiredString(root, "moduleId", manifestPath);
        var version = RequiredString(root, "version", manifestPath);
        var entryManifest = RequiredString(frontend, "entryManifest", frontendManifestPath);
        var entryManifestPath = ResolveContainedFile(
            Path.Combine(packageRoot, "frontend"),
            entryManifest,
            "frontend entry manifest");
        using var entryDocument = JsonDocument.Parse(File.ReadAllText(entryManifestPath));
        var frontendEntry = RequiredString(entryDocument.RootElement, "entry", entryManifestPath);
        var metadata = ReadMetadata(
            Path.Combine(packageRoot, "modules", moduleId, "metadata"),
            moduleId);

        return new ModuleCapsuleCandidate(
            moduleId,
            version,
            RequiredString(root, "displayName", manifestPath),
            OptionalString(root, "description"),
            packageRoot,
            RequiredString(backend, "entryAssembly", backendManifestPath),
            null,
            frontendEntry,
            null,
            "legacy-module",
            ReadStringArray(backend, "dependencies"),
            metadata.Routes,
            metadata.FrontendBindingReferences,
            metadata.FrontendBindingReferences,
            metadata.PermissionReferences,
            metadata.PermissionReferences,
            metadata.ModelKeys,
            new ModulePlanProvenance("installed-package", manifestPath, "/"),
            ReadPersistence(root, manifestPath));
    }

    private static ModulePersistenceContribution? ReadPersistence(JsonElement root, string sourcePath)
    {
        if (!TryGetObject(root, "persistence", out var persistence))
        {
            return null;
        }

        var modelPaths = ReadStringArray(persistence, "models");
        var bundles = new List<ModuleMigrationBundleReference>();
        if (TryGetObject(persistence, "migrations", out var migrations))
        {
            foreach (var property in migrations.EnumerateObject().OrderBy(static item => item.Name, StringComparer.Ordinal))
            {
                if (property.Value.ValueKind != JsonValueKind.Object)
                {
                    throw new JsonException($"Persistence migration '{property.Name}' must be an object in '{sourcePath}'.");
                }

                var provider = property.Name.ToLowerInvariant() switch
                {
                    "sqlserver" => "SqlServer",
                    "oracle" => "Oracle",
                    "mysql" => "MySql",
                    _ => throw new JsonException($"Unsupported persistence provider '{property.Name}' in '{sourcePath}'.")
                };
                bundles.Add(new ModuleMigrationBundleReference(
                    provider,
                    RequiredString(property.Value, "assembly", sourcePath),
                    RequiredString(property.Value, "manifest", sourcePath)));
            }
        }

        return new ModulePersistenceContribution(modelPaths, bundles);
    }

    private static MetadataFacts ReadMetadata(string metadataRoot, string moduleId)
    {
        if (!Directory.Exists(metadataRoot))
        {
            return MetadataFacts.Empty;
        }

        var routes = new List<string>();
        var bindingReferences = new List<string>();
        var permissionReferences = new List<string>();
        var navigationRoot = Path.Combine(metadataRoot, "navigation");
        if (Directory.Exists(navigationRoot))
        {
            foreach (var path in Directory.GetFiles(navigationRoot, "*.json", SearchOption.AllDirectories)
                         .OrderBy(static value => value, StringComparer.Ordinal))
            {
                using var document = JsonDocument.Parse(File.ReadAllText(path));
                if (!TryGetObject(document.RootElement, "navigation", out var navigation)
                    || !navigation.TryGetProperty("items", out var items)
                    || items.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var item in items.EnumerateArray())
                {
                    AddOptionalString(item, "path", routes);
                    AddOptionalString(item, "targetKey", bindingReferences);
                    CollectStringArrayProperties(item, "permissions", permissionReferences);
                }
            }
        }

        var viewsRoot = Path.Combine(metadataRoot, "views");
        if (Directory.Exists(viewsRoot))
        {
            foreach (var path in Directory.GetFiles(viewsRoot, "*.json", SearchOption.AllDirectories)
                         .OrderBy(static value => value, StringComparer.Ordinal))
            {
                using var document = JsonDocument.Parse(File.ReadAllText(path));
                if (TryGetObject(document.RootElement, "view", out var view))
                {
                    AddOptionalString(view, "key", bindingReferences);
                    CollectStringArrayProperties(view, "permissions", permissionReferences);
                }
            }
        }

        var modelKeys = new List<string>();
        var modelsRoot = Path.Combine(metadataRoot, "models");
        if (Directory.Exists(modelsRoot))
        {
            foreach (var path in Directory.GetFiles(modelsRoot, "*.json", SearchOption.AllDirectories)
                         .OrderBy(static value => value, StringComparer.Ordinal))
            {
                using var document = JsonDocument.Parse(File.ReadAllText(path));
                foreach (var property in document.RootElement.EnumerateObject())
                {
                    if (property.NameEquals("schemaVersion") || property.NameEquals("type"))
                    {
                        continue;
                    }

                    modelKeys.Add(property.Name.Contains('.', StringComparison.Ordinal)
                        ? property.Name
                        : $"{moduleId}.{property.Name}");
                }
            }
        }

        return new MetadataFacts(
            routes.Distinct(StringComparer.Ordinal).OrderBy(static value => value, StringComparer.Ordinal).ToArray(),
            bindingReferences.Distinct(StringComparer.Ordinal).OrderBy(static value => value, StringComparer.Ordinal).ToArray(),
            permissionReferences.Distinct(StringComparer.Ordinal).OrderBy(static value => value, StringComparer.Ordinal).ToArray(),
            modelKeys.Distinct(StringComparer.Ordinal).OrderBy(static value => value, StringComparer.Ordinal).ToArray());
    }

    private static bool TryGetObject(JsonElement element, string propertyName, out JsonElement value)
    {
        if (TryGetProperty(element, propertyName, out value) && value.ValueKind == JsonValueKind.Object)
        {
            return true;
        }

        value = default;
        return false;
    }

    private static string RequiredString(JsonElement element, string propertyName, string sourcePath)
    {
        var value = OptionalString(element, propertyName);
        return !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new JsonException($"Required property '{propertyName}' is missing in '{sourcePath}'.");
    }

    private static string? OptionalString(JsonElement element, string propertyName)
    {
        return TryGetProperty(element, propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static bool? OptionalBoolean(JsonElement element, string propertyName)
    {
        return TryGetProperty(element, propertyName, out var value)
            && value.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? value.GetBoolean()
                : null;
    }

    private static string[] ReadStringArray(JsonElement element, string propertyName)
    {
        return TryGetProperty(element, propertyName, out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray()
                .Where(static item => item.ValueKind == JsonValueKind.String)
                .Select(static item => item.GetString())
                .Where(static item => !string.IsNullOrWhiteSpace(item))
                .Cast<string>()
                .ToArray()
            : Array.Empty<string>();
    }

    private static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    private static string ResolveContainedFile(string root, string relativePath, string description)
    {
        if (Path.IsPathRooted(relativePath))
        {
            throw new JsonException($"{description} resolves outside its package root.");
        }

        var fullRoot = Path.GetFullPath(root);
        var candidate = Path.GetFullPath(Path.Combine(fullRoot, relativePath));
        if (!candidate.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || !File.Exists(candidate))
        {
            throw new FileNotFoundException($"{description} is missing or unsafe.", relativePath);
        }

        return candidate;
    }

    private static void AddOptionalString(JsonElement element, string propertyName, ICollection<string> values)
    {
        var value = OptionalString(element, propertyName);
        if (!string.IsNullOrWhiteSpace(value))
        {
            values.Add(value);
        }
    }

    private static void CollectStringArrayProperties(
        JsonElement element,
        string propertyName,
        ICollection<string> values)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (property.NameEquals(propertyName) && property.Value.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var value in property.Value.EnumerateArray())
                        {
                            if (value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString()))
                            {
                                values.Add(value.GetString()!);
                            }
                        }
                    }
                    else
                    {
                        CollectStringArrayProperties(property.Value, propertyName, values);
                    }
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    CollectStringArrayProperties(item, propertyName, values);
                }
                break;
        }
    }

    private static string ToKebabCase(string value)
    {
        var characters = new List<char>(value.Length + 4);
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (char.IsUpper(character) && index > 0)
            {
                characters.Add('-');
            }
            characters.Add(char.ToLowerInvariant(character));
        }
        return new string(characters.ToArray());
    }

    private sealed record MetadataFacts(
        IReadOnlyList<string> Routes,
        IReadOnlyList<string> FrontendBindingReferences,
        IReadOnlyList<string> PermissionReferences,
        IReadOnlyList<string> ModelKeys)
    {
        public static MetadataFacts Empty { get; } = new(
            Array.Empty<string>(),
            Array.Empty<string>(),
            Array.Empty<string>(),
            Array.Empty<string>());
    }
}
