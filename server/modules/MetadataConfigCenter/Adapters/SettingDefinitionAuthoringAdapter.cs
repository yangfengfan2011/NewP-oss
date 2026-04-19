using System.Text.Json;
using System.Text.Encodings.Web;
using ICP.Platform.Modules.MetadataConfigCenter.Models;

namespace ICP.Platform.Modules.MetadataConfigCenter.Adapters;

public class SettingDefinitionAuthoringAdapter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true
    };

    public IReadOnlyList<SettingDefinition> ParseDefinitions(string sourceFile, string content)
    {
        using var document = JsonDocument.Parse(content);
        var root = document.RootElement;

        if (!root.TryGetProperty("type", out var typeElement)
            || typeElement.ValueKind != JsonValueKind.String
            || !string.Equals(typeElement.GetString(), "settings", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Settings metadata file '{sourceFile}' must declare type = 'settings'.");
        }

        if (!root.TryGetProperty("settings", out var settingsElement) || settingsElement.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        var definitions = new List<SettingDefinition>();
        // settings 改为对象映射后，key 自身就是稳定标识；
        // adapter 在这里负责把 authoring 结构还原成面向 UI/校验的行式对象。
        foreach (var setting in settingsElement.EnumerateObject())
        {
            var item = setting.Value;
            definitions.Add(new SettingDefinition
            {
                Key = setting.Name,
                DisplayName = TryGetString(item, "displayName"),
                Description = TryGetOptionalString(item, "description"),
                ValueType = TryGetString(item, "valueType"),
                DefaultValue = TryGetString(item, "value"),
                Required = TryGetBoolean(item, "required"),
                IsSensitive = TryGetBoolean(item, "isSensitive"),
                SupportsOverride = TryGetBoolean(item, "supportsOverride"),
                Group = TryGetString(item, "group"),
                SourceFile = sourceFile
            });
        }

        return definitions;
    }

    public void Validate(IEnumerable<SettingDefinition> definitions)
    {
        var definitionList = definitions.ToList();
        // key 去重按大小写不敏感处理，避免同一设置项因命名大小写漂移被误认为两个定义。
        var duplicateKey = definitionList
            .GroupBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateKey is not null)
        {
            throw new InvalidOperationException($"Duplicate setting key detected: {duplicateKey.Key}");
        }

        if (definitionList.Any(item => string.IsNullOrWhiteSpace(item.Group)))
        {
            throw new InvalidOperationException("Setting definition group is required.");
        }

        if (definitionList.Any(item => string.IsNullOrWhiteSpace(item.DefaultValue)))
        {
            throw new InvalidOperationException("Setting definition default value is required.");
        }
    }

    public IReadOnlyList<GeneratedSettingDefinitionFile> BuildFileContents(string moduleId, IEnumerable<SettingDefinition> definitions)
    {
        var files = new List<GeneratedSettingDefinitionFile>();

        foreach (var group in definitions.GroupBy(item => NormalizeSourceFile(moduleId, item.SourceFile), StringComparer.OrdinalIgnoreCase))
        {
            var items = group.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase).ToList();
            var document = new
            {
                schemaVersion = "1.0",
                type = "settings",
                settings = items.ToDictionary(
                    item => item.Key,
                    item => new
                    {
                        group = item.Group,
                        displayName = item.DisplayName,
                        description = item.Description,
                        valueType = item.ValueType,
                        value = item.DefaultValue,
                        required = item.Required,
                        isSensitive = item.IsSensitive,
                        supportsOverride = item.SupportsOverride
                    },
                    StringComparer.OrdinalIgnoreCase)
            };

            files.Add(new GeneratedSettingDefinitionFile
            {
                SourceFile = group.Key,
                Content = JsonSerializer.Serialize(document, JsonOptions)
            });
        }

        return files;
    }

    private static string NormalizeSourceFile(string moduleId, string sourceFile)
    {
        if (!string.IsNullOrWhiteSpace(sourceFile))
        {
            return sourceFile;
        }

        // 缺少源文件名时回退到模块级默认文件名，避免保存阶段生成随机文件路径。
        return $"{moduleId}.settings.json";
    }

    public class GeneratedSettingDefinitionFile
    {
        public string SourceFile { get; set; } = string.Empty;

        public string Content { get; set; } = string.Empty;
    }

    private static string TryGetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? string.Empty
            : string.Empty;
    }

    private static string? TryGetOptionalString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private static bool TryGetBoolean(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.True
            ? true
            : element.TryGetProperty(propertyName, out property) && property.ValueKind == JsonValueKind.False
                ? false
                : false;
    }
}
