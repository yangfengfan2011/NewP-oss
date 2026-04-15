namespace ICP.Platform.Tools.DevSchemaSync.Database;

public sealed record DatabaseTableSchema(
    string Schema,
    string Name,
    IReadOnlyList<DatabaseColumnSchema> Columns);
