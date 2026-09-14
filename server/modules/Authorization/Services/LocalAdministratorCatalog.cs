using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Crud.ObjectIds;
using ICP.Platform.Modules.Authorization.Models;
using ICP.Platform.Modules.Authorization.Repositories;
namespace ICP.Platform.Modules.Authorization.Services;
/// <summary>内部初始化只确保受保护角色存在，功能授权由完整目录同步维护。</summary>
public sealed class LocalAdministratorCatalog(IRoleRepository roles, IPlatformObjectIdGenerator ids) : ILocalAdministratorCatalog
{
    /// <inheritdoc/>
    public async Task EnsureAsync(CancellationToken cancellationToken = default)
    {
        if (await roles.GetByCodeAsync("LocalAdmin", cancellationToken) is not null) return;
        await roles.CreateAsync(new Role { Id = ids.NewId("Role"), Code = "LocalAdmin", Name = "Local Administrator",
            Description = "Protected platform local administrator", IsBuiltIn = true, CreatedBy = "system" }, cancellationToken);
    }
}
