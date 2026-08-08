namespace ICP.Platform.Modeling.Definitions;

public sealed record ModelDefinition(
    string Name,
    string TableName,
    ModelKind Kind,
    IReadOnlyList<FieldDefinition> Fields,
    IReadOnlyList<IndexDefinition> Indexes,
    string OwnerModuleId = "platform.core",
    string SchemaName = "dbo")
{
    /// <summary>
    /// 获取跨模块唯一的稳定逻辑模型键。
    /// </summary>
    public string LogicalKey => $"{OwnerModuleId}/{Name}";
}
