namespace ICP.Platform.Modeling.Definitions;

public sealed record ModelDefinition(
    string Name,
    string TableName,
    ModelKind Kind,
    IReadOnlyList<FieldDefinition> Fields,
    IReadOnlyList<IndexDefinition> Indexes,
    string OwnerModuleId = "platform.core",
    string SchemaName = "dbo",
    IReadOnlyList<ForeignKeyDefinition>? ForeignKeys = null)
{
    public IReadOnlyList<ForeignKeyDefinition> ForeignKeys { get; init; } = ForeignKeys ?? [];

    /// <summary>
    /// 获取跨模块唯一的稳定逻辑模型键。
    /// </summary>
    public string LogicalKey => $"{OwnerModuleId}/{Name}";
}
