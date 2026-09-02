namespace ICP.Platform.Crud.Models;

/// <summary>
/// 标准 CRUD lookup 分页结果；当前值独立返回，避免候选页刷新时丢失既有选择。
/// </summary>
public sealed record StandardCrudLookupResult(
    IReadOnlyList<StandardCrudOptionItem> Items,
    IReadOnlyList<StandardCrudOptionItem> CurrentItems,
    int Page,
    int PageSize,
    bool HasMore);
