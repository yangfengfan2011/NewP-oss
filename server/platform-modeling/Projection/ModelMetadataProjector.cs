using ICP.Platform.Modeling.Definitions;
using ICP.Platform.Modeling.Metadata;

namespace ICP.Platform.Modeling.Projection;

public sealed class ModelMetadataProjector
{
    private static readonly HashSet<string> EntityFixedFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "EID",
        "CreatedBy",
        "CreatedAt",
        "UpdatedBy",
        "UpdatedAt",
        "ENAME",
        "ECODE"
    };

    private static readonly HashSet<string> LinkFixedFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "LID",
        "PEID",
        "CEID",
        "LCreatedBy",
        "LCreatedAt",
        "LUpdatedBy",
        "LUpdatedAt",
        "LSequence"
    };

    public ModelDefinition Project(ModelMetadataDocument document)
    {
        return Project(document, null, null);
    }

    public ModelDefinition Project(
        ModelMetadataDocument document,
        StorageTypeMapDocument? typeMap,
        string? provider)
    {
        ArgumentNullException.ThrowIfNull(document);

        var fields = new List<FieldDefinition>();

        if (string.Equals(document.Model.Kind, "Entity", StringComparison.OrdinalIgnoreCase))
        {
            AddEntityFixedFields(fields, document.Model.BuiltinFields);
        }
        else if (string.Equals(document.Model.Kind, "Link", StringComparison.OrdinalIgnoreCase))
        {
            AddLinkFixedFields(fields, document.Model.BuiltinFields);
        }

        foreach (var field in document.Model.Fields)
        {
            if (field.Storage is null)
            {
                if (IsProjectedByKernel(document.Model.Kind, field.Name))
                {
                    // 固定字段允许只在 authoring 中补 UI/schema 信息，不要求重复声明 storage。
                    continue;
                }

                throw new InvalidOperationException($"Field '{field.Name}' must define storage.");
            }

            fields.Add(ProjectField(field, typeMap, provider));
        }

        var indexes = BuildIndexes(document);

        return new ModelDefinition(
            document.Model.Name,
            document.Model.Name,
            Enum.Parse<ModelKind>(document.Model.Kind, ignoreCase: true),
            fields,
            indexes);
    }

    private static FieldDefinition ProjectField(
        ModelMetadataField field,
        StorageTypeMapDocument? typeMap,
        string? provider)
    {
        var storage = field.Storage!;
        var valueType = ResolveValueType(field.Contract?.ValueType, storage.Type);

        var storeType = storage.Type;
        if (typeMap is not null && provider is not null)
        {
            if (typeMap.Types.TryGetValue(storage.Type, out var typeEntry)
                && typeEntry.Providers.TryGetValue(provider, out var providerMap))
            {
                // provider 专属 storeType 只来自映射文件，不回流到模型文件中。
                storeType = providerMap.StoreType;
            }
        }

        return new FieldDefinition(
            field.Name,
            field.Name,
            ResolveFieldKind(field),
            valueType,
            storeType,
            !storage.Nullable,
            false,
            false);
    }

    private static FieldKind ResolveFieldKind(ModelMetadataField field)
    {
        // model 数据源天然表示模型引用；provider 只有在 lookup 控件语义下才表示受控引用展示。
        // 这里仅影响 DTO 展示伴随字段，不改变 storage 投影或数据库结构。
        return string.Equals(field.DataSource?.Type, "model", StringComparison.OrdinalIgnoreCase)
            || (string.Equals(field.DataSource?.Type, "provider", StringComparison.OrdinalIgnoreCase)
                && string.Equals(field.UiType, "lookup", StringComparison.OrdinalIgnoreCase))
            ? FieldKind.Reference
            : FieldKind.Scalar;
    }

    private static bool IsProjectedByKernel(string modelKind, string fieldName)
    {
        return string.Equals(modelKind, "Entity", StringComparison.OrdinalIgnoreCase)
            ? EntityFixedFields.Contains(fieldName)
            : LinkFixedFields.Contains(fieldName);
    }

    // 固定字段的存储定义收口在 projector，避免模块元数据重复声明这些内建事实。
    private static void AddEntityFixedFields(List<FieldDefinition> fields, IReadOnlyCollection<string> builtinFields)
    {
        fields.Add(new FieldDefinition("EID", "EID", FieldKind.Scalar, FieldValueType.ObjectId, "string", true, true, false));
        fields.Add(new FieldDefinition("CreatedBy", "CreatedBy", FieldKind.Audit, FieldValueType.User, "string", false, false, true));
        fields.Add(new FieldDefinition("CreatedAt", "CreatedAt", FieldKind.Audit, FieldValueType.DateTime, "datetime", true, false, true));
        fields.Add(new FieldDefinition("UpdatedBy", "UpdatedBy", FieldKind.Audit, FieldValueType.User, "string", false, false, true));
        fields.Add(new FieldDefinition("UpdatedAt", "UpdatedAt", FieldKind.Audit, FieldValueType.DateTime, "datetime", false, false, true));

        if (builtinFields.Contains("ENAME", StringComparer.OrdinalIgnoreCase))
        {
            fields.Add(new FieldDefinition("ENAME", "ENAME", FieldKind.Scalar, FieldValueType.Text, "string", true, false, false));
        }

        if (builtinFields.Contains("ECODE", StringComparer.OrdinalIgnoreCase))
        {
            fields.Add(new FieldDefinition("ECODE", "ECODE", FieldKind.Scalar, FieldValueType.Text, "string", true, false, false));
        }
    }

    // Link 固定字段不仅包含两端引用，也包含关系对象自身的审计字段。
    private static void AddLinkFixedFields(List<FieldDefinition> fields, IReadOnlyCollection<string> builtinFields)
    {
        fields.Add(new FieldDefinition("LID", "LID", FieldKind.Scalar, FieldValueType.ObjectId, "string", true, true, false));
        fields.Add(new FieldDefinition("PEID", "PEID", FieldKind.Reference, FieldValueType.ObjectId, "string", true, false, false));
        fields.Add(new FieldDefinition("CEID", "CEID", FieldKind.Reference, FieldValueType.ObjectId, "string", true, false, false));
        fields.Add(new FieldDefinition("LCreatedBy", "LCreatedBy", FieldKind.Audit, FieldValueType.User, "string", false, false, true));
        fields.Add(new FieldDefinition("LCreatedAt", "LCreatedAt", FieldKind.Audit, FieldValueType.DateTime, "datetime", true, false, true));
        fields.Add(new FieldDefinition("LUpdatedBy", "LUpdatedBy", FieldKind.Audit, FieldValueType.User, "string", false, false, true));
        fields.Add(new FieldDefinition("LUpdatedAt", "LUpdatedAt", FieldKind.Audit, FieldValueType.DateTime, "datetime", false, false, true));

        if (builtinFields.Contains("LSequence", StringComparer.OrdinalIgnoreCase))
        {
            fields.Add(new FieldDefinition("LSequence", "LSequence", FieldKind.Scalar, FieldValueType.Text, "string", false, false, false));
        }
    }

    private static FieldValueType ResolveValueType(string? contractValueType, string storageType)
    {
        if (!string.IsNullOrWhiteSpace(contractValueType))
        {
            return contractValueType.Trim().ToLowerInvariant() switch
            {
                "enum" => FieldValueType.Enum,
                "objectid" => FieldValueType.ObjectId,
                "bool" => FieldValueType.Boolean,
                "datetime" => FieldValueType.DateTime,
                "description" => FieldValueType.Description,
                "text" => FieldValueType.Text,
                _ => FieldValueType.Text
            };
        }

        return MapStorageValueType(storageType);
    }

    private static FieldValueType MapStorageValueType(string storageType)
    {
        return storageType.ToLowerInvariant() switch
        {
            "string" => FieldValueType.Text,
            "text" => FieldValueType.Description,
            "json" => FieldValueType.Description,
            "bool" => FieldValueType.Boolean,
            "date" => FieldValueType.DateTime,
            "datetime" => FieldValueType.DateTime,
            "guid" => FieldValueType.ObjectId,
            _ => FieldValueType.Text
        };
    }

    private static IReadOnlyList<IndexDefinition> BuildIndexes(ModelMetadataDocument document)
    {
        var indexes = new List<IndexDefinition>();

        if (string.Equals(document.Model.Kind, "Entity", StringComparison.OrdinalIgnoreCase))
        {
            indexes.Add(new IndexDefinition($"UX_{document.Model.Name}_EID", ["EID"], true));
        }
        else if (string.Equals(document.Model.Kind, "Link", StringComparison.OrdinalIgnoreCase))
        {
            indexes.Add(new IndexDefinition($"UX_{document.Model.Name}_LID", ["LID"], true));
            indexes.Add(new IndexDefinition($"IX_{document.Model.Name}_PEID", ["PEID"], false));
            indexes.Add(new IndexDefinition($"IX_{document.Model.Name}_CEID", ["CEID"], false));
        }

        indexes.AddRange(document.Model.Indexes.Select(index =>
            new IndexDefinition(index.Name, index.Fields, index.Unique)));

        return indexes;
    }
}
