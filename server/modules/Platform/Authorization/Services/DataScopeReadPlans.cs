using System.Linq.Expressions;
using System.Security.Claims;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Persistence.EfCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;

namespace ICP.Platform.Modules.Authorization.Services;

/// <summary>按可信当前主体合并明确优先级意见，再把拒绝范围编译为数据库谓词。</summary>
public sealed partial class DataScopeReadPlans(DataScopeRules rules, DataScopeCatalog catalog,
    IHttpContextAccessor context, ILocalAdministratorRoleAssignment administrators, DataScopeIdentities identities,
    DataScopeObjectGrants objects, EfPlatformDbContext db) : IDataScopeReadPlanProvider, IDataScopeObjectDecisionProvider, IDataScopeReadableIds
{
    /// <inheritdoc/>
    public async Task<IReadOnlySet<string>> GetReadableIdsAsync(string resourceKey, IReadOnlyCollection<string> objectIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(objectIds);
        var ids = objectIds.Where(static id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal).ToArray();
        if (ids.Length == 0) return new HashSet<string>(StringComparer.Ordinal);

        try
        {
            var resource = catalog.Resolve(resourceKey);
            var plan = await GetReadPlanAsync(resourceKey, cancellationToken);
            if (plan.Predicate is null) return ids.ToHashSet(StringComparer.Ordinal);

            var readable = new HashSet<string>(StringComparer.Ordinal);
            var model = db.Set<Dictionary<string, object?>>(db.ResolveModelKey(resource.ModelKey)).AsNoTracking();
            // 一批目标只投影主键，分块保持 SQL 参数数目有界；不读取整份业务目录。
            foreach (var batch in ids.Chunk(500))
            {
                var found = await model.Where(plan.Predicate)
                    .Where(row => batch.Contains(EF.Property<string>(row, resource.IdField)))
                    .Select(row => EF.Property<string>(row, resource.IdField))
                    .ToArrayAsync(cancellationToken);
                readable.UnionWith(found.Where(ids.Contains));
            }
            return readable;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new DataScopeReadExecutionException(resourceKey, exception);
        }
    }

    /// <inheritdoc/>
    public async Task<DataScopeReadPlan> GetReadPlanAsync(string resourceKey, CancellationToken cancellationToken = default)
        => await GetReadPlanForSubjectAsync(resourceKey, null, cancellationToken);

    // 诊断显式传入被分析身份，不修改请求主体；正常请求仍从可信上下文取身份。
    private async Task<DataScopeReadPlan> GetReadPlanForSubjectAsync(string resourceKey, string? accountId,
        CancellationToken cancellationToken, DataScopeDecisionTrace? trace = null)
    {
        catalog.Resolve(resourceKey);
        var action = catalog.Actions.Single(action => action.Key == "read");
        if (!action.Enabled) { trace?.SetReason("disabled"); return new(null); }
        var id = accountId ?? CurrentSubject();
        if (await administrators.InspectAsync(id, cancellationToken) == LocalAdministratorAssignmentState.Assigned)
        { trace?.SetReason("administrator"); return new(null); }
        var applicable = await rules.ForResourceAsync(resourceKey, cancellationToken);
        foreach (var rule in applicable) catalog.Validate(rule);
        trace?.SetRules(applicable);
        var relevant = RelevantNodes(resourceKey, applicable, "read", true);
        var readable = applicable.SelectMany(ActiveNodes).Where(rule => relevant.Contains(rule.Id)).ToArray();
        var objectTypes = await objects.SubjectTypesAsync(resourceKey, "read", null, cancellationToken);
        var types = readable.SelectMany(rule => rule.Subjects)
            .Where(subject => subject.Actions.TryGetValue("read", out var opinion) && opinion != "unset")
            .Select(subject => subject.Type).Concat(objectTypes).Distinct(StringComparer.Ordinal).ToArray();
        var facts = await identities.ResolveAsync(id, types,
            readable.SelectMany(rule => catalog.ContextKeys(rule.Condition)), cancellationToken);
        var row = Expression.Parameter(typeof(Dictionary<string, object?>), "row");
        var denied = DataScopeRuleTree.BuildDenied(applicable, node =>
        {
            if (!relevant.Contains(node.Id)) return null;
            return catalog.BuildReadCondition(resourceKey, node.Condition, row, facts)
                ?? throw new InvalidOperationException("DATA_SCOPE_READ_CONTRIBUTION_MISSING");
        }, node =>
        {
            if (!relevant.Contains(node.Id)) return null;
            var opinions = node.Subjects.Where(subject => subject.Actions.TryGetValue("read", out var opinion) && opinion != "unset"
                && facts.Matches(subject.Type, subject.SubjectId, subject.MatchOptions)).ToArray();
            if (opinions.Length == 0) return null;
            var priority = opinions.Max(subject => subject.Priority);
            trace?.SetSubjects(node.Id, opinions.Where(subject => subject.Priority == priority).ToArray());
            return opinions.Any(subject => subject.Priority == priority && subject.Actions["read"] == "deny") ? "deny" : "allow";
        }, trace is null ? null : trace.ObserveReadRule);
        var (allow, deny) = objects.ReadOverrides(resourceKey, row, facts, objectTypes);
        var permitted = Expression.OrElse(allow, Expression.AndAlso(Expression.Not(deny),
            denied is null ? Expression.Constant(true) : Expression.Not(denied)));
        if (trace is not null) await trace.ReadSourcesAsync(db, catalog.Resolve(resourceKey), row, cancellationToken);
        if (trace is not null && objectTypes.Length > 0)
            await objects.OpinionAsync(resourceKey, trace.ObjectId, "read", facts, cancellationToken, trace.ObserveObject);
        return new(Expression.Lambda<Func<Dictionary<string, object?>, bool>>(permitted, row));
    }

    /// <inheritdoc/>
    public async Task<bool> IsAllowedAsync(string resourceKey, string objectId, string actionKey,
        CancellationToken cancellationToken = default)
    {
        return await DecideActionAsync(resourceKey, objectId, actionKey, null, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<bool> IsCandidateAllowedAsync(string resourceKey, IReadOnlyDictionary<string, object?> candidate,
        string actionKey, CancellationToken cancellationToken = default)
    {
        var resource = catalog.Resolve(resourceKey);
        var objectId = candidate.TryGetValue(resource.IdField, out var value) ? value as string : null;
        if (string.IsNullOrWhiteSpace(objectId)) throw new InvalidOperationException("DATA_SCOPE_CANDIDATE_ID_REQUIRED");
        return await DecideActionAsync(resourceKey, objectId, actionKey, candidate, cancellationToken);
    }

    // 对象与候选共用意见合并；新建只改变条件事实来源，不引入另一套裁决算法。
    private async Task<bool> DecideActionAsync(string resourceKey, string objectId, string actionKey,
        IReadOnlyDictionary<string, object?>? candidate, CancellationToken cancellationToken,
        string? accountId = null, DataScopeDecisionTrace? trace = null)
    {
        var resource = catalog.Resolve(resourceKey);
        var action = catalog.Actions.Single(item => item.Key == actionKey);
        if (actionKey == "read")
        {
            if (candidate is not null) throw new InvalidOperationException("DATA_SCOPE_CANDIDATE_READ_UNSUPPORTED");
            return await CanReadAsync(resource, objectId, cancellationToken, accountId, trace);
        }
        if (action.RequiresRead && !await CanReadAsync(resource, objectId, cancellationToken, accountId))
        { trace?.SetReason("readRequired"); return false; }
        if (!action.Enabled) { trace?.SetReason("disabled"); return true; }
        var subject = accountId ?? CurrentSubject();
        if (await administrators.InspectAsync(subject, cancellationToken) == LocalAdministratorAssignmentState.Assigned)
        { trace?.SetReason("administrator"); return true; }
        var applicable = await rules.ForResourceAsync(resourceKey, cancellationToken);
        foreach (var rule in applicable) catalog.Validate(rule);
        trace?.SetRules(applicable);
        // 对象明确意见覆盖规则；校验配置后先只解析当前对象的主体，避免无关规则事实阻断覆盖。
        var objectTypes = await objects.SubjectTypesAsync(resourceKey, actionKey, objectId, cancellationToken);
        if (objectTypes.Length > 0)
        {
            var objectFacts = await identities.ResolveAsync(subject, objectTypes, [], cancellationToken);
            var opinion = await objects.OpinionAsync(resourceKey, objectId, actionKey, objectFacts, cancellationToken,
                trace is null ? null : trace.ObserveObject);
            if (opinion is not null) return opinion == "allow";
        }
        var relevant = RelevantNodes(resourceKey, applicable, actionKey, false);
        var active = applicable.SelectMany(ActiveNodes).Where(rule => relevant.Contains(rule.Id)).ToArray();
        var facts = await identities.ResolveAsync(subject,
            active.SelectMany(rule => rule.Subjects)
                .Where(item => item.Actions.TryGetValue(actionKey, out var vote) && vote != "unset")
                .Select(item => item.Type).Distinct(StringComparer.Ordinal),
            active.SelectMany(rule => catalog.ContextKeys(rule.Condition)), cancellationToken);
        return !await DataScopeRuleTree.IsDeniedAsync(applicable,
            rule => !relevant.Contains(rule.Id) ? Task.FromResult(false) : candidate is null
                ? catalog.MatchesAsync(resourceKey, objectId, rule.Condition, facts, cancellationToken)
                : catalog.MatchesCandidateAsync(resourceKey, candidate, rule.Condition, facts, cancellationToken),
            rule =>
            {
                if (!relevant.Contains(rule.Id)) return null;
                var selected = rule.Subjects.Where(item => item.Actions.TryGetValue(actionKey, out var vote) && vote != "unset"
                    && facts.Matches(item.Type, item.SubjectId, item.MatchOptions)).ToArray();
                if (selected.Length == 0) return null;
                var priority = selected.Max(item => item.Priority);
                trace?.SetSubjects(rule.Id, selected.Where(item => item.Priority == priority).ToArray());
                return selected.Any(item => item.Priority == priority && item.Actions[actionKey] == "deny") ? "deny" : "allow";
            }, trace is null ? null : trace.ObserveRule);
    }

    // 仅保留实际明确表态节点及通往它们的祖先；读取祖先无贡献时不解析条件。
    private HashSet<string> RelevantNodes(string resourceKey,
        IEnumerable<ICP.Platform.Modules.Authorization.DTOs.DataScopeRuleDto> roots, string actionKey, bool read)
    {
        var relevant = new HashSet<string>(StringComparer.Ordinal);
        foreach (var root in roots) Visit(root);
        return relevant;

        // 全部子分支都要访问，不能因首个有意见分支而跳过兄弟的拒绝票。
        bool Visit(ICP.Platform.Modules.Authorization.DTOs.DataScopeRuleDto node)
        {
            if (!node.Enabled) return false;
            var descendant = false;
            foreach (var child in node.Children) descendant |= Visit(child);
            var contributes = !read || catalog.HasReadContribution(resourceKey, node.Condition);
            var own = contributes && node.Subjects.Any(subject =>
                subject.Actions.TryGetValue(actionKey, out var vote) && vote != "unset");
            if (contributes && (own || descendant)) relevant.Add(node.Id);
            return own || descendant;
        }
    }

    // 单对象读取沿列表的同一数据库谓词，另以实际主键阻止排序规则把大小写别名当成规范对象身份。
    private async Task<bool> CanReadAsync(DataScopeResource resource, string objectId, CancellationToken token,
        string? accountId = null, DataScopeDecisionTrace? trace = null)
    {
        var plan = await GetReadPlanForSubjectAsync(resource.ResourceKey, accountId, token, trace);
        var rows = db.Set<Dictionary<string, object?>>(db.ResolveModelKey(resource.ModelKey)).AsNoTracking();
        if (plan.Predicate is not null) rows = rows.Where(plan.Predicate);
        var actual = await rows.Where(row => EF.Property<string>(row, resource.IdField) == objectId)
            .Select(row => EF.Property<string>(row, resource.IdField)).SingleOrDefaultAsync(token);
        return string.Equals(actual, objectId, StringComparison.Ordinal);
    }

    // 仅正式请求入口使用当前身份；诊断的被分析身份必须由管理入口校验后显式传入。
    private string CurrentSubject() => context.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? throw new InvalidOperationException("DATA_SCOPE_TRUSTED_SUBJECT_REQUIRED");

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
