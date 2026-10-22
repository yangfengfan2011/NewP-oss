namespace ICP.Platform.Crud.Models;

/// <summary>
/// 描述内核可执行的单个查询条件；GroupKey/GroupOperator 只表达组合约束，不承载业务算法。
/// </summary>
public sealed record StandardCrudQueryCondition(
    string Field,
    string Operator,
    object? Value,
    string? GroupKey = null,
    string GroupOperator = "and");
