namespace ICP.Platform.Modeling.Schema;

// 后端对外输出的最小模型 schema 描述对象。
public sealed class ModelSchemaDescriptor
{
    public string ModelName { get; init; } = string.Empty;

    public List<ModelSchemaFieldDescriptor> Fields { get; init; } = [];
}

// 首轮先只输出前端最小必需字段，避免 schema 契约过早膨胀。
public sealed class ModelSchemaFieldDescriptor
{
    public string Name { get; init; } = string.Empty;

    public string UiType { get; init; } = string.Empty;

    public string LabelKey { get; init; } = string.Empty;
}
