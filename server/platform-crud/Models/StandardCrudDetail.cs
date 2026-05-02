namespace ICP.Platform.Crud.Models;

public sealed record StandardCrudDetail
{
    public required IReadOnlyDictionary<string, object?> Fields { get; init; }
    public IReadOnlyDictionary<string, StandardCrudSummaryBlock> Summaries { get; init; }
        = new Dictionary<string, StandardCrudSummaryBlock>(StringComparer.Ordinal);
}
