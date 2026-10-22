using System.Linq.Expressions;
using System.Text.Json;
using ICP.Platform.Modules.Authorization.DTOs;
using ICP.Platform.Modules.Authorization.Services;
using Xunit;

namespace ICP.Platform.Modules.Authorization.Tests;

public sealed class DataScopeRuleTreeTests
{
    [Fact]
    public void ExplicitDescendantsOverrideAncestorsButNotSiblingOrIndependentBranches()
    {
        var allow = Node("allow", "equal", 5, "allow");
        var unset = Node("unset", "equal", 5, null);
        var parent = Node("parent", "lessThan", 10, "deny", children: [allow, unset]);
        Assert.Equal(new[] { true, false, true }, new[] { 5, 6, 10 }.Select(value => Permitted([parent], value)));
        var sibling = Node("sibling", "equal", 5, "deny");
        parent = parent with { Children = [allow, sibling] };
        Assert.False(Permitted([parent], 5));
        Assert.False(Permitted([parent, Node("independent", "equal", 5, "deny")], 5));
        Assert.True(Permitted([parent with { Enabled = false }], 6));
        Assert.True(Permitted([parent with { Condition = Condition("equal", 6) }], 5));
        Assert.False(Permitted([Node("without-read", "absent", 0, "deny", children: [sibling])], 5));
        Assert.True(Permitted([Node("without-read", "absent", 0, "deny", children: [sibling])], 6));
    }

    [Fact]
    public void RandomNestedTreesMatchIndependentBranchVoteEnumeration()
    {
        var random = new Random(231);
        for (var sample = 0; sample < 100; sample++)
        {
            var id = 0;
            DataScopeRuleDto Generate(int depth)
            {
                var operation = new[] { "lessThan", "equal", "greaterThan", "absent" }[random.Next(4)];
                var vote = new string?[] { null, "allow", "deny" }[random.Next(3)];
                var children = depth == 0 ? [] : Enumerable.Range(0, random.Next(3)).Select(_ => Generate(depth - 1)).ToArray();
                return Node("n" + id++, operation, random.Next(0, 9), vote, random.Next(5) != 0, children);
            }
            var roots = new[] { Generate(3), Generate(2) };
            for (var value = 0; value < 10; value++)
                Assert.Equal(EnumeratedDecision(roots, value), Permitted(roots, value));
        }
    }

    private static DataScopeRuleDto Node(string id, string operation, int threshold, string? vote,
        bool enabled = true, IReadOnlyList<DataScopeRuleDto>? children = null) =>
        new(id, id, null, id, enabled, "organization.person", Condition(operation, threshold),
            vote is null ? [] : [new(id + "_subject", "all", null, 0, new Dictionary<string, string> { ["read"] = vote })], children ?? []);

    private static DataScopeConditionDto Condition(string operation, int threshold) =>
        new(operation == "absent" ? "business" : "attribute", "N", operation, JsonSerializer.SerializeToElement(threshold));

    private static bool Permitted(IEnumerable<DataScopeRuleDto> roots, int value)
    {
        var row = Expression.Parameter(typeof(int), "value");
        var denied = DataScopeRuleTree.BuildDenied(roots, node => Match(node, row),
            node => node.Subjects.FirstOrDefault()?.Actions.GetValueOrDefault("read"));
        return denied is null || !Expression.Lambda<Func<int, bool>>(denied, row).Compile()(value);
    }

    private static Expression? Match(DataScopeRuleDto node, ParameterExpression row)
    {
        if (node.Condition.Type == "business") return null;
        var threshold = Expression.Constant(node.Condition.Value.GetInt32());
        return node.Condition.Operator switch
        {
            "lessThan" => Expression.LessThan(row, threshold),
            "equal" => Expression.Equal(row, threshold),
            "greaterThan" => Expression.GreaterThan(row, threshold),
            _ => throw new InvalidOperationException()
        };
    }

    // 独立枚举全部生效票，再去除同一分支上有更深明确票的祖先票。
    private static bool EnumeratedDecision(IEnumerable<DataScopeRuleDto> roots, int value)
    {
        var votes = new List<(string Opinion, IReadOnlyList<string> Path)>();
        foreach (var root in roots) Visit(root, []);
        return !votes.Any(candidate => candidate.Opinion == "deny" && !votes.Any(other =>
            other.Path.Count > candidate.Path.Count && candidate.Path.SequenceEqual(other.Path.Take(candidate.Path.Count))));

        void Visit(DataScopeRuleDto node, IReadOnlyList<string> ancestors)
        {
            if (!node.Enabled) return;
            var condition = node.Condition;
            if (condition.Type != "business" && !(condition.Operator switch
            {
                "lessThan" => value < condition.Value.GetInt32(),
                "equal" => value == condition.Value.GetInt32(),
                "greaterThan" => value > condition.Value.GetInt32(),
                _ => false
            })) return;
            var path = ancestors.Append(node.Id).ToArray();
            var vote = condition.Type == "business" ? null : node.Subjects.FirstOrDefault()?.Actions.GetValueOrDefault("read");
            if (vote is not null) votes.Add((vote, path));
            foreach (var child in node.Children) Visit(child, path);
        }
    }
}
