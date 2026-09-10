using ICP.Platform.Crud.ObjectIds;
using ICP.Platform.Identity.Contracts;
using ICP.Platform.Modules.Audit.Contracts;
using ICP.Platform.Modules.Authorization.DTOs;
using ICP.Platform.Persistence.Abstractions;
using ICP.Platform.Persistence.Auditing;
using ICP.Platform.Persistence.EfCore;
using Microsoft.EntityFrameworkCore;
namespace ICP.Platform.Modules.Authorization.Services;

/// <summary>普通账号角色关系的查询与事务性完整替换。</summary>
public interface IUserRoleAssignments
{
    /// <summary>读取可引用普通账号的已配置角色，包括停用角色。</summary>
    Task<UserRoleAssignmentsDto> GetAsync(string userId, CancellationToken cancellationToken = default);
    /// <summary>保护管理员成员，完整验证后替换关系并登记实际差异。</summary>
    Task ReplaceAsync(string userId, IReadOnlyCollection<string> codes, CancellationToken cancellationToken = default);
}

/// <summary>只消费 Identity 稳定目录，关系与 Audit 共用平台事务。</summary>
public sealed class UserRoleAssignments(EfPlatformDbContext db, IIdentityAccountDirectory accounts,
    IPlatformTransactionExecutor transactions, IPlatformObjectIdGenerator ids, IBusinessAuditRecorder recorder,
    IEntityAuditStampProvider stamps) : IUserRoleAssignments
{
    /// <inheritdoc/>
    public async Task<UserRoleAssignmentsDto> GetAsync(string userId, CancellationToken cancellationToken = default)
    {
        await ValidateAccountAsync(userId, cancellationToken);
        var assignedIds = await Links().AsNoTracking().Where(r => EF.Property<string>(r, "UserId") == userId)
            .Select(r => EF.Property<string>(r, "RoleId")).ToListAsync(cancellationToken);
        var codes = await Roles().AsNoTracking().Where(r => assignedIds.Contains(EF.Property<string>(r, "EID")))
            .Select(r => EF.Property<string>(r, "ECODE")).ToListAsync(cancellationToken);
        if (codes.Any(IsAdministrator)) throw new UserRoleValidationException("本地管理员成员不进入普通账号角色管理。");
        return new() { UserId = userId, RoleCodes = codes };
    }

    /// <inheritdoc/>
    public async Task ReplaceAsync(string userId, IReadOnlyCollection<string> codes, CancellationToken cancellationToken = default)
    {
        if (codes is null || codes.Any(string.IsNullOrWhiteSpace)) throw new UserRoleValidationException("必须提供完整有效角色集合。");
        var requested = codes.ToHashSet(StringComparer.Ordinal);
        if (requested.Any(IsAdministrator)) throw new UserRoleValidationException("本地管理员成员不能通过普通管理修改。");
        await transactions.ExecuteAsync(async token =>
        {
            // 账号可能尚无任何角色行，事务级应用锁仍能串行化空集合的首次分配。
            // 锁仅属于 Authorization，不读写 Identity 私有存储；失败不自动重放。
            var resource = "Authorization.UserRoles:" + userId;
            await db.Database.ExecuteSqlInterpolatedAsync($"DECLARE @result int; EXEC @result = sys.sp_getapplock @Resource={resource}, @LockMode='Exclusive', @LockOwner='Transaction'; IF @result < 0 THROW 51000, 'User role assignment lock failed.', 1;", token);
            await ValidateAccountAsync(userId, token);
            var selected = await Roles().AsNoTracking().Where(r => requested.Contains(EF.Property<string>(r, "ECODE")))
                .Select(r => new { Id = EF.Property<string>(r, "EID"), Code = EF.Property<string>(r, "ECODE") }).ToListAsync(token);
            if (!requested.SetEquals(selected.Select(r => r.Code))) throw new UserRoleValidationException("角色集合包含未知角色。");
            var old = await Links().Where(r => EF.Property<string>(r, "UserId") == userId).ToListAsync(token);
            var oldIds = old.Select(r => (string)r["RoleId"]!).ToHashSet(StringComparer.Ordinal);
            if (await Roles().AsNoTracking().AnyAsync(r => oldIds.Contains(EF.Property<string>(r, "EID")) && EF.Property<string>(r, "ECODE") == "LocalAdmin", token))
                throw new UserRoleValidationException("本地管理员成员不能通过普通管理修改。");
            var newIds = selected.Select(r => r.Id).ToHashSet(StringComparer.Ordinal);
            if (oldIds.SetEquals(newIds)) return true;
            var changes = oldIds.Union(newIds).Order(StringComparer.Ordinal).Where(id => oldIds.Contains(id) != newIds.Contains(id))
                .Select(id => new BusinessAuditField(id, BusinessAuditValue.Reference("authorization.role", oldIds.Contains(id) ? id : null),
                    BusinessAuditValue.Reference("authorization.role", newIds.Contains(id) ? id : null))).ToArray();
            var stamp = stamps.GetCurrent();
            Links().RemoveRange(old);
            Links().AddRange(selected.Select(r => new Dictionary<string, object?> { ["EID"] = ids.NewId("UserRole"), ["UserId"] = userId,
                ["RoleId"] = r.Id, ["AssignedAt"] = stamp.OccurredAtUtc, ["AssignedBy"] = stamp.ActorId }));
            await recorder.RegisterAsync(new("user.roles.changed", "identity.account", userId, changes), token);
            return true;
        }, cancellationToken);
    }

    // 保护标记是 Identity 的独立安全事实；展示名称不参与判定。
    private async Task ValidateAccountAsync(string userId, CancellationToken token)
    {
        var account = await accounts.FindAsync(userId, token);
        if (account is null || account.Status == IdentityAccountStatus.Deleted) throw new UserRoleValidationException("账号不存在或已删除。");
        if (account.IsLocalAdministrator) throw new UserRoleValidationException("受保护账号不进入普通角色管理。");
    }
    // 只访问本模块拥有的运行时关系。
    private DbSet<Dictionary<string, object?>> Links() => db.Set<Dictionary<string, object?>>(db.ResolveModelKey("UserRole"));
    // 角色停用不影响配置资格。
    private DbSet<Dictionary<string, object?>> Roles() => db.Set<Dictionary<string, object?>>(db.ResolveModelKey("Role"));
    // 保留身份按规范的不区分大小写规则判断。
    private static bool IsAdministrator(string code) => string.Equals(code, "LocalAdmin", StringComparison.OrdinalIgnoreCase);
}

/// <summary>普通成员管理的确定性输入错误。</summary>
public sealed class UserRoleValidationException(string message) : InvalidOperationException(message);

/// <summary>账号名称经 Identity 稳定目录批量解析，已删除账号保留历史 ID。</summary>
public sealed class BusinessAuditAccountNames(IIdentityAccountDirectory accounts) : IBusinessAuditNameDirectory
{
    public string Kind => "identity.account";
    /// <inheritdoc/>
    public async Task<IReadOnlyDictionary<string, string>> FindManyAsync(IReadOnlyCollection<string> ids, CancellationToken cancellationToken = default) =>
        (await accounts.FindManyAsync(ids, cancellationToken)).ToDictionary(r => r.Key, r => r.Value.ENAME, StringComparer.Ordinal);
}
