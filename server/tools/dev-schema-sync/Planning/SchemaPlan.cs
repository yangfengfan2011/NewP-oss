namespace ICP.Platform.Tools.DevSchemaSync.Planning;

public sealed record SchemaPlanSummary(
    int ManagedTableCount,
    int OperationCount,
    int SafeOperationCount,
    int ConfirmOperationCount,
    int UnmanagedTableCount);

public sealed record SchemaPlan(
    SchemaPlanSummary Summary,
    IReadOnlyList<string> ManagedObjects,
    IReadOnlyList<SchemaOperation> Operations,
    IReadOnlyList<string> UnmanagedTables);
