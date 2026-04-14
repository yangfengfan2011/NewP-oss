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
            LabelKey = $"Model.{document.Model.Name}.Field.{field.Name}",
            Layout = field.Layout is null
                ? null
                : new ModelSchemaLayoutDescriptor
                {
                    Position = field.Layout.Position,
                    Width = field.Layout.Width,
                    Section = field.Layout.Section
                },
            Form = field.Form is null
                ? null
                : new ModelSchemaFormDescriptor
                {
                    CreateMode = field.Form.Create?.Mode,
                    EditMode = field.Form.Edit?.Mode
                },
            View = field.View is null
                ? null
                : new ModelSchemaViewDescriptor
                {
                    Visible = field.View.Visible
                },
            DataSource = field.DataSource is null
                ? null
                : new ModelSchemaDataSourceDescriptor
                {
                    Type = field.DataSource.Type,
                    Enum = field.DataSource.Enum,
                    Model = field.DataSource.Model
                }
        }).ToList();

        return new ModelSchemaDescriptor
        {
            ModelName = document.Model.Name,
            Fields = fields
        };
    }
}
