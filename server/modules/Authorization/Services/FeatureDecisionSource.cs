using ICP.Platform.Authorization;
using ICP.Platform.Authorization.Http;
using ICP.Platform.Persistence.EfCore;
using Microsoft.EntityFrameworkCore;
namespace ICP.Platform.Modules.Authorization.Services;

/// <summary>逐请求直接使用当前角色关系；不读取用户全部授权或按角色逐次请求。</summary>
public sealed class FeatureDecisionSource(EfPlatformDbContext db, ActiveFeatureCatalog catalog) : IFeatureDecisionSource, IFeatureGrantProjection
{
    /// <inheritdoc/>
    public async ValueTask<PermissionDecision> EvaluateAsync(string subjectId, string position, string behaviorKey, CancellationToken cancellationToken)
    {
        var snapshot = catalog.Snapshot;
        if (!snapshot.ByPosition.TryGetValue(position, out var point) || (point.Point.Container || (point.Point.BehaviorKey != behaviorKey && !point.Point.ReadBehaviors.Contains(behaviorKey, StringComparer.Ordinal))))
            return PermissionDecision.Denied;
        return await EffectiveLinks(subjectId).AnyAsync(link => EF.Property<string>(link, "FunctionPointId") == point.Id, cancellationToken)
            ? PermissionDecision.Granted : PermissionDecision.Denied;
    }

    /// <summary>前端完整投影是独立读取，不参与单功能判权。</summary>
    public async Task<IReadOnlyList<string>> GetCurrentPositionsAsync(string subjectId, CancellationToken cancellationToken)
    {
        var snapshot = catalog.Snapshot;
        var ids = await EffectiveLinks(subjectId).Select(link => EF.Property<string>(link, "FunctionPointId")).Distinct().ToArrayAsync(cancellationToken);
        return ids.Where(snapshot.ById.ContainsKey).Select(id => snapshot.ById[id].Point.Position).ToArray();
    }

    // 同一条 SQL 连接当前用户、启用角色及授权，AnyAsync 投影为关联 EXISTS。
    private IQueryable<Dictionary<string, object?>> EffectiveLinks(string subjectId) =>
        from userRole in db.Set<Dictionary<string, object?>>(db.ResolveModelKey("UserRole")).AsNoTracking()
        join role in db.Set<Dictionary<string, object?>>(db.ResolveModelKey("Role")).AsNoTracking()
            on EF.Property<string>(userRole, "RoleId") equals EF.Property<string>(role, "EID")
        join grant in db.Set<Dictionary<string, object?>>(db.ResolveModelKey("RoleFunctionGrant")).AsNoTracking()
            on EF.Property<string>(role, "EID") equals EF.Property<string>(grant, "RoleId")
        where EF.Property<string>(userRole, "UserId") == subjectId && EF.Property<bool>(role, "IsEnabled")
        select grant;
}
