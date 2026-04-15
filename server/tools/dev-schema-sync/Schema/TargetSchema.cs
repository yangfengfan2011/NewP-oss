namespace ICP.Platform.Tools.DevSchemaSync.Schema;

public sealed record TargetSchema(IReadOnlyList<TargetTableSchema> Tables);
