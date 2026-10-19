namespace ICP.Platform.Crud.Models;

public sealed record StandardCrudListQuery(
    int Page = 1,
    int PageSize = 20,
    string? SortBy = null,
    string? SortDirection = null,
    IReadOnlyDictionary<string, string?>? Filters = null,
    IReadOnlyList<string>? Fields = null,
    IReadOnlyDictionary<string, string?>? QueryScope = null)
{
    /// <summary>仅服务端传递的正式查询来源；浏览器JSON不能提交或覆盖它。</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public ICP.Platform.Authorization.Contracts.DataExecutionQueryContext? ReadContext { get; init; }
}
