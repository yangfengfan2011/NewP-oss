namespace ICP.Platform.Modeling.Definitions;

// Model artifact 的规范字段定义；DisplayRule 只携带 metadata 展示语义，不改变存储或持久化投影。
public sealed record FieldDefinition(
    string Name,
    string StoreName,
    FieldKind Kind,
    FieldValueType ValueType,
    string StoreType,
    bool Required,
    bool IsPrimaryKey,
    bool IsAuditField,
    FieldDisplayRuleDefinition? DisplayRule = null);
