using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ICP.Platform.PluginPlatform.ComponentBundles;

// build 与 inspect 共用固定序列化和安全路径；摘要从消费字节生成，不从机器路径生成。
internal static class ComponentBundleIntegrity
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    // 固定 JSON 字节不依赖操作系统换行。
    internal static byte[] Serialize<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value, Json);

    // 对任意资产流式计算摘要，避免将程序集或前端 chunk 完整物化。
    internal static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    // 小型索引的规范表示参与内容身份，字段和数组顺序由生成器固定。
    internal static string Hash<T>(T value) => Convert.ToHexStringLower(SHA256.HashData(Serialize(value)));

    // 相对路径采用单一协议形态，拒绝 Windows ADS、大小写碰撞和链接越界。
    internal static string Resolve(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || relative.Contains('\\') || relative.Contains(':')
            || relative.StartsWith('/') || relative.Split('/').Any(segment => segment is "" or "." or ".."))
            throw new InvalidDataException($"COMPONENT_BUNDLE_PATH_INVALID: '{relative}'.");
        var resolvedRoot = Path.GetFullPath(root);
        var path = Path.GetFullPath(Path.Combine(resolvedRoot, relative));
        if (!path.StartsWith(resolvedRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"COMPONENT_BUNDLE_PATH_INVALID: '{relative}'.");
        var current = resolvedRoot;
        if (Directory.Exists(current) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException($"COMPONENT_BUNDLE_PATH_INVALID: reparse point '{current}'.");
        foreach (var segment in relative.Split('/'))
        {
            current = Path.Combine(current, segment);
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException($"COMPONENT_BUNDLE_PATH_INVALID: reparse point '{current}'.");
        }
        return path;
    }

    // 枚举时先检查每一级目录；不先跟随链接递归，再试图收口结果。
    internal static IEnumerable<string> Files(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.TryPop(out var directory))
        {
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException($"COMPONENT_BUNDLE_PATH_INVALID: reparse point '{directory}'.");
            foreach (var file in Directory.EnumerateFiles(directory))
            {
                var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
                Resolve(root, relative);
                yield return relative;
            }
            foreach (var child in Directory.EnumerateDirectories(directory)) pending.Push(child);
        }
    }

    // 派生 transport 仅改变受控资产位置；其他声明完全保留原 authoring。
    internal static JsonNode Transport(JsonNode authoring)
    {
        var result = authoring.DeepClone();
        if (result["entryAssembly"] is JsonValue assembly)
            result["entryAssembly"] = "backend/" + Path.GetFileName(assembly.GetValue<string>().Replace('\\', '/'));
        if (result["frontend"] is JsonObject frontend && frontend["enabled"]?.GetValue<bool>() != false)
            frontend["entry"] = "frontend/entry.js";
        return result;
    }
}
