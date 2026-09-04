using ICP.Platform.Modeling.Definitions;
using ICP.Platform.Modeling.DisplayPlanning;
using ICP.Platform.Modeling.Metadata;

namespace ICP.Platform.Modeling.Validation;

public sealed class ModelMetadataValidator
{
    private static readonly HashSet<string> EntityKernelFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "EID",
        "CreatedBy",
        "CreatedAt",
        "UpdatedBy",
        "UpdatedAt"
    };

    // 可选固定字段必须按模型类型分组使用，不能跨 kind 混用。
    private static readonly HashSet<string> EntityBuiltinFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "ENAME",
        "ECODE"
    };

    private static readonly HashSet<string> LinkBuiltinFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "LSequence"
    };

    private static readonly HashSet<string> LinkKernelFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "LID",
        "PEID",
        "CEID",
        "LCreatedBy",
        "LCreatedAt",
        "LUpdatedBy",
        "LUpdatedAt"
    };

    public void Validate(ModelMetadataDocument document)
    {
        Validate(document, null);
    }

    public void Validate(ModelMetadataDocument document, StorageTypeMapDocument? typeMap)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (string.IsNullOrWhiteSpace(document.ModuleId))
        {
            throw new InvalidOperationException("Model metadata document must define moduleId.");
        }

        if (string.IsNullOrWhiteSpace(document.Model.Name))
        {
            throw new InvalidOperationException("Model metadata document must define model.name.");
        }

        if (!string.Equals(document.Model.Kind, "Entity", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(document.Model.Kind, "Link", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Model '{document.Model.Name}' has unsupported kind '{document.Model.Kind}'.");
        }

        var allowedBuiltinFields = string.Equals(document.Model.Kind, "Entity", StringComparison.OrdinalIgnoreCase)
            ? EntityBuiltinFields
            : LinkBuiltinFields;

        var invalidBuiltinField = document.Model.BuiltinFields
            .FirstOrDefault(field => !allowedBuiltinFields.Contains(field));

        if (invalidBuiltinField is not null)
        {
            throw new InvalidOperationException(
                $"Model '{document.Model.Name}' kind '{document.Model.Kind}' does not allow builtin field '{invalidBuiltinField}'.");
        }

        if (string.Equals(document.Model.Kind, "Link", StringComparison.OrdinalIgnoreCase)
            && document.Model.LinkedTypes.Count == 0)
        {
            // Link 模型必须预先声明允许的父子类型组合。
            throw new InvalidOperationException($"Link model '{document.Model.Name}' must define linkedTypes.");
        }

        ValidateFieldContracts(document, allowedBuiltinFields);
        ValidateIndexContracts(document);
        ValidateForeignKeyContracts(document);

        if (typeMap is null)
        {
            return;
        }

        // storage.type 必须先在平台类型映射里可解析，后续投影才能继续。
        foreach (var field in document.Model.Fields)
        {
            var storage = field.Storage;
            if (storage is null)
            {
                continue;
            }

            if (!typeMap.Types.ContainsKey(storage.Type))
            {
                throw new InvalidOperationException(
                    $"Field '{field.Name}' references unknown storage.type '{storage.Type}'.");
            }
        }
    }

    // 字段名重复、内核固定字段 storage 覆写和 display 规则错误都必须在 authoring validator 阶段拦住。
    private static void ValidateFieldContracts(
        ModelMetadataDocument document,
        HashSet<string> allowedBuiltinFields)
    {
        var seenFieldNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in document.Model.Fields)
        {
            if (!seenFieldNames.Add(field.Name))
            {
                throw new InvalidOperationException(
                    $"Model '{document.Model.Name}' contains duplicate field '{field.Name}'.");
            }

            if (field.Storage is not null && IsKernelManagedField(document.Model.Kind, field.Name, allowedBuiltinFields))
            {
                throw new InvalidOperationException(
                    $"Field '{field.Name}' is kernel-managed and must not redefine storage.");
            }

            ValidateTemporalContract(field);
        }

        ValidateDisplayRules(document);
    }

    // CalendarDate 与 Instant 的 contract/storage 组合必须显式且唯一，避免 provider 从物理类型反推值语义。
    private static void ValidateTemporalContract(ModelMetadataField field)
    {
        var valueType = field.Contract?.ValueType;
        var storageType = field.Storage?.Type;
        var matches = valueType?.Trim().ToLowerInvariant() switch
        {
            "calendardate" => string.Equals(storageType, "date", StringComparison.OrdinalIgnoreCase),
            "instant" => string.Equals(storageType, "datetime", StringComparison.OrdinalIgnoreCase),
            _ => true
        };
        if (!matches)
        {
            throw new InvalidOperationException(
                $"Field '{field.Name}' contract.valueType '{valueType}' is incompatible with storage.type '{storageType}'.");
        }
    }

    // authoring validator 只消费统一 planning module 的结果，不再维护平行策略判据。
    private static void ValidateDisplayRules(ModelMetadataDocument document)
    {
        var result = new ModelDisplayProjectionPlanner().Compile(document);
        if (!result.IsSuccess)
        {
            throw new InvalidOperationException(result.Diagnostics[0].Message);
        }
    }

    private static void ValidateIndexContracts(ModelMetadataDocument document)
    {
        var availableFields = CollectAvailableFieldNames(document);
        foreach (var index in document.Model.Indexes)
        {
            foreach (var fieldName in index.Fields)
            {
                if (!availableFields.Contains(fieldName))
                {
                    throw new InvalidOperationException(
                        $"Index '{index.Name}' references unknown field '{fieldName}'.");
                }
            }

            if (index.IncludeNullValues && !index.Unique)
            {
                throw new InvalidOperationException(
                    $"Index '{index.Name}' can use includeNullValues only when unique is true.");
            }

            if (index.IncludeNullValues && !index.Fields.Any(fieldName => IsNullableField(document, fieldName)))
            {
                throw new InvalidOperationException(
                    $"Index '{index.Name}' can use includeNullValues only when it contains a nullable field.");
            }
        }
    }

    // 内核与 builtin 字段固定为必填；只有显式 storage nullable 字段能触发 nullable unique scope。
    private static bool IsNullableField(ModelMetadataDocument document, string fieldName) =>
        document.Model.Fields.Any(field =>
            string.Equals(field.Name, fieldName, StringComparison.OrdinalIgnoreCase)
            && field.Storage?.Nullable == true);

    private static void ValidateForeignKeyContracts(ModelMetadataDocument document)
    {
        var availableFields = CollectAvailableFieldNames(document);
        foreach (var foreignKey in document.Model.ForeignKeys)
        {
            if (string.IsNullOrWhiteSpace(foreignKey.Name)
                || foreignKey.Fields.Count == 0
                || foreignKey.Fields.Any(field => !availableFields.Contains(field))
                || string.IsNullOrWhiteSpace(foreignKey.PrincipalModel)
                || foreignKey.PrincipalFields.Count == 0
                || foreignKey.Fields.Count != foreignKey.PrincipalFields.Count
                || !Enum.TryParse<ForeignKeyDeleteBehavior>(foreignKey.OnDelete, true, out _))
            {
                throw new InvalidOperationException(
                    $"Model '{document.Model.Name}' contains invalid foreign key '{foreignKey.Name}'.");
            }
        }
    }

    private static HashSet<string> CollectAvailableFieldNames(ModelMetadataDocument document)
    {
        var availableFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var field in document.Model.Fields)
        {
            availableFields.Add(field.Name);
        }

        if (string.Equals(document.Model.Kind, "Entity", StringComparison.OrdinalIgnoreCase))
        {
            availableFields.UnionWith(EntityKernelFields);
            availableFields.UnionWith(document.Model.BuiltinFields.Where(EntityBuiltinFields.Contains));
        }
        else
        {
            availableFields.UnionWith(LinkKernelFields);
            availableFields.UnionWith(document.Model.BuiltinFields.Where(LinkBuiltinFields.Contains));
        }

        return availableFields;
    }

    private static bool IsKernelManagedField(
        string modelKind,
        string fieldName,
        HashSet<string> allowedBuiltinFields)
    {
        if (string.Equals(modelKind, "Entity", StringComparison.OrdinalIgnoreCase))
        {
            return EntityKernelFields.Contains(fieldName) || allowedBuiltinFields.Contains(fieldName);
        }

        return LinkKernelFields.Contains(fieldName) || allowedBuiltinFields.Contains(fieldName);
    }
}
