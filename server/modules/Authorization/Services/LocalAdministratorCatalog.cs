using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Crud.ObjectIds;
using ICP.Platform.Modules.Authorization.Models;
using ICP.Platform.Modules.Authorization.Repositories;
namespace ICP.Platform.Modules.Authorization.Services;
/// <summary>内部目录同步保留原有初始化路径，普通 API 无法选择该入口。</summary>
public sealed class LocalAdministratorCatalog(IRoleRepository roles, IPermissionRepository permissions,
    IRolePermissionRepository links, IPlatformObjectIdGenerator ids) : ILocalAdministratorCatalog
{
    /// <inheritdoc/>
    public async Task SynchronizeAsync(IReadOnlyCollection<string> codes, CancellationToken cancellationToken = default)
    {
        var role = await roles.GetByCodeAsync("LocalAdmin", cancellationToken);
        if (role is null)
        {
            role = new Role { Id = ids.NewId("Role"), Code = "LocalAdmin", Name = "Local Administrator", Description = "Protected platform local administrator", IsBuiltIn = true, CreatedBy = "system" };
            await roles.CreateAsync(role, cancellationToken);
        }
        var selected = await permissions.GetByCodesAsync(codes, cancellationToken);
        if (!codes.ToHashSet(StringComparer.Ordinal).SetEquals(selected.Select(p => p.Code)))
            throw new InvalidOperationException("AUTH_LOCAL_ADMIN_CATALOG_MISMATCH: active permissions were not synchronized.");
        var existing = await links.GetByRoleIdAsync(role.Id, cancellationToken);
        if (existing.Select(l => l.PermissionId).ToHashSet(StringComparer.Ordinal).SetEquals(selected.Select(p => p.Id))) return;
        await links.DeleteByRoleIdAsync(role.Id, cancellationToken);
        await links.CreateBatchAsync(selected.Select(p => new RolePermission { Id = ids.NewId("RolePermission"), RoleId = role.Id, PermissionId = p.Id, GrantedBy = "system" }), cancellationToken);
    }
}
