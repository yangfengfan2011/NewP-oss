using System.Text.Json;
using ICP.Platform.Modeling.Definitions;
using ICP.Platform.Modeling.Metadata;
using ICP.Platform.Modeling.Projection;
using ICP.Platform.Modeling.Validation;

namespace ICP.Platform.Modules.ConfigCenter.Persistence.Descriptors;

public sealed class ConfigCenterModelMetadataProvider
{
    private static readonly string ConfigItemPath = GetRepositoryPath(
        "modules",
        "ConfigCenter",
        "metadata",
        "models",
        "ConfigItem.model.json");

    private static readonly string StorageTypeMapPath = GetRepositoryPath(
        "server",
        "platform-modeling",
        "metadata",
        "storage-types.json");

    private readonly IStorageTypeMapLoader _typeMapLoader = new JsonStorageTypeMapLoader();
    private readonly ModelMetadataValidator _validator = new();
    private readonly ModelMetadataProjector _projector = new();

    public ModelDefinition LoadConfigItem()
    {
        // 新布局只把业务根对象暴露在文件顶层，这里负责把它还原成当前运行时文档，避免旧 `model` 包装继续成为权威源。
        var document = LoadModelDocument(ConfigItemPath);
        var typeMap = _typeMapLoader.LoadFromFile(StorageTypeMapPath);

        _validator.Validate(document, typeMap);

        return _projector.Project(document, typeMap, "SqlServer");
    }

    private static ModelMetadataDocument LoadModelDocument(string path)
    {
        var json = File.ReadAllText(path);
        using var jsonDocument = JsonDocument.Parse(json);

        var root = jsonDocument.RootElement;
        if (!root.TryGetProperty("schemaVersion", out var schemaVersionElement))
        {
            throw new InvalidOperationException($"模型文件缺少 schemaVersion：{path}");
        }

        if (!root.TryGetProperty("type", out var typeElement))
        {
            throw new InvalidOperationException($"模型文件缺少 type：{path}");
        }

        if (!string.Equals(typeElement.GetString(), "model", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"模型文件类型不匹配：{path}");
        }

        string? modelName = null;
        JsonElement modelElement = default;
        var businessRootCount = 0;

        foreach (var property in root.EnumerateObject())
        {
            if (property.NameEquals("schemaVersion") || property.NameEquals("type"))
            {
                continue;
            }

            businessRootCount++;
            modelName = property.Name;
            modelElement = property.Value;
        }

        if (businessRootCount != 1 || modelName is null)
        {
            throw new InvalidOperationException($"模型文件必须且只能包含一个业务根对象：{path}");
        }

        var model = modelElement.Deserialize<ModelMetadataModel>(new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true
        }) ?? throw new InvalidOperationException($"模型文件反序列化失败：{path}");

        return new ModelMetadataDocument
        {
            SchemaVersion = schemaVersionElement.GetString() ?? "1.0",
            ModuleId = "ConfigCenter",
            Model = new ModelMetadataModel
            {
                Name = modelName,
                Kind = model.Kind,
                BuiltinFields = model.BuiltinFields,
                Fields = model.Fields,
                LinkedTypes = model.LinkedTypes,
                Indexes = model.Indexes
            }
        };
    }

    private static string GetRepositoryPath(params string[] segments)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "server");
            if (Directory.Exists(candidate))
            {
                var pathSegments = new List<string> { current.FullName };
                pathSegments.AddRange(segments);
                return Path.GetFullPath(Path.Combine(pathSegments.ToArray()));
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("无法定位仓库根目录。");
    }
}
