using System.Security.Claims;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Persistence.EfCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;

namespace ICP.Platform.Modules.Authorization.Services;

/// <summary>仅从已经可浏览的真实对象进入查询，被分析用户显式传给同一裁决引擎。</summary>
public sealed class DataScopeObjectPermissionQueries(IDataExecutionAccess executions, DataScopeIdentities identities,
    IDataScopeReadPlanProvider reads, IHttpContextAccessor context, DataScopeCatalog catalog, EfPlatformDbContext db,
    IDataScopeEvaluationScope? evaluation = null, DataScopeRules? rules = null) : IDataScopeObjectPermissionQueries
{
    /// <inheritdoc/>
    public Task<DataScopePermissionCatalog?> CatalogAsync(string resourceKey, string objectId,
        IReadOnlyList<DataExecutionBindingReference> bindings, CancellationToken token = default) =>
        CatalogCoreAsync(resourceKey, objectId, RequiredBindings(bindings), token);

    // 声明校验替换原完整读取点，目录内容与功能资格由原owner保持。
    private async Task<DataScopePermissionCatalog?> CatalogCoreAsync(string resourceKey, string objectId,
        IReadOnlyList<DataExecutionBindingReference> bindings, CancellationToken token)
    {
        using var operation = evaluation?.BeginOperation();
        using var configuration = rules?.BeginOperation();
        if (!await IsReadableAsync(resourceKey, objectId, bindings, token)) return null;
        return new(catalog.ActionsFor(resourceKey).Select(action => new DataScopePermissionAction(action.Key, action.Title, action.Enabled, action.RequiresRead)).ToArray(), catalog.DirectSubjects.Types(resourceKey));
    }

    /// <inheritdoc/>
    public Task<DataScopeAuthorizationResult<DataScopeSubjectPage>> UsersAsync(string resourceKey, string objectId, string? query,
        int page, IReadOnlyList<string> currentIds, IReadOnlyList<DataExecutionBindingReference> bindings, CancellationToken token = default) =>
        UsersCoreAsync(resourceKey, objectId, query, page, currentIds, RequiredBindings(bindings), token);

    // 用户候选和当前值仍用原最小安全目录，不引入业务档案对象权限。
    private async Task<DataScopeAuthorizationResult<DataScopeSubjectPage>> UsersCoreAsync(string resourceKey, string objectId, string? query,
        int page, IReadOnlyList<string> currentIds, IReadOnlyList<DataExecutionBindingReference> bindings, CancellationToken token)
    {
        using var operation = evaluation?.BeginOperation();
        using var configuration = rules?.BeginOperation();
        if (!await IsReadableAsync(resourceKey, objectId, bindings, token)) return new(DataScopeAuthorizationStatus.NotFound);
        try { return new(DataScopeAuthorizationStatus.Succeeded, await identities.SearchAsync("user", query, page, currentIds, token)); }
        catch (RolePermissionValidationException error)
        { return new(DataScopeAuthorizationStatus.InvalidInput, Error: new(error.Message, error.Code, "AuthorizationErrors." + error.Code, error.Args, error.Field)); }
    }

    /// <inheritdoc/>
    public Task<DataScopeObjectInspection?> InspectAsync(string resourceKey, string objectId, string? accountId,
        IReadOnlyList<DataExecutionBindingReference> bindings, CancellationToken token = default) =>
        InspectCoreAsync(resourceKey, objectId, accountId, RequiredBindings(bindings), token);

    // 解释连接所有权、被分析账号和原批量名称投影保持，只替换原read资格点。
    private async Task<DataScopeObjectInspection?> InspectCoreAsync(string resourceKey, string objectId, string? accountId,
        IReadOnlyList<DataExecutionBindingReference> bindings, CancellationToken token)
    {
        using var operation = evaluation?.BeginOperation();
        using var configuration = rules?.BeginOperation();
        // 整个解释是同一只读单元；只保留自有连接，不扩大事务或缓存授权事实。
        var openedHere = db.Database.GetDbConnection().State != System.Data.ConnectionState.Open;
        try
        {
            if (openedHere) await db.Database.OpenConnectionAsync(token);
            if (!await IsReadableAsync(resourceKey, objectId, bindings, token)) return null;
            var target = accountId ?? context.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(target) || target.Length > 128)
                throw new RolePermissionValidationException("被查询账号无效。", "DATA_SCOPE_ACCOUNT_INVALID", field: "accountId");
            var directory = await identities.SearchAsync("user", null, 1, [target], token);
            var account = directory.CurrentValues.SingleOrDefault(item => item.Id == target);
            if (account is null) return null;
            var actions = await ((DataScopeReadPlans)reads).ExplainAsync(resourceKey, objectId, target, token);
            var names = new List<DataScopeInspectionSubjectName>();
            foreach (var group in actions.SelectMany(action => action.Sources).SelectMany(source => source.Subjects)
                .Where(subject => subject.SubjectId is not null).GroupBy(subject => subject.Type).OrderBy(group => group.Key, StringComparer.Ordinal))
                foreach (var batch in group.Select(subject => subject.SubjectId!).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).Chunk(100))
                {
                    var page = await identities.SearchAsync(group.Key, null, 1, batch, token);
                    var known = page.CurrentValues.Where(value => batch.Contains(value.Id, StringComparer.Ordinal)).ToDictionary(value => value.Id, StringComparer.Ordinal);
                    names.AddRange(batch.Select(id => known.TryGetValue(id, out var value)
                        ? new DataScopeInspectionSubjectName(group.Key, id, value.Name, "resolved") : new(group.Key, id, null, "unavailable")));
                }
            return new(resourceKey, objectId, target, account.Name, actions.Select(action => new DataScopeActionInspection(
                action.Key, action.Title, action.Allowed, action.Reason, action.Sources.Select(source => new DataScopeInspectionSource(
                    source.Kind, source.Id, source.Name, source.Opinion, source.Status,
                    source.Subjects.Select(subject => new DataScopeInspectionSubject(subject.Id, subject.Type, subject.SubjectId,
                        subject.Priority, subject.Actions, subject.MatchOptions, subject.ExpiresAt)).ToArray())).ToArray())).ToArray(), names);
        }
        finally { if (openedHere) await db.Database.CloseConnectionAsync(); }
    }

    // 所有只读查询在原读取点消费明确来源，不保留无来源手工构造分支。
    private async Task<bool> IsReadableAsync(string resourceKey, string objectId,
        IReadOnlyList<DataExecutionBindingReference> bindings, CancellationToken token) =>
        await executions.EvaluateObjectFromBindingsAsync(bindings, resourceKey, "view", objectId, token) == DataExecutionDecision.Allowed;

    // 在打开解释连接或读取目录前拒绝缺失来源。
    private static IReadOnlyList<DataExecutionBindingReference> RequiredBindings(IReadOnlyList<DataExecutionBindingReference>? bindings) =>
        bindings is { Count: > 0 } && bindings.All(binding => binding is not null) ? bindings
            : throw new ActionDataAccessException(400, "ActionDataAccess.DeclarationInvalid");
}
