using System.Text.Json;

namespace ICP.Platform.PluginPlatform.ComponentBundles;

// 正式前端 builder 从 Rollup 实际输出生成引用闭包；inspect 核对每个 chunk、资源和共享依赖。
internal static class ComponentBundleFrontendValidator
{
    // 完整性覆盖入口实际引用的懒加载 chunk，不能只检查 entry.js 存在。
    internal static void Validate(string componentRoot)
    {
        var root = ComponentBundleIntegrity.Resolve(componentRoot, "frontend");
        using var document = JsonDocument.Parse(File.ReadAllBytes(ComponentBundleIntegrity.Resolve(root, "frontend-artifacts.json")));
        var map = document.RootElement;
        var runtime = map.GetProperty("platformRuntime").Deserialize<PlatformRuntimeRelease>(ComponentBundleIntegrity.Json);
        if (map.GetProperty("schemaVersion").GetInt32() != 1 || map.GetProperty("entry").GetString() != "entry.js"
            || runtime != PlatformRuntimeReleaseIdentity.Current)
            throw new InvalidDataException("COMPONENT_BUNDLE_RUNTIME_MISMATCH: frontend build release does not match platform.");
        using var release = JsonDocument.Parse(PlatformRuntimeReleaseIdentity.Descriptor);
        var shared = release.RootElement.GetProperty("frontend").EnumerateObject().Select(property => property.Name).Where(name => name != "binding").ToArray();
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "frontend-artifacts.json" };
        foreach (var artifact in map.GetProperty("files").EnumerateObject())
        {
            if (!files.Add(artifact.Name)) throw new InvalidDataException($"COMPONENT_BUNDLE_PATH_INVALID: '{artifact.Name}' collision.");
            RequireFile(root, artifact.Name);
            foreach (var reference in artifact.Value.GetProperty("imports").EnumerateArray().Concat(artifact.Value.GetProperty("assets").EnumerateArray()))
                RequireFile(root, reference.GetString()!);
            foreach (var reference in artifact.Value.GetProperty("sharedImports").EnumerateArray())
            {
                var id = reference.GetString()!;
                if (!shared.Any(name => id == name || id.StartsWith(name + "/", StringComparison.Ordinal)))
                    throw new InvalidDataException($"COMPONENT_BUNDLE_RUNTIME_MISMATCH: frontend imports unprovided runtime '{id}'.");
            }
        }
        var actual = ComponentBundleIntegrity.Files(root).ToArray();
        if (!files.Contains("entry.js") || actual.Length != files.Count || actual.Any(path => !files.Contains(path)))
            throw new InvalidDataException("COMPONENT_BUNDLE_ASSET_MISMATCH: frontend output does not match build map.");
    }

    // 由实际构建引用的位置同样受严格相对路径约束。
    private static void RequireFile(string root, string relative)
    {
        if (!File.Exists(ComponentBundleIntegrity.Resolve(root, relative)))
            throw new InvalidDataException($"COMPONENT_BUNDLE_ASSET_MISSING: frontend '{relative}'.");
    }
}
