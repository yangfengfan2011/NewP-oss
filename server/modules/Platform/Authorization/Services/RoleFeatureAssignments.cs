using ICP.Platform.Crud.ObjectIds;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Modules.Audit.Contracts;
using ICP.Platform.Modules.Authorization.DTOs;
using ICP.Platform.Persistence.Abstractions;
using ICP.Platform.Persistence.Auditing;
using ICP.Platform.Persistence.EfCore;
using Microsoft.EntityFrameworkCore;
namespace ICP.Platform.Modules.Authorization.Services;

/// <summary>目标角色完整功能集合的读取与事务性替换；审计只读取锁内的实际前驱。</summary>
public sealed class RoleFeatureAssignments(EfPlatformDbContext db, IPlatformTransactionExecutor transactions,
    ActiveFeatureCatalog catalog, IPlatformObjectIdGenerator ids, IBusinessAuditRecorder recorder,
    IEntityAuditStampProvider stamps, RoleManagementQuery roles)
{
    /// <summary>读取目标角色及其仍属于当前活动目录的功能位置。</summary>
    public async Task<RoleFeatureAssignmentsDto?> GetAsync(string roleId, CancellationToken cancellationToken)
    {
        var role = await roles.GetAsync(roleId, cancellationToken);
        if (role is null) return null;
        var snapshot = catalog.Snapshot;
        var assigned = await db.Set<Dictionary<string, object?>>(db.ResolveModelKey("RoleFunctionGrant")).AsNoTracking()
            .Where(row => EF.Property<string>(row, "RoleId") == roleId)
            .Select(row => EF.Property<string>(row, "FunctionPointId")).ToArrayAsync(cancellationToken);
        return new(role, assigned.Where(snapshot.ById.ContainsKey).Select(id => snapshot.ById[id].Point.Position).ToArray());
    }

    /// <summary>验证完整当前集合、串行替换目标角色关系及其差异审计；不自动重放。</summary>
    public async Task ReplaceAsync(string roleId, IReadOnlyCollection<string> positions, CancellationToken cancellationToken)
    {
        var snapshot = catalog.Snapshot;
        try { FeatureCatalogCompiler.ValidateSelection(snapshot.Points, positions); }
        catch (FeatureSelectionException exception)
        {
            var code = exception.Code switch
            {
                "FeatureSelection.UnknownPosition" => "AUTHORIZATION_FEATURE_UNKNOWN",
                "FeatureSelection.EntryDependencyMissing" => "AUTHORIZATION_FEATURE_ENTRY_REQUIRED",
                _ => null
            };
            if (code is null) throw;
            throw new RolePermissionValidationException(exception.Message, code, exception.Args);
        }
        var requested = positions.ToHashSet(StringComparer.Ordinal);
        await transactions.ExecuteAsync(async token =>
        {
            var roleRows = db.Set<Dictionary<string, object?>>(db.ResolveModelKey("Role"));
            var found = await roleRows.Where(row => EF.Property<string>(row, "EID") == roleId)
                .ExecuteUpdateAsync(setter => setter.SetProperty(row => EF.Property<string>(row, "EID"), row => EF.Property<string>(row, "EID")), token);
            if (found == 0) throw new RolePermissionValidationException("角色不存在。", "AUTHORIZATION_ROLE_NOT_FOUND", new Dictionary<string, object?> { ["roleId"] = roleId });
            var role = await roleRows.AsNoTracking().SingleAsync(row => EF.Property<string>(row, "EID") == roleId, token);
            if (LocalAdministratorCatalog.MatchesRoleCode((string)role["ECODE"]!))
                throw new RolePermissionValidationException("LocalAdmin 的完整功能授权由初始化维护，不能修改。", "AUTHORIZATION_FEATURE_PROTECTED");
            var grants = db.Set<Dictionary<string, object?>>(db.ResolveModelKey("RoleFunctionGrant"));
            var old = await grants.Where(row => EF.Property<string>(row, "RoleId") == roleId).ToListAsync(token);
            var oldIds = old.Select(row => (string)row["FunctionPointId"]!).ToArray();
            var oldPositions = (await db.Set<Dictionary<string, object?>>(db.ResolveModelKey("FunctionPoint")).AsNoTracking()
                .Where(row => oldIds.Contains(EF.Property<string>(row, "EID")))
                .Select(row => EF.Property<string>(row, "PositionKey")).ToListAsync(token)).ToHashSet(StringComparer.Ordinal);
            if (requested.SetEquals(oldPositions)) return true;
            var changes = oldPositions.Union(requested).Order(StringComparer.Ordinal).Where(key => oldPositions.Contains(key) != requested.Contains(key))
                .Select(key => new BusinessAuditField(key, BusinessAuditValue.Boolean(oldPositions.Contains(key)), BusinessAuditValue.Boolean(requested.Contains(key)))).ToArray();
            var stamp = stamps.GetCurrent();
            grants.RemoveRange(old);
            grants.AddRange(requested.Select(position => new Dictionary<string, object?>
            {
                ["EID"] = ids.NewId("RoleFunctionGrant"), ["RoleId"] = roleId, ["FunctionPointId"] = snapshot.ByPosition[position].Id,
                ["GrantedAt"] = stamp.OccurredAtUtc, ["GrantedBy"] = stamp.ActorId
            }));
            await recorder.RegisterAsync(new("role.features.changed", "authorization.role", roleId, changes), token);
            return true;
        }, cancellationToken);
    }
}
