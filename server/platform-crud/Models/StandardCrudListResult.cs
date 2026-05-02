namespace ICP.Platform.Crud.Models;

public sealed record StandardCrudListResult(
    IReadOnlyList<IReadOnlyDictionary<string, object?>> Items,
    int Total,
    int Page,
    int PageSize,
    string? SortBy,
    string? SortDirection);
