using ICP.Platform.Modules.Authorization.Models;
using ICP.Platform.Modules.Authorization.Repositories;
using ICP.Platform.Modules.Authorization.Services;
using Xunit;
namespace ICP.Platform.Modules.Authorization.Tests;
public sealed class AuthorizationServiceTests
{
    [Fact]
    public async Task CurrentRoleProjection_UsesOnlyAssignedEnabledRoles()
    {
        var roles = new RoleRepository();
        var members = new UserRoleRepository();
        await roles.CreateAsync(new Role { Id = "enabled", Code = "Enabled", Name = "启用" });
        await roles.CreateAsync(new Role { Id = "disabled", Code = "Disabled", Name = "停用", IsEnabled = false });
        await roles.CreateAsync(new Role { Id = "other", Code = "Other", Name = "未分配" });
        await members.CreateBatchAsync([new UserRole { UserId = "actor", RoleId = "enabled" }, new UserRole { UserId = "actor", RoleId = "disabled" }]);
        var service = new AuthorizationService(members, roles);
        Assert.Equal(["Enabled"], await service.GetUserRolesAsync("actor"));
        Assert.Empty(await service.GetUserRolesAsync("unassigned"));
    }
}
