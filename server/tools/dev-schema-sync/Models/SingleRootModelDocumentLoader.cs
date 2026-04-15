using System.Text.Json;
using ICP.Platform.Modeling.Metadata;

namespace ICP.Platform.Tools.DevSchemaSync.Models;

// 工具侧专用 loader，适配当前仓库的 `{ schemaVersion, type, <业务根对象> }` authoring 形状。
public sealed class SingleRootModelDocumentLoader
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public ModelMetadataDocument LoadFromFile(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var json = File.ReadAllText(fullPath);
        using var document = JsonDocument.Parse(json);

        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException($"模型文档必须是对象: {fullPath}");
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
        if (!string.Equals(type, "model", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"模型文档 type 必须是 model: {fullPath}");
        }

        var businessRootName = string.Empty;
        JsonElement businessRootElement = default;
        var businessRootCount = 0;

        foreach (var property in root.EnumerateObject())
        {
            if (property.NameEquals("schemaVersion") || property.NameEquals("type"))
            {
                continue;
            }

            businessRootName = property.Name;
            businessRootElement = property.Value;
            businessRootCount++;
        }

        if (businessRootCount != 1)
        {
            throw new InvalidOperationException($"模型文档必须且只能包含一个业务根对象: {fullPath}");
        }

        if (businessRootElement.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException($"业务根对象必须是对象: {fullPath}");
        }

        var model = JsonSerializer.Deserialize<ModelMetadataModel>(businessRootElement.GetRawText(), Options)
            ?? throw new InvalidOperationException($"业务根对象反序列化失败: {fullPath}");

        // 业务根对象名来自文件结构而不是 JSON 内容，因此这里显式回填 Name。
        return new ModelMetadataDocument
        {
            SchemaVersion = schemaVersionElement.GetString() ?? "1.0",
            ModuleId = ExtractModuleId(fullPath),
            Model = new ModelMetadataModel
            {
                Name = businessRootName,
                HistoryName = model.HistoryName,
                Kind = model.Kind,
                BuiltinFields = model.BuiltinFields,
                Fields = model.Fields,
                LinkedTypes = model.LinkedTypes,
                Indexes = model.Indexes
            }
        };
    }

    private static string ExtractModuleId(string path)
    {
        var segments = Path.GetFullPath(path)
            .Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);

        for (var index = 0; index < segments.Length - 4; index++)
        {
            if (!string.Equals(segments[index], "modules", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!string.Equals(segments[index + 2], "metadata", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(segments[index + 3], "models", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return segments[index + 1];
        }

        throw new InvalidOperationException($"无法从路径提取 moduleId: {path}");
    }
}
