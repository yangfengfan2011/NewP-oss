namespace ICP.Platform.Crud.Models;

/// <summary>
/// 标准 CRUD lookup 的可序列化请求；分页候选与当前值解析共享同一资源边界。
/// </summary>
public sealed record StandardCrudLookupRequest(
    string? Keyword = null,
    int Page = 1,
    int PageSize = 20,
    IReadOnlyList<string>? CurrentValueIds = null)
{
    /// <summary>仅服务端按正式读取注册选定的查询和当前值来源。</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public ICP.Platform.Authorization.Contracts.DataExecutionQueryContext? ReadContext { get; init; }
}
