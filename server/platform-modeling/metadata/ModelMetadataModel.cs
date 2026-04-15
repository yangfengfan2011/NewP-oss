namespace ICP.Platform.Modeling.Metadata;

// 模型级元数据只保留 authoring 事实，运行时行为仍由内核负责。
public sealed class ModelMetadataModel
{
    public string Name { get; init; } = string.Empty;

    // 历史名称只表达 authoring 层面的迁移事实，真正 rename 判定由工具内核负责。
    public string? HistoryName { get; init; }

    public string Kind { get; init; } = string.Empty;

    // builtinFields 用于在强制固定字段之外启用可选固定字段。
    public List<string> BuiltinFields { get; init; } = [];

    public List<ModelMetadataField> Fields { get; init; } = [];

    // Link 模型在这里声明允许的父子类型组合。
    public List<ModelMetadataLinkedType> LinkedTypes { get; init; } = [];

    public List<ModelMetadataIndex> Indexes { get; init; } = [];
}
