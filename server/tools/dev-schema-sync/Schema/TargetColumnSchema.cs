namespace ICP.Platform.Tools.DevSchemaSync.Schema;

public sealed record TargetColumnSchema(
    string Name,
    string StoreName,
    string? HistoryName,
    string StoreType,
    bool Required);
