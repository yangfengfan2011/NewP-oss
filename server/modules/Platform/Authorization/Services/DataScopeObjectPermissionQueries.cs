using System.Security.Claims;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Persistence.EfCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;

namespace ICP.Platform.Modules.Authorization.Services;

/// <summary>仅从已经可浏览的真实对象进入查询，被分析用户显式传给同一裁决引擎。</summary>
public sealed class DataScopeObjectPermissionQueries(IDataScopeObjectAccess access, DataScopeIdentities identities,
    IDataScopeReadPlanProvider reads, IHttpContextAccessor context, DataScopeCatalog catalog, EfPlatformDbContext db, IDataScopeEvaluationScope? evaluation = null) : IDataScopeObjectPermissionQueries
{
    /// <inheritdoc/>
    public async Task<DataScopePermissionCatalog?> CatalogAsync(string resourceKey, string objectId, CancellationToken token = default)
    {
        using var operation = evaluation?.BeginOperation();
        if (!await access.IsReadableAsync(resourceKey, objectId, token)) return null;
        return new(catalog.ActionsFor(resourceKey).Select(action => new DataScopePermissionAction(action.Key, action.Title, action.Enabled, action.RequiresRead)).ToArray(), catalog.DirectSubjects.Types(resourceKey));
    }

    /// <inheritdoc/>
    public async Task<DataScopeAuthorizationResult<DataScopeSubjectPage>> UsersAsync(string resourceKey, string objectId, string? query,
        int page, IReadOnlyList<string> currentIds, CancellationToken token = default)
    {
        using var operation = evaluation?.BeginOperation();
        if (!await access.IsReadableAsync(resourceKey, objectId, token)) return new(DataScopeAuthorizationStatus.NotFound);
        try { return new(DataScopeAuthorizationStatus.Succeeded, await identities.SearchAsync("user", query, page, currentIds, token)); }
        catch (RolePermissionValidationException error)
        { return new(DataScopeAuthorizationStatus.InvalidInput, Error: new(error.Message, error.Code, "AuthorizationErrors." + error.Code, error.Args, error.Field)); }
    }

    /// <inheritdoc/>
    public async Task<DataScopeObjectInspection?> InspectAsync(string resourceKey, string objectId, string? accountId,
        CancellationToken token = default)
    {
        using var operation = evaluation?.BeginOperation();
        // 整个解释是同一只读单元；只保留自有连接，不扩大事务或缓存授权事实。
        var openedHere = db.Database.GetDbConnection().State != System.Data.ConnectionState.Open;
        try
        {
            if (openedHere) await db.Database.OpenConnectionAsync(token);
            if (!await access.IsReadableAsync(resourceKey, objectId, token)) return null;
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
}
