using ICP.Platform.Query.Descriptors;

namespace ICP.Platform.Query.Models;

/// <summary>
/// 表示字段规划结果：对外响应字段、数据库投影字段、仅内核使用字段和需要后续解析的 display value 字段。
/// </summary>
/// <param name="ResponseFields">最终允许返回给调用方的字段顺序。</param>
/// <param name="DbSelectFields">数据库查询必须投影的真实字段顺序。</param>
/// <param name="InternalOnlyFields">仅为排序、筛选或 display source 补齐而加入的内部字段。</param>
/// <param name="RequestedDisplayFields">需要后续 display resolver 处理的 display value 字段声明。</param>
public sealed record PlatformQueryFieldPlan(
    IReadOnlyList<string> ResponseFields,
    IReadOnlyList<string> DbSelectFields,
    IReadOnlyList<string> InternalOnlyFields,
    IReadOnlyList<PlatformDisplayValueField> RequestedDisplayFields);
