using ICP.Platform.Modeling.Definitions;
using ICP.Platform.Modeling.Metadata;

namespace ICP.Platform.Modeling.Projection;

/// <summary>
/// 将模型元数据文档投影为运行时可消费的模型定义。
/// </summary>
public sealed class ModelMetadataProjector
{
    /// <summary>
    /// 在不依赖 provider 映射的情况下完成最小投影。
    /// </summary>
    public ModelDefinition Project(ModelMetadataDocument document)
    {
        return Project(document, null, null);
    }

    /// <summary>
    /// 根据类型映射与 provider 信息完成带存储类型的投影。
    /// </summary>
    public ModelDefinition Project(ModelMetadataDocument document, StorageTypeMapDocument? typeMap, string? provider)
    {
        ArgumentNullException.ThrowIfNull(document);

        var fields = new List<FieldDefinition>();
        var indexes = new List<IndexDefinition>();
        var kind = ParseKind(document.Model.Kind);

        if (kind == ModelKind.Entity)
        {
            AddEntityBuiltins(document.Model, fields, indexes);
        }
        else
        {
            AddLinkBuiltins(document.Model, fields, indexes);
        }

        foreach (var field in document.Model.Fields)
        {
            if (IsBuiltinFieldReference(field.Name))
            {
                continue;
            }

            if (field.Storage is null)
            {
                continue;
            }

            fields.Add(ProjectField(field, typeMap, provider));
        }

        indexes.AddRange(document.Model.Indexes.Select(index => new IndexDefinition(index.Name, index.Fields, index.Unique)));

        return new ModelDefinition(
            document.Model.Name,
            ToSnakeCasePlural(document.Model.Name),
            kind,
            fields,
            indexes);
    }

    private static void AddEntityBuiltins(
        ModelMetadataModel model,
        ICollection<FieldDefinition> fields,
        ICollection<IndexDefinition> indexes)
    {
        fields.Add(new FieldDefinition("EID", "EID", FieldKind.Scalar, FieldValueType.ObjectId, "string", true, true, false));

        if (model.BuiltinFields.Contains("ENAME", StringComparer.OrdinalIgnoreCase))
        {
            fields.Add(new FieldDefinition("ENAME", "ENAME", FieldKind.Scalar, FieldValueType.Text, "string", true, false, false));
        }

        if (model.BuiltinFields.Contains("ECODE", StringComparer.OrdinalIgnoreCase))
        {
            fields.Add(new FieldDefinition("ECODE", "ECODE", FieldKind.Scalar, FieldValueType.Text, "string", true, false, false));
        }

        indexes.Add(new IndexDefinition($"UX_{model.Name}_EID", ["EID"], true));
    }

    private static void AddLinkBuiltins(
        ModelMetadataModel model,
        ICollection<FieldDefinition> fields,
        ICollection<IndexDefinition> indexes)
    {
        fields.Add(new FieldDefinition("LID", "LID", FieldKind.Scalar, FieldValueType.ObjectId, "string", true, true, false));
        fields.Add(new FieldDefinition("PEID", "PEID", FieldKind.Reference, FieldValueType.ObjectId, "string", true, false, false));
        fields.Add(new FieldDefinition("CEID", "CEID", FieldKind.Reference, FieldValueType.ObjectId, "string", true, false, false));

        indexes.Add(new IndexDefinition($"UX_{model.Name}_LID", ["LID"], true));
        indexes.Add(new IndexDefinition($"IX_{model.Name}_PEID", ["PEID"], false));
        indexes.Add(new IndexDefinition($"IX_{model.Name}_CEID", ["CEID"], false));
    }

    private static FieldDefinition ProjectField(
        ModelMetadataField field,
        StorageTypeMapDocument? typeMap,
        string? provider)
    {
        var storageType = ResolveStoreType(field.Storage!, typeMap, provider);
        var valueType = ResolveValueType(field.Storage!.Type);
        var fieldKind = valueType == FieldValueType.ObjectId ? FieldKind.Reference : FieldKind.Scalar;

        return new FieldDefinition(
            field.Name,
            field.Name,
            fieldKind,
            valueType,
            storageType,
            !field.Storage.Nullable,
            false,
            false);
    }

    private static string ResolveStoreType(
        ModelMetadataStorageSection storage,
        StorageTypeMapDocument? typeMap,
        string? provider)
    {
        if (typeMap is null || string.IsNullOrWhiteSpace(provider))
        {
            return storage.Type;
        }

        if (!typeMap.Types.TryGetValue(storage.Type, out var definition))
        {
            return storage.Type;
        }

        if (!definition.Providers.TryGetValue(provider, out var providerDefinition))
        {
            return storage.Type;
        }

        return providerDefinition.StoreType;
    }

    private static FieldValueType ResolveValueType(string storageType)
    {
        return storageType.ToLowerInvariant() switch
        {
            "text" => FieldValueType.Description,
            "bool" => FieldValueType.Boolean,
            "date" => FieldValueType.DateTime,
            "datetime" => FieldValueType.DateTime,
            "guid" => FieldValueType.Text,
            "json" => FieldValueType.Description,
            _ => FieldValueType.Text
        };
    }

    private static bool IsBuiltinFieldReference(string fieldName)
    {
        return string.Equals(fieldName, "EID", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(fieldName, "ENAME", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(fieldName, "ECODE", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(fieldName, "LID", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(fieldName, "PEID", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(fieldName, "CEID", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(fieldName, "LSequence", StringComparison.OrdinalIgnoreCase);
    }

    private static ModelKind ParseKind(string kind)
    {
        return Enum.TryParse<ModelKind>(kind, true, out var parsedKind)
            ? parsedKind
            : throw new InvalidOperationException("不支持的模型类型。");
    }

    private static string ToSnakeCasePlural(string name)
    {
        var buffer = new List<char>(name.Length + 4);

        for (var i = 0; i < name.Length; i++)
        {
            var current = name[i];
            if (char.IsUpper(current) && i > 0)
            {
                buffer.Add('_');
            }

            buffer.Add(char.ToLowerInvariant(current));
        }

        buffer.Add('s');
        return new string([.. buffer]);
    }
}
