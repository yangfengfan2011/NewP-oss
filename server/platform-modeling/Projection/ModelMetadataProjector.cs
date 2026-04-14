using ICP.Platform.Modeling.Definitions;
using ICP.Platform.Modeling.Metadata;

namespace ICP.Platform.Modeling.Projection;

public sealed class ModelMetadataProjector
{
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
            // Entity 固定字段由内核补入，不要求每个模型文件重复声明。
            fields.Add(new FieldDefinition("EID", "EID", FieldKind.Scalar, FieldValueType.ObjectId, "string", true, true, false));

            if (document.Model.BuiltinFields.Contains("ENAME", StringComparer.OrdinalIgnoreCase))
            {
                fields.Add(new FieldDefinition("ENAME", "ENAME", FieldKind.Scalar, FieldValueType.Text, "string", true, false, false));
            }
        }
        else if (string.Equals(document.Model.Kind, "Link", StringComparison.OrdinalIgnoreCase))
        {
            // Link 固定字段定义关系对象自身标识以及两端端点。
            fields.Add(new FieldDefinition("LID", "LID", FieldKind.Scalar, FieldValueType.ObjectId, "string", true, true, false));
            fields.Add(new FieldDefinition("PEID", "PEID", FieldKind.Reference, FieldValueType.ObjectId, "string", true, false, false));
            fields.Add(new FieldDefinition("CEID", "CEID", FieldKind.Reference, FieldValueType.ObjectId, "string", true, false, false));
        }

        foreach (var field in document.Model.Fields)
        {
            var storage = field.Storage ?? throw new InvalidOperationException($"Field '{field.Name}' must define storage.");
            fields.Add(ProjectField(field.Name, storage, typeMap, provider));
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
        string fieldName,
        ModelMetadataStorageSection storage,
        StorageTypeMapDocument? typeMap,
        string? provider)
    {
        // 首轮 projector 先保持显式映射，后续再扩展更多 storage.type。
        var valueType = storage.Type.ToLowerInvariant() switch
        {
            "string" => FieldValueType.Text,
            "text" => FieldValueType.Description,
            _ => FieldValueType.Text
        };

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
            fieldName,
            fieldName,
            FieldKind.Scalar,
            valueType,
            storeType,
            !storage.Nullable,
            false,
            false);
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
