namespace ICP.Platform.Tools.DevSchemaSync.Planning;

public static class SchemaOperationKind
{
    public const string CreateTable = nameof(CreateTable);
    public const string DropTable = nameof(DropTable);
    public const string AddColumn = nameof(AddColumn);
    public const string DropColumn = nameof(DropColumn);
    public const string RenameTable = nameof(RenameTable);
    public const string RenameColumn = nameof(RenameColumn);
    public const string AlterColumnType = nameof(AlterColumnType);
    public const string AlterNullability = nameof(AlterNullability);
}

public static class RiskLevel
{
    public const string Safe = nameof(Safe);
    public const string Confirm = nameof(Confirm);
}

public sealed record SchemaOperation(
    string Kind,
    string RiskLevel,
    string TableName,
    string? ColumnName,
    string? FromName,
    string Description,
    IReadOnlyList<string> SqlPreview);
