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
        var enabled = applicable.Where(rule => rule.Enabled).ToArray();
        foreach (var rule in enabled) catalog.Validate(rule);
        var readable = enabled.Where(rule => catalog.HasReadContribution(resourceKey, rule.Condition)).ToArray();
        var facts = await identities.ResolveAsync(id, readable.SelectMany(rule => rule.Subjects).Select(subject => subject.Type),
            readable.SelectMany(rule => catalog.ContextKeys(rule.Condition)), cancellationToken);
        var row = Expression.Parameter(typeof(Dictionary<string, object?>), "row");
        Expression? permitted = null;
        foreach (var rule in readable)
        {
            var opinions = rule.Subjects.Where(subject => subject.Actions.TryGetValue("read", out var opinion) && opinion != "unset"
                && facts.Matches(subject.Type, subject.SubjectId, subject.MatchOptions)).ToArray();
            if (opinions.Length == 0) continue;
            var priority = opinions.Max(subject => subject.Priority);
            if (!opinions.Any(subject => subject.Priority == priority && subject.Actions["read"] == "deny")) continue;
            var match = catalog.BuildReadCondition(resourceKey, rule.Condition, row, facts);
            if (match is null) continue;
            Expression allow = Expression.Not(match);
            permitted = permitted is null ? allow : Expression.AndAlso(permitted, allow);
        }
        return new(permitted is null ? null : Expression.Lambda<Func<Dictionary<string, object?>, bool>>(permitted, row));
    }
}
