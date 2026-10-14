using System.Text.Json;
using ICP.Platform.Kernel.ModuleLoading;
using ICP.Platform.Kernel.PluginPackages;

namespace ICP.Platform.Kernel.ModulePlanning;

/// <summary>
/// 把内建 Module Capsule 与其 metadata owner 读取为纯编译候选。
/// </summary>
public sealed class ModuleCapsuleCandidateReader
{
    /// <summary>
    /// 读取构建输出中的内建模块胶囊。公开读取只返回来源描述，不授予 destructive migration 权限。
    /// </summary>
    public IReadOnlyList<ModuleCapsuleCandidate> ReadBuiltIn(string modulesRoot)
        => ReadBuiltIn(modulesRoot, trustedSource: null);

    // 只有受控 Host/activation 路径可把配置的 distribution root 提升为 capability；
    // 任意公共调用方即使复制完全相同的 SourceKind 字符串也只能得到普通来源描述。
    internal IReadOnlyList<ModuleCapsuleCandidate> ReadTrustedBuiltIn(TrustedBuiltInModuleSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return ReadBuiltIn(source.ModulesRoot, source);
    }

    // 原生库存已由 Host source 登记固定快照，仍沿用正式 Capsule 读取和 Canonical 编译。
    internal ModuleCapsuleCandidate ReadTrustedNativeModule(string moduleRoot, TrustedBuiltInModuleSource source) =>
        ReadBuiltInModule(moduleRoot, source, source.CaptureModuleSnapshot(moduleRoot)) with { FrontendDelivery = ModuleFrontendDelivery.Url };

    private static IReadOnlyList<ModuleCapsuleCandidate> ReadBuiltIn(
        string modulesRoot,
        TrustedBuiltInModuleSource? trustedSource)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modulesRoot);
        if (!Directory.Exists(modulesRoot))
        {
            throw new DirectoryNotFoundException($"Built-in module root not found: {modulesRoot}");
        }

        return Directory.GetDirectories(modulesRoot)
            .OrderBy(static path => path, StringComparer.Ordinal)
            .Select(path =>
            {
                var snapshot = trustedSource?.CaptureModuleSnapshot(path);
                return ReadBuiltInModule(path, trustedSource, snapshot);
            })
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

    // 从同一胶囊和 metadata 快照读取候选；显式绑定决定哪些 Detail View 同时拥有完整页行为。
    private static ModuleCapsuleCandidate ReadBuiltInModule(
        string moduleRoot,
        TrustedBuiltInModuleSource? trustedSource,
        VerifiedModuleArtifactSnapshot? snapshot)
    {
        var manifestPath = Path.Combine(moduleRoot, "plugin.json");
        if (!File.Exists(manifestPath))
        {
            throw new FileNotFoundException("Built-in module capsule root is missing.", manifestPath);
        }

        using var manifestDocument = JsonDocument.Parse(
            snapshot?.ReadFile("plugin.json") ?? File.ReadAllBytes(manifestPath));
        var root = manifestDocument.RootElement;
        var moduleId = RequiredString(root, "moduleId", manifestPath);
        var version = RequiredString(root, "version", manifestPath);
        var displayName = RequiredString(root, "displayName", manifestPath);
        var description = OptionalString(root, "description");
        var backendEntry = OptionalString(root, "entryAssembly");
        if (backendEntry is not null && trustedSource is not null)
        {
            var native = trustedSource.IsAcceptedNativeRoot(moduleRoot);
            var entryImage = native
                ? snapshot!.ReadFile(backendEntry)
                : trustedSource.CaptureDistributionFile(Path.Combine(moduleRoot, backendEntry));
            snapshot = snapshot!.WithDistributionFile(backendEntry, entryImage);
            if (!native)
            {
                // 普通内建模块的引用闭包在签发前捕获；装载阶段不能按邻文件补齐依赖。
                var directory = Path.GetDirectoryName(backendEntry) ?? string.Empty;
                foreach (var contract in VerifiedAssemblyIdentityReader.ReadOwnerContractClosure(entryImage,
                             name => trustedSource.CaptureDistributionFile(Path.Combine(moduleRoot, directory, name + ".dll"))))
                    snapshot = snapshot.WithDistributionFile(Path.Combine(directory, contract.Identity.AssemblyName.Name + ".dll"), contract.Bytes);
            }
        }
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

        var declaredBindings = frontendBindingMode == "behavior-bindings"
            ? frontendBindings.ToHashSet(StringComparer.Ordinal)
            : null;
        var metadata = snapshot is null
            ? ReadMetadata(Path.Combine(moduleRoot, "metadata"), moduleId, declaredBindings)
            : ReadMetadata(snapshot, moduleId, declaredBindings);

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
            metadata.ModelKeys,
            trustedSource is not null
                ? ModulePlanProvenance.CreateTrustedBuiltInRoot(
                    moduleId,
                    moduleRoot,
                    trustedSource,
                    snapshot!,
                    manifestPath,
                    "/")
                : new ModulePlanProvenance(
                    "built-in",
                    Path.GetFullPath(manifestPath),
                    "/"),
            ReadPersistence(root, manifestPath))
        {
            Features = ReadFeatures(root, moduleId, Path.Combine(moduleRoot, "metadata"), snapshot),
            DataActions = DataActionCatalogCompiler.Read(root, moduleId),
            CompositionMembers = ReadCompositionMembers(root, manifestPath)
        };
    }

    // 外部包动作与内建动作经过相同读取、规范编译和启动装配。
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
            metadata.ModelKeys,
            new ModulePlanProvenance("installed-package", manifestPath, "/"),
            ReadPersistence(root, manifestPath))
        {
            Features = ReadFeatures(root, moduleId, Path.Combine(packageRoot, "modules", moduleId, "metadata"), null),
            DataActions = DataActionCatalogCompiler.Read(root, moduleId),
            CompositionMembers = ReadCompositionMembers(root, manifestPath)
        };
    }

    // 组合引用必须完整读取；错误形状不能像可选旧数组一样被静默裁剪。
    private static IReadOnlyList<string> ReadCompositionMembers(JsonElement root, string sourcePath)
    {
        if (!TryGetProperty(root, "composition", out var composition)) return [];
        if (composition.ValueKind != JsonValueKind.Object
            || !TryGetProperty(composition, "members", out var members)
            || members.ValueKind != JsonValueKind.Array)
            throw new JsonException($"Invalid composition.members in '{sourcePath}'. Expected an array of component ids.");
        var result = new List<string>();
        foreach (var member in members.EnumerateArray())
        {
            if (member.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(member.GetString()))
                throw new JsonException($"Invalid composition member in '{sourcePath}'. Expected a non-empty component id.");
            result.Add(member.GetString()!);
        }
        return result;
    }

    // 功能声明与已校验 metadata 来自同一候选快照，不能旁读变化中的 authoring 文件。
    private static IReadOnlyList<FeatureDeclaration> ReadFeatures(JsonElement capsule, string moduleId,
        string metadataRoot, VerifiedModuleArtifactSnapshot? snapshot)
    {
        var documents = snapshot is not null
            ? snapshot.EnumerateFiles("metadata").Where(path => path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                .Select(path => JsonSerializer.Deserialize<JsonElement>(ReadMetadataText(snapshot, path)))
            : Directory.Exists(metadataRoot)
                ? Directory.EnumerateFiles(metadataRoot, "*.json", SearchOption.AllDirectories)
                    .Select(path => JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(path)))
                : Enumerable.Empty<JsonElement>();
        var custom = FeatureCatalogCompiler.Read(capsule, moduleId);
        return StandardFeatureMetadataCompiler.Read(moduleId, documents, custom, includeCustomFeatures: true);
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

    // 读取文件系统 authoring；查看专用 Detail View 贡献权限引用，但不要求完整页 binding。
    private static MetadataFacts ReadMetadata(
        string metadataRoot,
        string moduleId,
        IReadOnlySet<string>? declaredBindings = null)
    {
        if (!Directory.Exists(metadataRoot))
        {
            return MetadataFacts.Empty;
        }

        var routes = new List<string>();
        var bindingReferences = new List<string>();
        bindingReferences.AddRange(PageCompositionMetadataValidator.Validate(
            Directory.EnumerateFiles(metadataRoot, "*.json", SearchOption.AllDirectories)
                .Select(path => JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(path))), declaredBindings));
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
                    AddViewBindingReference(view, bindingReferences, declaredBindings);
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

                    modelKeys.Add(property.Name);
                }
            }
        }

        return new MetadataFacts(
            routes.Distinct(StringComparer.Ordinal).OrderBy(static value => value, StringComparer.Ordinal).ToArray(),
            bindingReferences.Distinct(StringComparer.Ordinal).OrderBy(static value => value, StringComparer.Ordinal).ToArray(),
            modelKeys.Distinct(StringComparer.Ordinal).OrderBy(static value => value, StringComparer.Ordinal).ToArray());
    }

    // 可信快照与普通文件读取使用同一 View/binding 分类，不因装载路径改变候选含义。
    private static MetadataFacts ReadMetadata(
        VerifiedModuleArtifactSnapshot snapshot,
        string moduleId,
        IReadOnlySet<string>? declaredBindings = null)
    {
        var routes = new List<string>();
        var bindingReferences = new List<string>();
        bindingReferences.AddRange(PageCompositionMetadataValidator.Validate(
            snapshot.EnumerateFiles("metadata").Where(path => path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                .Select(path => JsonSerializer.Deserialize<JsonElement>(ReadMetadataText(snapshot, path))), declaredBindings));
        foreach (var path in snapshot.EnumerateFiles("metadata/navigation")
                     .Where(static path => path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)))
        {
            using var document = JsonDocument.Parse(ReadMetadataText(snapshot, path));
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
            }
        }

        foreach (var path in snapshot.EnumerateFiles("metadata/views")
                     .Where(static path => path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)))
        {
            using var document = JsonDocument.Parse(ReadMetadataText(snapshot, path));
            if (TryGetObject(document.RootElement, "view", out var view))
            {
                AddViewBindingReference(view, bindingReferences, declaredBindings);
            }
        }

        var modelKeys = new List<string>();
        foreach (var path in snapshot.EnumerateFiles("metadata/models")
                     .Where(static path => path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)))
        {
            using var document = JsonDocument.Parse(ReadMetadataText(snapshot, path));
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (property.NameEquals("schemaVersion") || property.NameEquals("type"))
                {
                    continue;
                }

                modelKeys.Add(property.Name);
            }
        }

        return new MetadataFacts(
            routes.Distinct(StringComparer.Ordinal).OrderBy(static value => value, StringComparer.Ordinal).ToArray(),
            bindingReferences.Distinct(StringComparer.Ordinal).OrderBy(static value => value, StringComparer.Ordinal).ToArray(),
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

    // 快照字节保持原样用于来源验证；JSON 读取与 File.ReadAllText 一样检测 BOM。
    private static string ReadMetadataText(VerifiedModuleArtifactSnapshot snapshot, string path)
    {
        using var stream = new MemoryStream(snapshot.ReadFile(path), writable: false);
        using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
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
        if (!PathContainment.Contains(fullRoot, candidate, allowRoot: false)
            || !File.Exists(candidate))
        {
            throw new FileNotFoundException($"{description} is missing or unsafe.", relativePath);
        }

        return candidate;
    }

    // Detail View 是展示定义；显式声明完整页行为时，其 key 才同时成为 binding 引用。
    private static void AddViewBindingReference(
        JsonElement view,
        ICollection<string> bindingReferences,
        IReadOnlySet<string>? declaredBindings)
    {
        if (OptionalString(view, "mode") is "tree" or "tab") return;
        var key = OptionalString(view, "key");
        if (key is not null
            && (OptionalString(view, "mode") != "detail"
                || declaredBindings is null
                || declaredBindings.Contains(key)))
        {
            bindingReferences.Add(key);
        }
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
        IReadOnlyList<string> ModelKeys)
    {
        public static MetadataFacts Empty { get; } = new(
            Array.Empty<string>(),
            Array.Empty<string>(),
            Array.Empty<string>());
    }
}
