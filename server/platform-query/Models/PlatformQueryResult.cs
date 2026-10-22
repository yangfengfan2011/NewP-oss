using ICP.Platform.Query.Descriptors;

namespace ICP.Platform.Query.Models;

/// <summary>
/// 表示一次平台查询执行后的分页结果与字段规划摘要。
/// Items 只包含数据库侧实际选择出来的真实字段，最终响应投影由上层 adapter 决定。
/// </summary>
/// <param name="Items">数据库查询返回的行集合。</param>
/// <param name="Total">满足条件的总行数。</param>
/// <param name="Page">当前页码。</param>
/// <param name="PageSize">当前分页大小。</param>
/// <param name="SortBy">本次请求的排序字段。</param>
/// <param name="SortDirection">本次请求的排序方向。</param>
/// <param name="ResponseFields">允许最终返回给调用方的字段顺序。</param>
/// <param name="RequestedDisplayFields">需要后续 display resolver 处理的 display value 字段。</param>
public sealed record PlatformQueryResult(
    IReadOnlyList<IReadOnlyDictionary<string, object?>> Items,
    int Total,
    int Page,
    int PageSize,
    string? SortBy,
    string? SortDirection,
    IReadOnlyList<string> ResponseFields,
    IReadOnlyList<PlatformDisplayValueField> RequestedDisplayFields);
