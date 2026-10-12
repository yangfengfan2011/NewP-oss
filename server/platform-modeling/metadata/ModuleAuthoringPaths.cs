using System.Text.Json;
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
            ? Path.Combine(root, "frontend", "modules", ToSlug(moduleId))
            : Path.Combine(root, "modules", moduleId);

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
