using ICP.Platform.Modeling.Metadata;

namespace ICP.Platform.Modeling.Validation;

/// <summary>
/// 负责校验单模型元数据文档的最小结构合法性。
/// </summary>
public sealed class ModelMetadataValidator
{
    private static readonly HashSet<string> EntityBuiltinFields = ["ENAME", "ECODE"];
    private static readonly HashSet<string> LinkBuiltinFields = ["LSequence"];

    /// <summary>
    /// 在不依赖类型映射的情况下校验文档基础结构。
    /// </summary>
    public void Validate(ModelMetadataDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (string.IsNullOrWhiteSpace(document.ModuleId))
        {
            throw new InvalidOperationException("moduleId 不能为空。");
        }

        if (document.Model is null)
        {
            throw new InvalidOperationException("model 不能为空。");
        }

        if (string.IsNullOrWhiteSpace(document.Model.Name))
        {
            throw new InvalidOperationException("model.name 不能为空。");
        }

        if (!string.Equals(document.Model.Kind, nameof(ModelKind.Entity), StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(document.Model.Kind, nameof(ModelKind.Link), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("model.kind 仅支持 Entity 或 Link。");
        }

        ValidateBuiltinFields(document.Model);

        if (string.Equals(document.Model.Kind, nameof(ModelKind.Link), StringComparison.OrdinalIgnoreCase) &&
            document.Model.LinkedTypes.Count == 0)
        {
            throw new InvalidOperationException("Link 模型必须声明 linkedTypes。");
        }
    }

    /// <summary>
    /// 在基础结构校验之后，进一步校验 storage.type 是否存在于类型映射中。
    /// </summary>
    public void Validate(ModelMetadataDocument document, StorageTypeMapDocument typeMap)
    {
        ArgumentNullException.ThrowIfNull(typeMap);

        Validate(document);

        foreach (var field in document.Model.Fields)
        {
            if (field.Storage is null || string.IsNullOrWhiteSpace(field.Storage.Type))
            {
                continue;
            }

            if (!typeMap.Types.ContainsKey(field.Storage.Type))
            {
                throw new InvalidOperationException($"字段 {field.Name} 的 storage.type 无效。");
            }
        }
    }

    private static void ValidateBuiltinFields(ModelMetadataModel model)
    {
        var allowedFields = string.Equals(model.Kind, nameof(ModelKind.Entity), StringComparison.OrdinalIgnoreCase)
            ? EntityBuiltinFields
            : LinkBuiltinFields;

        foreach (var builtinField in model.BuiltinFields)
        {
            if (!allowedFields.Contains(builtinField))
            {
                throw new InvalidOperationException($"builtinFields 中的 {builtinField} 与模型类型不匹配。");
            }
        }
    }
}
