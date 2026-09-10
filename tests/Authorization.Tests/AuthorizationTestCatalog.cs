using ICP.Platform.Crud.ObjectIds;
using ICP.Platform.Modules.Authorization.Models;
using ICP.Platform.Modules.Authorization.Repositories;
using ICP.Platform.Modules.Authorization.Services;
namespace ICP.Platform.Modules.Authorization.Tests;
internal static class AuthorizationTestCatalog
{
    internal static ActivePermissionCatalog Create(params string[] codes)
    {
        var catalog = new ActivePermissionCatalog();
        catalog.Publish(codes.Select(c => new ICP.Platform.Kernel.Abstractions.PermissionDescriptor { Code = c, DisplayName = c, ModuleId = "Test" }));
        return catalog;
    }
}
// RoleService 单元只验证分派和标识；原子写入的真实行为由 SQL HTTP 证明。
internal sealed class TestRolePermissionWriter(IPermissionRepository permissions, IRolePermissionRepository links, IPlatformObjectIdGenerator ids) : IRolePermissionWriter
{
    public async Task ReplaceAsync(string roleId, IReadOnlyCollection<string> codes, CancellationToken cancellationToken = default)
    {
        var selected = await permissions.GetByCodesAsync(codes, cancellationToken);
        await links.DeleteByRoleIdAsync(roleId, cancellationToken);
        await links.CreateBatchAsync(selected.Select(p => new RolePermission { Id = ids.NewId("RolePermission"), RoleId = roleId, PermissionId = p.Id }), cancellationToken);
    }
}
