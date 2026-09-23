using System.Linq.Expressions;
using ICP.Platform.Modules.Authorization.DTOs;

namespace ICP.Platform.Modules.Authorization.Services;

/// <summary>编译规则层级的明确拒绝范围；同层分支独立投票，后代只遮蔽自己的祖先。</summary>
internal static class DataScopeRuleTree
{
    internal static Expression? BuildDenied(IEnumerable<DataScopeRuleDto> roots,
        Func<DataScopeRuleDto, Expression?> condition, Func<DataScopeRuleDto, string?> opinion)
    {
        Expression? denied = null;
        foreach (var root in roots) denied = Or(denied, Decide(root, null).Denied);
        return denied;

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

    private static Expression? And(Expression? left, Expression? right) =>
        left is null ? right : right is null ? left : Expression.AndAlso(left, right);

    private static Expression? Or(Expression? left, Expression? right) =>
        left is null ? right : right is null ? left : Expression.OrElse(left, right);
}
