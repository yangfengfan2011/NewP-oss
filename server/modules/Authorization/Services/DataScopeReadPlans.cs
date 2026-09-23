using System.Linq.Expressions;
using System.Security.Claims;
using ICP.Platform.Authorization.Contracts;
using Microsoft.AspNetCore.Http;

namespace ICP.Platform.Modules.Authorization.Services;

/// <summary>按可信当前主体合并明确优先级意见，再把拒绝范围编译为数据库谓词。</summary>
public sealed class DataScopeReadPlans(DataScopeRules rules, DataScopeCatalog catalog,
    IHttpContextAccessor context, ILocalAdministratorRoleAssignment administrators, DataScopeIdentities identities) : IDataScopeReadPlanProvider
{
    /// <inheritdoc/>
    public async Task<DataScopeReadPlan> GetReadPlanAsync(string resourceKey, CancellationToken cancellationToken = default)
    {
        catalog.Resolve(resourceKey);
        var action = catalog.Actions.Single(action => action.Key == "read");
        if (!action.Enabled) return new(null);
        var id = context.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("DATA_SCOPE_TRUSTED_SUBJECT_REQUIRED");
        if (await administrators.InspectAsync(id, cancellationToken) == LocalAdministratorAssignmentState.Assigned) return new(null);
        var applicable = await rules.ForResourceAsync(resourceKey, cancellationToken);
        foreach (var rule in applicable) catalog.Validate(rule);
        var readable = applicable.SelectMany(ActiveNodes).Where(rule => catalog.HasReadContribution(resourceKey, rule.Condition)).ToArray();
        var facts = await identities.ResolveAsync(id, readable.SelectMany(rule => rule.Subjects).Select(subject => subject.Type),
            readable.SelectMany(rule => catalog.ContextKeys(rule.Condition)), cancellationToken);
        var row = Expression.Parameter(typeof(Dictionary<string, object?>), "row");
        var denied = DataScopeRuleTree.BuildDenied(applicable, node =>
        {
            if (!catalog.HasReadContribution(resourceKey, node.Condition)) return null;
            return catalog.BuildReadCondition(resourceKey, node.Condition, row, facts)
                ?? throw new InvalidOperationException("DATA_SCOPE_READ_CONTRIBUTION_MISSING");
        }, node =>
        {
            var opinions = node.Subjects.Where(subject => subject.Actions.TryGetValue("read", out var opinion) && opinion != "unset"
                && facts.Matches(subject.Type, subject.SubjectId, subject.MatchOptions)).ToArray();
            if (opinions.Length == 0) return null;
            var priority = opinions.Max(subject => subject.Priority);
            return opinions.Any(subject => subject.Priority == priority && subject.Actions["read"] == "deny") ? "deny" : "allow";
        });
        return new(denied is null ? null : Expression.Lambda<Func<Dictionary<string, object?>, bool>>(Expression.Not(denied), row));
    }

    private static IEnumerable<ICP.Platform.Modules.Authorization.DTOs.DataScopeRuleDto> ActiveNodes(
        ICP.Platform.Modules.Authorization.DTOs.DataScopeRuleDto node)
    {
        if (!node.Enabled) yield break;
        yield return node;
        foreach (var child in node.Children)
            foreach (var descendant in ActiveNodes(child)) yield return descendant;
    }
}
