namespace ICP.Platform.Modeling.Definitions;

// 该 artifact 只携带已校验的字段展示语义，不参与持久化模型投影。
public sealed record FieldDisplayRuleDefinition(
    string Type,
    string TargetModel,
    string TargetIdField,
    string TargetDisplayField);
