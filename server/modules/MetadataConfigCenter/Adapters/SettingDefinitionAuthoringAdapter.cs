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
        var group = TryGetString(root, "group");

        if (!root.TryGetProperty("definitions", out var definitionsElement) || definitionsElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var definitions = new List<SettingDefinition>();
        // group 作为文件级元数据挂到每条定义上，
        // 这样后续校验和回写都能基于单条定义工作，而不必反复回看原始 JSON 结构。
        foreach (var item in definitionsElement.EnumerateArray())
        {
            definitions.Add(new SettingDefinition
            {
                Key = TryGetString(item, "key"),
                DisplayName = TryGetString(item, "displayName"),
                Description = TryGetOptionalString(item, "description"),
                ValueType = TryGetString(item, "valueType"),
                DefaultValue = TryGetString(item, "defaultValue"),
                Required = TryGetBoolean(item, "required"),
                IsSensitive = TryGetBoolean(item, "isSensitive"),
                SupportsOverride = TryGetBoolean(item, "supportsOverride"),
                Group = group,
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
            var distinctGroups = items
                .Select(item => item.Group)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            // 单个源文件只允许回写一个 group，确保 authoring 文件可以稳定重建，
            // 而不是把多个逻辑分组混进同一个 JSON 后再依赖隐式顺序解析。
            if (distinctGroups.Count != 1)
            {
                throw new InvalidOperationException($"Source file {group.Key} contains multiple groups and cannot be written deterministically.");
            }

            var document = new
            {
                moduleId,
                type = "settings",
                group = distinctGroups[0],
                definitions = items.Select(item => new
                {
                    key = item.Key,
                    displayName = item.DisplayName,
                    description = item.Description,
                    valueType = item.ValueType,
                    defaultValue = item.DefaultValue,
                    required = item.Required,
                    isSensitive = item.IsSensitive,
                    supportsOverride = item.SupportsOverride
                }).ToList()
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
