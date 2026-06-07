namespace ICP.Platform.Query.Models;

/// <summary>
/// 表示一次平台查询请求的字段意图、分页和排序信息。
/// fields 只表达当前视图意图，最终可执行字段由查询内核校验和补齐。
/// </summary>
/// <param name="Fields">调用方请求返回的字段列表；为空时回退到 descriptor 默认字段。</param>
/// <param name="Page">页码，从 1 开始。</param>
/// <param name="PageSize">分页大小。</param>
/// <param name="SortBy">排序字段。</param>
/// <param name="SortDirection">排序方向。</param>
/// <param name="Conditions">筛选条件列表。</param>
public sealed record PlatformQueryRequest(
    IReadOnlyList<string>? Fields,
    int Page = 1,
    int PageSize = 20,
    string? SortBy = null,
    string? SortDirection = null,
    IReadOnlyList<PlatformQueryCondition>? Conditions = null);
