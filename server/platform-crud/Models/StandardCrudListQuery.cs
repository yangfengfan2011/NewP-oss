namespace ICP.Platform.Crud.Models;

public sealed record StandardCrudListQuery(
    int Page = 1,
    int PageSize = 20,
    string? SortBy = null,
    string? SortDirection = null,
    IReadOnlyDictionary<string, string?>? Filters = null,
    IReadOnlyList<string>? Fields = null);
