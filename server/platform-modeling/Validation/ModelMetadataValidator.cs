using ICP.Platform.Modeling.Metadata;

namespace ICP.Platform.Modeling.Validation;

public sealed class ModelMetadataValidator
{
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
}
