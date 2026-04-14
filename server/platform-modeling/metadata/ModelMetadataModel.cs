namespace ICP.Platform.Modeling.Metadata;

// 模型级元数据只保留 authoring 事实，运行时行为仍由内核负责。
public sealed class ModelMetadataModel
{
    public string Name { get; init; } = string.Empty;

    public string Kind { get; init; } = string.Empty;

    // builtinFields 用于在强制固定字段之外启用可选固定字段。
    public List<string> BuiltinFields { get; init; } = [];

    public List<ModelMetadataField> Fields { get; init; } = [];

    // Link 模型在这里声明允许的父子类型组合。
    public List<ModelMetadataLinkedType> LinkedTypes { get; init; } = [];

    public List<ModelMetadataIndex> Indexes { get; init; } = [];
}
