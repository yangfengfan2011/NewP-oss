using ICP.Platform.Modeling.Metadata;

namespace ICP.Platform.Modeling.Schema;

public sealed class ModelSchemaBuilder
{
    public ModelSchemaDescriptor Build(ModelMetadataDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        // 当前最小 builder 只做字段级 schema 展开，后续再补 section、dataSource 和模式细节。
        var fields = document.Model.Fields.Select(field => new ModelSchemaFieldDescriptor
        {
            Name = field.Name,
            UiType = string.IsNullOrWhiteSpace(field.UiType) ? "readonly" : field.UiType,
            LabelKey = $"Model.{document.Model.Name}.Field.{field.Name}"
        }).ToList();

        return new ModelSchemaDescriptor
        {
            ModelName = document.Model.Name,
            Fields = fields
        };
    }
}
