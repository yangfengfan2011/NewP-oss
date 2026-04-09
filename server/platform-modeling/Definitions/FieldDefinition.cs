namespace ICP.Platform.Modeling.Definitions;

public sealed record FieldDefinition(
    string Name,
    string StoreName,
    FieldKind Kind,
    FieldValueType ValueType,
    string StoreType,
    bool Required,
    bool IsPrimaryKey,
    bool IsAuditField);
