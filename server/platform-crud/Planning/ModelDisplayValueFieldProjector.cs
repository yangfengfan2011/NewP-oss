using ICP.Platform.Modeling.Definitions;
using ICP.Platform.Query.Descriptors;

namespace ICP.Platform.Crud.Planning;

/// <summary>
/// 把 Model artifact 中已校验的 reference display 规则投影为现有查询运行时计划。
/// </summary>
public sealed class ModelDisplayValueFieldProjector
{
    /// <summary>
    /// 为源模型的 reference display 规则生成规范伴随字段，并校验目标模型字段存在。
    /// </summary>
    public IReadOnlyList<PlatformDisplayValueField> Project(
        ModelDefinition sourceModel,
        IReadOnlyCollection<ModelDefinition> modelCatalog)
    {
        ArgumentNullException.ThrowIfNull(sourceModel);
        ArgumentNullException.ThrowIfNull(modelCatalog);

        return sourceModel.Fields
            .Where(static field => field.DisplayRule is not null)
            .Select(field => ProjectField(field, modelCatalog))
            .ToArray();
    }

    // 每条规则只解析一次目标模型和字段，runtime resolver 继续消费既有计划形状。
    private static PlatformDisplayValueField ProjectField(
        FieldDefinition sourceField,
        IReadOnlyCollection<ModelDefinition> modelCatalog)
    {
        var display = sourceField.DisplayRule!;
        var targetModel = modelCatalog.SingleOrDefault(model =>
            string.Equals(model.Name, display.TargetModel, StringComparison.Ordinal))
            ?? throw new InvalidOperationException(
                $"Reference display for field '{sourceField.Name}' targets missing target model '{display.TargetModel}'.");

        RequireTargetField(sourceField, targetModel, display.TargetIdField, "id");
        RequireTargetField(sourceField, targetModel, display.TargetDisplayField, "display");

        return new PlatformDisplayValueField(
            $"{sourceField.Name}_DisplayValue",
            sourceField.Name,
            targetModel.Name,
            display.TargetIdField,
            display.TargetDisplayField);
    }

    // 目标字段缺失必须在 descriptor 生成前稳定失败，不能推迟到逐行解析。
    private static void RequireTargetField(
        FieldDefinition sourceField,
        ModelDefinition targetModel,
        string targetFieldName,
        string targetFieldRole)
    {
        if (targetModel.Fields.Any(field =>
                string.Equals(field.Name, targetFieldName, StringComparison.Ordinal)))
        {
            return;
        }

        throw new InvalidOperationException(
            $"Reference display for field '{sourceField.Name}' targets model '{targetModel.Name}' without {targetFieldRole} field '{targetFieldName}'.");
    }
}
