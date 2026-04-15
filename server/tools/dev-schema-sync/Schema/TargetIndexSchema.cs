namespace ICP.Platform.Tools.DevSchemaSync.Schema;

public sealed record TargetIndexSchema(
    string Name,
    IReadOnlyList<string> Columns,
    bool IsUnique);
