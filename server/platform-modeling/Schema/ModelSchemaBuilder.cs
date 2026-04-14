using ICP.Platform.Modeling.Metadata;

namespace ICP.Platform.Modeling.Schema;

/// <summary>
/// 根据模型元数据构造面向前端的最小 schema 描述。
/// </summary>
public sealed class ModelSchemaBuilder
{
    /// <summary>
    /// 生成模型的最小 schema 输出。
    /// </summary>
    public ModelSchemaDescriptor Build(ModelMetadataDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        return new ModelSchemaDescriptor
        {
            ModelName = document.Model.Name,
            Fields = document.Model.Fields
                .Select(field => new ModelSchemaFieldDescriptor
                {
                    Name = field.Name,
                    UiType = string.IsNullOrWhiteSpace(field.UiType) ? "readonly" : field.UiType!,
                    LabelKey = $"Model.{document.Model.Name}.Field.{field.Name}"
                })
                .ToList()
        };
    }
}
