namespace ICP.Platform.Modules.Example.DTOs;

/// <summary>
/// 模块 API 使用的运行时列传输对象，不依赖前端 SDK 的标准列契约。
/// </summary>
/// <param name="Key">跨日期变化保持稳定的语义键。</param>
/// <param name="Field">计算型行中的字段名。</param>
/// <param name="Title">当前业务周期对应的显示标题。</param>
/// <param name="ValueKind">标准标量值类型。</param>
/// <param name="Sortable">是否允许服务端排序。</param>
/// <param name="Hideable">是否允许用户隐藏。</param>
public sealed record RuntimeWorkloadColumnDto(
    string Key,
    string Field,
    string Title,
    string ValueKind,
    bool Sortable,
    bool Hideable);

/// <summary>
/// 模块列 API 返回的完整列集与对应业务周期令牌。
/// </summary>
/// <param name="Columns">模块私有的可序列化列 DTO。</param>
/// <param name="ContextToken">仅由本模块解释的业务周期令牌。</param>
public sealed record RuntimeWorkloadColumnSetDto(
    IReadOnlyList<RuntimeWorkloadColumnDto> Columns,
    string ContextToken);

/// <summary>
/// 模块行 API 返回的标准分页数据形状。
/// </summary>
/// <param name="Items">按列周期计算得到的行。</param>
/// <param name="Total">筛选后的总行数。</param>
/// <param name="Page">实际页码。</param>
/// <param name="PageSize">实际页大小。</param>
public sealed record RuntimeWorkloadListResultDto(
    IReadOnlyList<IReadOnlyDictionary<string, object?>> Items,
    int Total,
    int Page,
    int PageSize);
