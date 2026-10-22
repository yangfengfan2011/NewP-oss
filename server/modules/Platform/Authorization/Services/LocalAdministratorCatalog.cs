using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Crud.ObjectIds;
using ICP.Platform.Modules.Authorization.Models;
using ICP.Platform.Modules.Authorization.Repositories;
namespace ICP.Platform.Modules.Authorization.Services;
/// <summary>内部初始化只确保受保护角色存在，功能授权由完整目录同步维护。</summary>
public sealed class LocalAdministratorCatalog(IRoleRepository roles, IPlatformObjectIdGenerator ids) : ILocalAdministratorCatalog
{
    /// <summary>按规范的不区分大小写规则判断原始角色代码，不隐式修剪输入。</summary>
    public static bool MatchesRoleCode(string? roleCode) =>
        string.Equals(roleCode, LocalAdministratorRole.Code, StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc/>
    public async Task EnsureAsync(CancellationToken cancellationToken = default)
    {
        if (await roles.GetByCodeAsync(LocalAdministratorRole.Code, cancellationToken) is not null) return;
        await roles.CreateAsync(new Role { Id = ids.NewId("Role"), Code = LocalAdministratorRole.Code, Name = "Local Administrator",
            Description = "Protected platform local administrator", IsBuiltIn = true, CreatedBy = "system" }, cancellationToken);
    }
}
