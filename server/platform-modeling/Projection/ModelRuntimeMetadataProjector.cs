using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ICP.Platform.Modeling.Definitions;
using ICP.Platform.Modeling.Metadata;
using ICP.Platform.Modeling.Ownership;
using ICP.Platform.Modeling.Validation;

namespace ICP.Platform.Modeling.Projection;

/// <summary>
/// 把已校验的 Model authoring 字节投影为 Shell 消费的规范 runtime Model JSON。
/// </summary>
public sealed class ModelRuntimeMetadataProjector
{
    private static readonly JsonSerializerOptions RuntimeJsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly JsonModelMetadataDocumentLoader _loader = new();
    private readonly ModelMetadataProjector _metadataProjector = new();
    private readonly ModelMetadataValidator _validator = new();

    /// <summary>
    /// 校验 Model authoring，补齐规范固定字段目录，并规范显式服务端字段的表单模式。
    /// </summary>
    /// <param name="authoringJson">候选快照中的原始 UTF-8 Model JSON。</param>
    /// <param name="sourcePath">用于恢复模块与模型位置的候选内路径。</param>
    /// <returns>派生 runtime 字节；不修改传入的 authoring 字节。</returns>
    public byte[] Project(ReadOnlyMemory<byte> authoringJson, string sourcePath)
    {
        var document = _loader.Load(authoringJson.Span, sourcePath);
        _validator.Validate(document);
        var fixedFields = _metadataProjector.ProjectFixedFields(document);

        var root = JsonNode.Parse(authoringJson.Span)?.AsObject()
            ?? throw new InvalidOperationException($"Model metadata document must be an object: {sourcePath}");
        var model = root[document.Model.Name]?.AsObject()
            ?? throw new InvalidOperationException(
                $"Module '{document.ModuleId}' runtime model '{document.Model.Name}' is missing its business root.");
        var fields = model["fields"]?.AsArray()
            ?? throw new InvalidOperationException(
                $"Module '{document.ModuleId}' runtime model '{document.Model.Name}' is missing fields.");
        var fixedFieldsByName = fixedFields.ToDictionary(static field => field.Name, StringComparer.OrdinalIgnoreCase);
        var explicitFieldNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var fieldNode in fields)
        {
            if (fieldNode is not JsonObject field
                || field["name"]?.GetValue<string>() is not { } fieldName)
            {
                continue;
            }

            explicitFieldNames.Add(fieldName);
            if (!fixedFieldsByName.TryGetValue(fieldName, out var fieldDefinition))
            {
                continue;
            }

            CompleteCanonicalSemantics(field, fieldDefinition);
            if (PlatformModelFieldOwnership.IsServerOwned(fieldDefinition, hasExplicitAuthoring: true))
            {
                var form = field["form"] as JsonObject ?? new JsonObject();
                field["form"] = form;
                NormalizeMode(form, "create");
                NormalizeMode(form, "edit");
            }
        }

        foreach (var fieldDefinition in fixedFields)
        {
            if (!explicitFieldNames.Contains(fieldDefinition.Name))
            {
                fields.Add(CreateSyntheticField(fieldDefinition));
            }
        }

        return Encoding.UTF8.GetBytes(root.ToJsonString(RuntimeJsonOptions) + Environment.NewLine);
    }

    // 缺失固定字段只补成可发现目录项；create/edit 一律隐藏，避免目录完整性扩大表单输入面。
    private static JsonObject CreateSyntheticField(FieldDefinition field) =>
        CompleteCanonicalSemantics(
            new JsonObject
            {
                ["name"] = field.Name,
                ["form"] = new JsonObject
                {
                    ["create"] = new JsonObject { ["mode"] = "hidden" },
                    ["edit"] = new JsonObject { ["mode"] = "hidden" }
                }
            },
            field);

    // 显式固定字段保留 authoring 值；这里只填其缺失的规范类型和存储语义。
    private static JsonObject CompleteCanonicalSemantics(JsonObject runtimeField, FieldDefinition field)
    {
        var hasAuthoredTypeInference = runtimeField["dataSource"] is JsonObject
            || runtimeField["contract"] is JsonObject authoredContract
                && authoredContract.ContainsKey("valueType");
        var contract = runtimeField["contract"] as JsonObject ?? new JsonObject();
        runtimeField["contract"] = contract;
        if (!contract.ContainsKey("valueType"))
        {
            contract["valueType"] = ContractValueType(field.ValueType);
        }

        var storage = runtimeField["storage"] as JsonObject ?? new JsonObject();
        runtimeField["storage"] = storage;
        if (!storage.ContainsKey("type"))
        {
            storage["type"] = field.StoreType;
        }
        if (!storage.ContainsKey("nullable"))
        {
            storage["nullable"] = !field.Required;
        }
        if (!runtimeField.ContainsKey("uiType") && !hasAuthoredTypeInference)
        {
            runtimeField["uiType"] = UiType(field.ValueType);
        }

        return runtimeField;
    }

    // 规范值类型沿用 authoring contract 的 lower camel case 词汇。
    private static string ContractValueType(FieldValueType valueType) => valueType switch
    {
        FieldValueType.CalendarDate => "calendarDate",
        FieldValueType.DateTime => "dateTime",
        FieldValueType.ObjectId => "objectId",
        _ => char.ToLowerInvariant(valueType.ToString()[0]) + valueType.ToString()[1..]
    };

    // 固定字段只需要稳定的通用展示意图；表单可见性仍完全由 form mode 控制。
    private static string UiType(FieldValueType valueType) => valueType switch
    {
        FieldValueType.Boolean => "switch",
        FieldValueType.CalendarDate => "date",
        FieldValueType.DateTime or FieldValueType.Instant => "dateTime",
        FieldValueType.Description => "textarea",
        FieldValueType.Integer => "number",
        FieldValueType.Enum => "select",
        _ => "text"
    };

    // 只有显式 readonly 能让服务端字段进入表单；其他已校验模式一律规范为 hidden。
    private static void NormalizeMode(JsonObject form, string scenario)
    {
        var rule = form[scenario] as JsonObject ?? new JsonObject();
        form[scenario] = rule;
        rule["mode"] = string.Equals(
            rule["mode"]?.GetValue<string>(),
            "readonly",
            StringComparison.OrdinalIgnoreCase)
            ? "readonly"
            : "hidden";
    }
}
