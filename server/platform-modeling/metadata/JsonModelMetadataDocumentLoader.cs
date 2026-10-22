using System.Text.Json;

namespace ICP.Platform.Modeling.Metadata;

public sealed class JsonModelMetadataDocumentLoader : IModelMetadataDocumentLoader
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public ModelMetadataDocument LoadFromFile(string path)
    {
        var fullPath = Path.GetFullPath(path);
        return Load(File.ReadAllBytes(fullPath), fullPath);
    }

    /// <summary>
    /// 从调用方已固定的 UTF-8 工件字节加载模型，避免验证后重新打开路径。
    /// </summary>
    public ModelMetadataDocument Load(ReadOnlySpan<byte> utf8Json, string sourcePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        var fullPath = Path.GetFullPath(sourcePath);
        using var document = JsonDocument.Parse(utf8Json.ToArray());

        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException($"模型文档必须是对象: {fullPath}");
        }

        // 兼容旧式整文档形状，避免存量测试与工具链在本轮收口期间一起失稳。
        if (root.TryGetProperty("moduleId", out _) && root.TryGetProperty("model", out _))
        {
            return JsonSerializer.Deserialize<ModelMetadataDocument>(utf8Json, Options)
                ?? throw new InvalidOperationException($"Failed to deserialize model metadata: {fullPath}");
        }

        if (!root.TryGetProperty("schemaVersion", out var schemaVersionElement) || schemaVersionElement.ValueKind != JsonValueKind.String)
        {
            throw new InvalidOperationException($"模型文档缺少有效的 schemaVersion: {fullPath}");
        }

        if (!root.TryGetProperty("type", out var typeElement) || typeElement.ValueKind != JsonValueKind.String)
        {
            throw new InvalidOperationException($"模型文档缺少有效的 type: {fullPath}");
        }

        var type = typeElement.GetString();
        if (!string.Equals(type, "model", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"模型文档 type 必须是 model: {fullPath}");
        }

        var businessRoot = GetSingleBusinessRoot(root, fullPath);
        if (businessRoot.Value.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException($"模型业务根对象必须是对象: {fullPath}");
        }

        var fileStem = GetModelFileStem(fullPath);
        if (!string.Equals(fileStem, businessRoot.Name, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"模型文件 '{fullPath}' 的文件名 '{fileStem}' 与业务根对象 '{businessRoot.Name}' 不一致。");
        }

        var model = JsonSerializer.Deserialize<ModelMetadataModel>(businessRoot.Value.GetRawText(), Options)
            ?? throw new InvalidOperationException($"业务根对象反序列化失败: {fullPath}");

        return new ModelMetadataDocument
        {
            SchemaVersion = schemaVersionElement.GetString() ?? "1.0",
            ModuleId = ExtractModuleId(fullPath),
            Model = new ModelMetadataModel
            {
                Name = businessRoot.Name,
                HistoryName = model.HistoryName,
                Kind = model.Kind,
                BuiltinFields = model.BuiltinFields,
                Fields = model.Fields,
                LinkedTypes = model.LinkedTypes,
                LinkQuery = model.LinkQuery,
                Indexes = model.Indexes,
                ForeignKeys = model.ForeignKeys
            }
        };
    }

    private static JsonProperty GetSingleBusinessRoot(JsonElement root, string fullPath)
    {
        JsonProperty businessRoot = default;
        var businessRootCount = 0;

        foreach (var property in root.EnumerateObject())
        {
            if (property.NameEquals("schemaVersion") || property.NameEquals("type"))
            {
                continue;
            }

            businessRoot = property;
            businessRootCount++;
        }

        if (businessRootCount != 1)
        {
            throw new InvalidOperationException($"模型文档必须且只能包含一个业务根对象: {fullPath}");
        }

        return businessRoot;
    }

    // 源码目录读 Capsule 身份；运行时与已固定包路径继续使用其模块身份目录。
    private static string ExtractModuleId(string path)
    {
        var metadataDirectory = new DirectoryInfo(Path.GetDirectoryName(path)!);
        while (metadataDirectory is not null)
        {
            if (string.Equals(metadataDirectory.Name, "metadata", StringComparison.OrdinalIgnoreCase)
                && metadataDirectory.Parent is not null
                && (File.Exists(Path.Combine(metadataDirectory.Parent.FullName, "newp.plugin.json"))
                    || IsFrontendAuthoringDirectory(metadataDirectory.Parent)))
            {
                return ModuleAuthoringPaths.ReadModuleId(metadataDirectory.Parent.FullName);
            }

            metadataDirectory = metadataDirectory.Parent;
        }

        var segments = Path.GetFullPath(path)
            .Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);

        for (var index = 0; index < segments.Length - 2; index++)
        {
            if (string.Equals(segments[index], "modules", StringComparison.OrdinalIgnoreCase)
                && string.Equals(segments[index + 2], "metadata", StringComparison.OrdinalIgnoreCase))
            {
                return segments[index + 1];
            }

            // 外部包激活后模型位于 packages/<moduleId>/<version>/metadata；
            // 这里只恢复路径声明的 owner，canonical plan 仍会在持久化计划阶段校验 owner 一致性。
            if (index < segments.Length - 3
                && string.Equals(segments[index], "packages", StringComparison.OrdinalIgnoreCase)
                && string.Equals(segments[index + 3], "metadata", StringComparison.OrdinalIgnoreCase))
            {
                return segments[index + 1];
            }
        }

        throw new InvalidOperationException($"无法从路径提取 moduleId: {path}");
    }

    // 历史 metadata 可以位于分组目录内；只在源码 frontend/modules 范围恢复 slug owner。
    private static bool IsFrontendAuthoringDirectory(DirectoryInfo directory)
    {
        for (DirectoryInfo? current = directory; current is not null; current = current.Parent)
            if (current.Name == "modules" && current.Parent?.Name == "frontend") return true;
        return false;
    }

    private static string GetModelFileStem(string path)
    {
        var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(path);
        const string suffix = ".model";
        return fileNameWithoutExtension.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
            ? fileNameWithoutExtension[..^suffix.Length]
            : fileNameWithoutExtension;
    }
}
