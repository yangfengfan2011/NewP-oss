namespace ICP.Platform.Modeling.Definitions;

public sealed record ModelDefinition(
    string Name,
    string TableName,
    ModelKind Kind,
    IReadOnlyList<FieldDefinition> Fields,
    IReadOnlyList<IndexDefinition> Indexes);
