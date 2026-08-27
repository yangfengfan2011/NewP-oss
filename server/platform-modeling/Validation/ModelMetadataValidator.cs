using ICP.Platform.Modeling.Definitions;
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
        }

        ValidateDisplayRules(document, seenFieldNames);
    }

    // 展示规则必须在 authoring 阶段闭合字段语义和保留伴随字段命名，避免进入 runtime 后再猜测。
    private static void ValidateDisplayRules(
        ModelMetadataDocument document,
        HashSet<string> authoredFieldNames)
    {
        foreach (var field in document.Model.Fields.Where(static field => field.Display is not null))
        {
            var display = field.Display!;
            if (!string.Equals(display.Type, "reference", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Field '{field.Name}' has unsupported display.type '{display.Type}'.");
            }

            if (!IsReferenceField(field))
            {
                throw new InvalidOperationException(
                    $"Field '{field.Name}' must be a reference field to declare reference display.");
            }

            if (string.IsNullOrWhiteSpace(display.TargetModel)
                || string.IsNullOrWhiteSpace(display.TargetIdField)
                || string.IsNullOrWhiteSpace(display.TargetDisplayField))
            {
                throw new InvalidOperationException(
                    $"Field '{field.Name}' reference display must define non-empty targetModel, targetIdField, and targetDisplayField.");
            }

            var companionField = $"{field.Name}_DisplayValue";
            if (authoredFieldNames.Contains(companionField))
            {
                throw new InvalidOperationException(
                    $"Field '{companionField}' conflicts with the generated display companion for '{field.Name}'.");
            }
        }
    }

    // model 数据源与 provider-backed lookup 都是当前 Model 协议认可的引用字段。
    private static bool IsReferenceField(ModelMetadataField field)
    {
        return string.Equals(field.DataSource?.Type, "model", StringComparison.OrdinalIgnoreCase)
            || (string.Equals(field.DataSource?.Type, "provider", StringComparison.OrdinalIgnoreCase)
                && string.Equals(field.UiType, "lookup", StringComparison.OrdinalIgnoreCase));
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
        }
    }

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
