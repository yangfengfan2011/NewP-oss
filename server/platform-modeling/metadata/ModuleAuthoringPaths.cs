using System.Text.Json;
using ICP.Platform.Kernel.ModulePlanning;
using System.Text.RegularExpressions;

namespace ICP.Platform.Modeling.Metadata;

/// <summary>区分仓库的前端与声明根和按模块身份组织的运行时 metadata 输出。</summary>
public static class ModuleAuthoringPaths
{
    /// <summary>把已有模块身份转换为仓库约定的小写连字符目录名。</summary>
    public static string ToSlug(string moduleId) =>
        Regex.Replace(moduleId, "([a-z0-9])([A-Z])", "$1-$2").ToLowerInvariant();

    /// <summary>仓库读取 frontend/modules；发布输出仍读取 modules/&lt;ModuleId&gt;。</summary>
    public static string ResolveModuleRoot(string root, string moduleId) =>
        Directory.Exists(Path.Combine(root, "frontend", "modules"))
            ? ResolveRepositoryModuleRoot(root, moduleId)
            : Path.Combine(root, "modules", moduleId);

    // 有 Capsule 时按身份发现真实根；无声明历史 metadata 保留原 owner 路径约定。
    private static string ResolveRepositoryModuleRoot(string root, string moduleId)
    {
        var catalog = ComponentSourceCatalog.Discover(root);
        return catalog.Components.SingleOrDefault(component => component.ModuleId == moduleId)?.Root
            ?? ComponentSourceCatalog.ResolveLegacyFrontendRoot(root, ToSlug(moduleId));
    }

    /// <summary>枚举真实组件根和既有无 Capsule 的 metadata owner，运行时输出保持平面。</summary>
    public static IEnumerable<string> EnumerateModuleRoots(string root)
    {
        var authoringRoot = Path.Combine(root, "frontend", "modules");
        if (!Directory.Exists(authoringRoot))
            return Directory.Exists(Path.Combine(root, "modules")) ? Directory.EnumerateDirectories(Path.Combine(root, "modules")) : [];
        var catalog = ComponentSourceCatalog.Discover(root);
        return catalog.Components.Select(component => component.Root).Concat(ComponentSourceCatalog.EnumerateSourceDirectories(authoringRoot)
            .Where(directory => !File.Exists(Path.Combine(directory, "newp.plugin.json")) && Directory.Exists(Path.Combine(directory, "metadata")))).Order(StringComparer.Ordinal);
    }

    /// <summary>读取声明身份；尚无 Capsule 的 metadata 目录沿用已有命名约定。</summary>
    public static string ReadModuleId(string moduleRoot)
    {
        var capsulePath = Path.Combine(moduleRoot, "newp.plugin.json");
        if (File.Exists(capsulePath))
        {
            using var capsule = JsonDocument.Parse(File.ReadAllBytes(capsulePath));
            return capsule.RootElement.GetProperty("moduleId").GetString()
                ?? throw new InvalidOperationException($"Module identity is missing: {capsulePath}");
        }

        return string.Concat(Path.GetFileName(moduleRoot).Split('-')
            .Select(segment => char.ToUpperInvariant(segment[0]) + segment[1..]));
    }
}
