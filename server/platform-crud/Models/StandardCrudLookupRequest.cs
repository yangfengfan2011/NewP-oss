namespace ICP.Platform.Crud.Models;

/// <summary>
/// 标准 CRUD lookup 的可序列化请求；分页候选与当前值解析共享同一资源边界。
/// </summary>
public sealed record StandardCrudLookupRequest(
    string? Keyword = null,
    int Page = 1,
    int PageSize = 20,
    IReadOnlyList<string>? CurrentValueIds = null);
