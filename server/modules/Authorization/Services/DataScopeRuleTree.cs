using System.Linq.Expressions;
using ICP.Platform.Modules.Authorization.DTOs;

namespace ICP.Platform.Modules.Authorization.Services;

/// <summary>编译规则层级的明确拒绝范围；同层分支独立投票，后代只遮蔽自己的祖先。</summary>
internal static class DataScopeRuleTree
{
    // 只返回明确拒绝的范围；无意见时由调用方保持默认允许。
    internal static Expression? BuildDenied(IEnumerable<DataScopeRuleDto> roots,
        Func<DataScopeRuleDto, Expression?> condition, Func<DataScopeRuleDto, string?> opinion)
    {
        Expression? denied = null;
        foreach (var root in roots) denied = Or(denied, Decide(root, null).Denied);
        return denied;

        // 后代明确票只遮蔽本分支祖先，兄弟分支的拒绝仍独立合并。
        (Expression? Occupied, Expression? Denied) Decide(DataScopeRuleDto node, Expression? ancestors)
        {
            if (!node.Enabled) return (null, null);
            var ownCondition = condition(node);
            var path = And(ancestors, ownCondition);
            Expression? childOccupied = null;
            Expression? childDenied = null;
            foreach (var child in node.Children)
            {
                var branch = Decide(child, path);
                childOccupied = Or(childOccupied, branch.Occupied);
                childDenied = Or(childDenied, branch.Denied);
            }
            var vote = ownCondition is null ? null : opinion(node);
            if (vote is null) return (childOccupied, childDenied);
            var own = path ?? Expression.Constant(true);
            var effective = childOccupied is null ? own : Expression.AndAlso(own, Expression.Not(childOccupied));
            return (Or(own, childOccupied), Or(childDenied, vote == "deny" ? effective : null));
        }
    }

    // null 表示没有额外条件，保留已有的祖先或本节点条件。
    private static Expression? And(Expression? left, Expression? right) =>
        left is null ? right : right is null ? left : Expression.AndAlso(left, right);

    // null 表示此分支没有明确票，不制造恒真或恒假票。
    private static Expression? Or(Expression? left, Expression? right) =>
        left is null ? right : right is null ? left : Expression.OrElse(left, right);
}
