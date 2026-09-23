using System.Linq.Expressions;
using System.Security.Claims;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Persistence.EfCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;

namespace ICP.Platform.Modules.Authorization.Services;

/// <summary>按可信当前主体合并明确优先级意见，再把拒绝范围编译为数据库谓词。</summary>
public sealed class DataScopeReadPlans(DataScopeRules rules, DataScopeCatalog catalog,
    IHttpContextAccessor context, ILocalAdministratorRoleAssignment administrators, DataScopeIdentities identities,
    DataScopeObjectGrants objects, EfPlatformDbContext db) : IDataScopeReadPlanProvider, IDataScopeObjectDecisionProvider
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
        var facts = await identities.ResolveAsync(id, readable.SelectMany(rule => rule.Subjects).Select(subject => subject.Type)
            .Concat(identities.SubjectTypes.Select(type => type.Key)),
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
        var (allow, deny) = objects.ReadOverrides(resourceKey, row, facts);
        var permitted = Expression.OrElse(allow, Expression.AndAlso(Expression.Not(deny),
            denied is null ? Expression.Constant(true) : Expression.Not(denied)));
        return new(Expression.Lambda<Func<Dictionary<string, object?>, bool>>(permitted, row));
    }

    /// <inheritdoc/>
    public async Task<bool> IsAllowedAsync(string resourceKey, string objectId, string actionKey,
        CancellationToken cancellationToken = default)
    {
        var resource = catalog.Resolve(resourceKey);
        var action = catalog.Actions.Single(item => item.Key == actionKey);
        if (actionKey == "read") return await CanReadAsync(resource, objectId, cancellationToken);
        if (action.RequiresRead && !await CanReadAsync(resource, objectId, cancellationToken)) return false;
        if (!action.Enabled) return true;
        var subject = context.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("DATA_SCOPE_TRUSTED_SUBJECT_REQUIRED");
        if (await administrators.InspectAsync(subject, cancellationToken) == LocalAdministratorAssignmentState.Assigned) return true;
        var applicable = await rules.ForResourceAsync(resourceKey, cancellationToken);
        foreach (var rule in applicable) catalog.Validate(rule);
        var active = applicable.SelectMany(ActiveNodes).ToArray();
        var facts = await identities.ResolveAsync(subject,
            active.SelectMany(rule => rule.Subjects).Select(item => item.Type).Concat(identities.SubjectTypes.Select(type => type.Key)),
            active.SelectMany(rule => catalog.ContextKeys(rule.Condition)), cancellationToken);
        var opinion = await objects.OpinionAsync(resourceKey, objectId, actionKey, facts, cancellationToken);
        if (opinion is not null) return opinion == "allow";
        return !await DataScopeRuleTree.IsDeniedAsync(applicable,
            rule => catalog.MatchesAsync(resourceKey, objectId, rule.Condition, facts, cancellationToken),
            rule =>
            {
                var selected = rule.Subjects.Where(item => item.Actions.TryGetValue(actionKey, out var vote) && vote != "unset"
                    && facts.Matches(item.Type, item.SubjectId, item.MatchOptions)).ToArray();
                if (selected.Length == 0) return null;
                var priority = selected.Max(item => item.Priority);
                return selected.Any(item => item.Priority == priority && item.Actions[actionKey] == "deny") ? "deny" : "allow";
            });
    }

    // 单对象读取沿列表的同一数据库谓词，另以实际主键阻止排序规则把大小写别名当成规范对象身份。
    private async Task<bool> CanReadAsync(DataScopeResource resource, string objectId, CancellationToken token)
    {
        var plan = await GetReadPlanAsync(resource.ResourceKey, token);
        var rows = db.Set<Dictionary<string, object?>>(db.ResolveModelKey(resource.ModelKey)).AsNoTracking();
        if (plan.Predicate is not null) rows = rows.Where(plan.Predicate);
        var actual = await rows.Where(row => EF.Property<string>(row, resource.IdField) == objectId)
            .Select(row => EF.Property<string>(row, resource.IdField)).SingleOrDefaultAsync(token);
        return string.Equals(actual, objectId, StringComparison.Ordinal);
    }

    // 停用节点截断子树，避免解析不参与本次读取的主体与上下文事实。
    private static IEnumerable<ICP.Platform.Modules.Authorization.DTOs.DataScopeRuleDto> ActiveNodes(
        ICP.Platform.Modules.Authorization.DTOs.DataScopeRuleDto node)
    {
        if (!node.Enabled) yield break;
        yield return node;
        foreach (var child in node.Children)
            foreach (var descendant in ActiveNodes(child)) yield return descendant;
    }
}
