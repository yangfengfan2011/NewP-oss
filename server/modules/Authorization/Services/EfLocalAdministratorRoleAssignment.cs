using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Crud.ObjectIds;
using ICP.Platform.Persistence.EfCore;
using Microsoft.EntityFrameworkCore;

namespace ICP.Platform.Modules.Authorization.Services;

public sealed class EfLocalAdministratorRoleAssignment(
    EfPlatformDbContext dbContext,
    IPlatformObjectIdGenerator objectIdGenerator) : ILocalAdministratorRoleAssignment
{
    public async ValueTask AssignAsync(
        string accountEid,
        string actorId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountEid);
        ArgumentException.ThrowIfNullOrWhiteSpace(actorId);
        if (dbContext.Database.IsRelational() && dbContext.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("LocalAdmin assignment requires IPlatformTransactionExecutor.");
        }

        var role = await FindRoleAsync(cancellationToken);
        if (role is null)
        {
            throw new InvalidOperationException("AUTHORIZATION_LOCAL_ADMIN_ROLE_UNAVAILABLE");
        }

        var roleId = GetString(role, "EID");
        var existing = await UserRoles
            .Where(item => EF.Property<string>(item, "UserId") == accountEid
                && EF.Property<string>(item, "RoleId") == roleId)
            .ToListAsync(cancellationToken);
        if (existing.Count == 1)
        {
            return;
        }

        if (existing.Count > 1)
        {
            throw new InvalidOperationException("AUTHORIZATION_LOCAL_ADMIN_ASSIGNMENT_INVALID");
        }

        // 这里只登记关系，不保存；Identity 维护服务拥有的共享事务统一提交或回滚全部跨上下文变化。
        UserRoles.Add(new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["EID"] = objectIdGenerator.NewId("UserRole"),
            ["UserId"] = accountEid,
            ["RoleId"] = roleId,
            ["AssignedAt"] = DateTime.UtcNow,
            ["AssignedBy"] = actorId
        });
    }

    public async ValueTask<LocalAdministratorAssignmentState> InspectAsync(
        string accountEid,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountEid);
        var role = await FindRoleAsync(cancellationToken);
        if (role is null)
        {
            return LocalAdministratorAssignmentState.RoleUnavailable;
        }

        var roleId = GetString(role, "EID");
        var count = await UserRoles.CountAsync(
            item => EF.Property<string>(item, "UserId") == accountEid
                && EF.Property<string>(item, "RoleId") == roleId,
            cancellationToken);
        return count switch
        {
            0 => LocalAdministratorAssignmentState.NotAssigned,
            1 => LocalAdministratorAssignmentState.Assigned,
            _ => LocalAdministratorAssignmentState.Invalid
        };
    }

    private async ValueTask<Dictionary<string, object?>?> FindRoleAsync(CancellationToken cancellationToken)
    {
        var matches = await Roles.AsNoTracking()
            .Where(role => EF.Property<string>(role, "ECODE") == LocalAdministratorRole.Code
                && EF.Property<bool>(role, "IsEnabled"))
            .Take(2)
            .ToArrayAsync(cancellationToken);
        return matches.Length == 1 ? matches[0] : null;
    }

    private DbSet<Dictionary<string, object?>> Roles =>
        dbContext.Set<Dictionary<string, object?>>(dbContext.ResolveModelKey("Role"));

    private DbSet<Dictionary<string, object?>> UserRoles =>
        dbContext.Set<Dictionary<string, object?>>(dbContext.ResolveModelKey("UserRole"));

    private static string GetString(IReadOnlyDictionary<string, object?> row, string property) =>
        row.TryGetValue(property, out var value) && value is string text
            ? text
            : throw new InvalidOperationException("AUTHORIZATION_LOCAL_ADMIN_ROLE_INVALID");
}
