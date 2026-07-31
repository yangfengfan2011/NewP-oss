namespace ICP.Platform.Query.Models;

/// <summary>
/// 表示一次平台查询中的单个筛选条件。
/// 第一版只负责字段规划所需的静态信息，不在此处承载执行策略。
/// </summary>
/// <param name="Field">筛选字段名。</param>
/// <param name="Operator">筛选操作符。</param>
/// <param name="Value">筛选值。</param>
/// <param name="GroupKey">可选条件组标识。</param>
/// <param name="GroupOperator">组内逻辑操作符。</param>
public sealed record PlatformQueryCondition(
    string Field,
    string Operator,
    object? Value,
    string? GroupKey = null,
    string? GroupOperator = null);
