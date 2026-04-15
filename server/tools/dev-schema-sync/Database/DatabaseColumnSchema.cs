namespace ICP.Platform.Tools.DevSchemaSync.Database;

public sealed record DatabaseColumnSchema(
    string Name,
    string StoreType,
    bool IsNullable);
