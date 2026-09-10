using ICP.Platform.Crud.ObjectIds;
using ICP.Platform.Modules.Audit.Contracts;
using ICP.Platform.Persistence.Abstractions;
using ICP.Platform.Persistence.Auditing;
using ICP.Platform.Persistence.EfCore;
using Microsoft.EntityFrameworkCore;
namespace ICP.Platform.Modules.Authorization.Services;
/// <summary>普通角色权限整组替换，客户端不提供事件、操作者或历史值。</summary>
public interface IRolePermissionWriter
{
    /// <summary>完整验证并在同一事务中替换关系和登记实际差异。</summary>
    Task ReplaceAsync(string roleId, IReadOnlyCollection<string> codes, CancellationToken cancellationToken = default);
}
/// <summary>Authorization 自有写入边界，Audit 只经稳定契约参与登记。</summary>
public sealed class RolePermissionWriter(EfPlatformDbContext db, IPlatformTransactionExecutor transactions,
    ActivePermissionCatalog catalog, IPlatformObjectIdGenerator ids, IBusinessAuditRecorder recorder, IEntityAuditStampProvider stamps) : IRolePermissionWriter
{
    /// <inheritdoc/>
    public async Task ReplaceAsync(string roleId, IReadOnlyCollection<string> codes, CancellationToken cancellationToken = default)
    {
        if (codes is null) throw new RolePermissionValidationException("必须提供完整权限集合。");
        var requested = codes.ToHashSet(StringComparer.Ordinal);
        if (requested.Any(c => !catalog.Codes.Contains(c)))
            throw new RolePermissionValidationException("权限集合包含当前活动目录之外的代码。");
        await transactions.ExecuteAsync(async token =>
        {
            var roles = db.Set<Dictionary<string, object?>>(db.ResolveModelKey("Role"));
            // 主键恒等更新持有角色写锁到提交，串行化实际被替换集合的读取。
            var found = await roles.Where(r => EF.Property<string>(r, "EID") == roleId)
                .ExecuteUpdateAsync(s => s.SetProperty(r => EF.Property<string>(r, "EID"), r => EF.Property<string>(r, "EID")), token);
            if (found == 0) throw new RolePermissionValidationException("角色不存在。");
            var role = await roles.AsNoTracking().SingleAsync(r => EF.Property<string>(r, "EID") == roleId, token);
            if (string.Equals((string)role["ECODE"]!, "LocalAdmin", StringComparison.OrdinalIgnoreCase))
                throw new RolePermissionValidationException("本地管理员权限只能由启动期目录同步维护。");
            var permissionRows = db.Set<Dictionary<string, object?>>(db.ResolveModelKey("Permission"));
            var selected = await permissionRows.AsNoTracking().Where(p => requested.Contains(EF.Property<string>(p, "ECODE")))
                .Select(p => new { Id = EF.Property<string>(p, "EID"), Code = EF.Property<string>(p, "ECODE") }).ToListAsync(token);
            if (!requested.SetEquals(selected.Select(p => p.Code))) throw new RolePermissionValidationException("活动权限目录尚未同步。");
            var links = db.Set<Dictionary<string, object?>>(db.ResolveModelKey("RolePermission"));
            var old = await links.Where(r => EF.Property<string>(r, "RoleId") == roleId).ToListAsync(token);
            var oldIds = old.Select(r => (string)r["PermissionId"]!).ToArray();
            var oldCodes = (await permissionRows.AsNoTracking().Where(p => oldIds.Contains(EF.Property<string>(p, "EID")))
                .Select(p => EF.Property<string>(p, "ECODE")).ToListAsync(token)).ToHashSet(StringComparer.Ordinal);
            if (requested.SetEquals(oldCodes)) return true;
            var fields = oldCodes.Union(requested).Order(StringComparer.Ordinal)
                .Where(c => oldCodes.Contains(c) != requested.Contains(c))
                .Select(c => new BusinessAuditField(c, BusinessAuditValue.Boolean(oldCodes.Contains(c)), BusinessAuditValue.Boolean(requested.Contains(c)))).ToArray();
            var stamp = stamps.GetCurrent();
            links.RemoveRange(old);
            links.AddRange(selected.Select(p => new Dictionary<string, object?>
            {
                ["EID"] = ids.NewId("RolePermission"), ["RoleId"] = roleId, ["PermissionId"] = p.Id,
                ["GrantedAt"] = stamp.OccurredAtUtc, ["GrantedBy"] = stamp.ActorId
            }));
            await recorder.RegisterAsync(new("role.permissions.changed", "authorization.role", roleId, fields), token);
            return true;
        }, cancellationToken);
    }
}

/// <summary>可翻译为请求校验失败的权限配置错误；存储与提交异常继续传播。</summary>
public sealed class RolePermissionValidationException(string message) : InvalidOperationException(message);
