using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using ICP.Platform.Kernel.ModulePlanning;

namespace ICP.Platform.PluginPlatform.ComponentBundles;

/// <summary>从声明与本轮真实产物生成组件独立资产和聚合交付索引。</summary>
public sealed class ComponentBundleBuilder
{
    /// <summary>收集完整归组到一个新的暂存根；不写安装库存、authoring 或迁移清单。</summary>
    public ComponentBundleManifest Stage(string repositoryRoot, string rootModuleId, string outputRoot,
        IReadOnlyDictionary<string, ComponentBuiltArtifacts> artifacts)
    {
        var catalog = ComponentSourceCatalog.Discover(repositoryRoot);
        var group = catalog.GetGroup(rootModuleId);
        var framework = group.Any(component => component.Capsule.TryGetProperty("entryAssembly", out _))
            ? ComponentBundleAssemblyValidator.FrameworkAssemblies() : new Dictionary<string, Version>();
        if (Directory.Exists(outputRoot) || File.Exists(outputRoot))
            throw new IOException($"COMPONENT_BUNDLE_OUTPUT_EXISTS: '{outputRoot}'.");
        Directory.CreateDirectory(outputRoot);
        var members = new List<ComponentBundleMember>();
        foreach (var component in group.OrderBy(component => component.ModuleId, StringComparer.Ordinal))
        {
            var relativeRoot = "modules/" + component.ModuleId;
            var target = ComponentBundleIntegrity.Resolve(outputRoot, relativeRoot);
            Directory.CreateDirectory(target);
            var built = artifacts.GetValueOrDefault(component.ModuleId);
            Copy(component.CapsulePath, target, "authoring/newp.plugin.json");
            var transport = ComponentBundleIntegrity.Transport(JsonNode.Parse(File.ReadAllBytes(component.CapsulePath))!);
            File.WriteAllBytes(Path.Combine(target, "plugin.json"), ComponentBundleIntegrity.Serialize(transport));
            foreach (var source in catalog.EnumerateOwnFiles(component.ModuleId))
            {
                var relative = Path.GetRelativePath(component.Root, source).Replace('\\', '/');
                if (relative.StartsWith("metadata/", StringComparison.Ordinal) || relative.StartsWith("migrations/", StringComparison.Ordinal)
                    || relative.StartsWith("resources/", StringComparison.Ordinal)) Copy(source, target, relative);
            }
            if (component.Capsule.TryGetProperty("entryAssembly", out var entry))
            {
                Copy(built?.BackendAssembly ?? throw Missing(component.ModuleId, "backend"), target,
                    "backend/" + Path.GetFileName(entry.GetString()!.Replace('\\', '/')));
                CopyBackendDependencies(built!.BackendAssembly!, target, framework);
            }
            if (transport["frontend"] is JsonObject frontend && frontend["enabled"]?.GetValue<bool>() != false)
            {
                var frontendRoot = built?.FrontendRoot ?? throw Missing(component.ModuleId, "frontend");
                foreach (var relative in ComponentBundleIntegrity.Files(frontendRoot))
                    Copy(ComponentBundleIntegrity.Resolve(frontendRoot, relative), target, "frontend/" + relative);
            }
            if (component.Capsule.TryGetProperty("persistence", out var persistence) && persistence.TryGetProperty("migrations", out var migrations))
            {
                foreach (var migration in migrations.EnumerateObject())
                {
                    var assembly = migration.Value.GetProperty("assembly").GetString()!;
                    Copy(built?.MigrationAssemblies.GetValueOrDefault(assembly) ?? throw Missing(component.ModuleId, assembly), target, assembly);
                }
                foreach (var source in built?.MigrationSources ?? new Dictionary<string, string>()) Copy(source.Value, target, source.Key);
            }
            var files = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (var relative in ComponentBundleIntegrity.Files(target).Order(StringComparer.Ordinal))
                files.Add(relative, ComponentBundleIntegrity.HashFile(ComponentBundleIntegrity.Resolve(target, relative)));
            members.Add(new(component.ModuleId, component.Version, ComponentBundleIntegrity.Hash(files), relativeRoot, files));
        }
        File.WriteAllBytes(Path.Combine(outputRoot, "platform-runtime-release.json"), PlatformRuntimeReleaseIdentity.Descriptor);
        var manifest = new ComponentBundleManifest(1, rootModuleId, PlatformRuntimeReleaseIdentity.Current, "", members);
        manifest = manifest with { ContentDigest = ComponentBundleIntegrity.Hash(manifest) };
        File.WriteAllBytes(Path.Combine(outputRoot, "component-bundle.json"), ComponentBundleIntegrity.Serialize(manifest));
        return new ComponentBundleInspector().Inspect(outputRoot).Manifest;
    }

    /// <summary>先核验再生成固定条目顺序与时间戳的 ZIP；相同内容产生相同字节。</summary>
    public ComponentBundleManifest Pack(string packageRoot, string zipPath)
    {
        var manifest = new ComponentBundleInspector().Inspect(packageRoot).Manifest;
        if (Path.GetFullPath(zipPath).StartsWith(Path.GetFullPath(packageRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new IOException("COMPONENT_BUNDLE_OUTPUT_INVALID: ZIP output must be outside the input package.");
        using var output = new FileStream(zipPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var archive = new ZipArchive(output, ZipArchiveMode.Create);
        foreach (var path in ComponentBundleIntegrity.Files(packageRoot).Order(StringComparer.Ordinal))
        {
            var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
            entry.LastWriteTime = new(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
            using var source = File.OpenRead(ComponentBundleIntegrity.Resolve(packageRoot, path));
            using var destination = entry.Open();
            source.CopyTo(destination);
        }
        return manifest;
    }

    // 原位产物可以在源码根之外，但复制前拒绝缺失文件和链接替换。
    private static void Copy(string source, string targetRoot, string relative)
    {
        if (!File.Exists(source)) throw new FileNotFoundException($"COMPONENT_BUNDLE_ASSET_MISSING: '{source}'.", source);
        if ((File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException($"COMPONENT_BUNDLE_PATH_INVALID: '{source}'.");
        var target = ComponentBundleIntegrity.Resolve(targetRoot, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Copy(source, target, false);
    }

    // 错误定位到缺少产物的 owner 和声明入口。
    private static InvalidDataException Missing(string moduleId, string asset) =>
        new($"COMPONENT_BUNDLE_ASSET_MISSING: module='{moduleId}', asset='{asset}'.");

    // 实际 private 引用沿 owner 构建输出收集；受控平台及共享框架由目标发行提供。
    private static void CopyBackendDependencies(string backendAssembly, string target, IReadOnlyDictionary<string, Version> framework)
    {
        using var release = JsonDocument.Parse(PlatformRuntimeReleaseIdentity.Descriptor);
        var shared = release.RootElement.GetProperty("backend").GetProperty("assemblies").EnumerateObject().Select(property => property.Name).ToHashSet(StringComparer.Ordinal);
        shared.UnionWith(framework.Keys);
        var pending = new Stack<string>();
        pending.Push(backendAssembly);
        var own = new HashSet<string>(StringComparer.Ordinal) { Path.GetFileNameWithoutExtension(backendAssembly) };
        while (pending.TryPop(out var current))
        {
            foreach (var name in ComponentBundleAssemblyValidator.References(current))
            {
                if (shared.Contains(name) || !own.Add(name)) continue;
                if (name.IndexOfAny(['/', '\\', ':']) >= 0) throw new InvalidDataException($"COMPONENT_BUNDLE_PATH_INVALID: assembly '{name}'.");
                var dependency = Path.Combine(Path.GetDirectoryName(backendAssembly)!, name + ".dll");
                if (!File.Exists(dependency)) throw new InvalidDataException($"COMPONENT_BUNDLE_DEPENDENCY_MISSING: '{backendAssembly}' requires '{name}'.");
                Copy(dependency, target, "backend/" + name + ".dll");
                pending.Push(dependency);
            }
        }
    }
}
