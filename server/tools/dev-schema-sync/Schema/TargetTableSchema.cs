namespace ICP.Platform.Tools.DevSchemaSync.Schema;

public sealed record TargetTableSchema(
    string ModuleId,
    string Name,
    string TableName,
    string? HistoryName,
    IReadOnlyList<TargetColumnSchema> Columns,
    IReadOnlyList<TargetIndexSchema> Indexes);
