using ICP.Platform.Modules.Authorization.Models;
using ICP.Platform.Modules.Authorization.Persistence.Descriptors;
using ICP.Platform.Modules.Authorization.Persistence.Ef;
using ICP.Platform.Modules.Authorization.Services;
using ICP.Platform.Persistence.EfCore;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ICP.Platform.Modules.Authorization.Tests;

public class EfAuthorizationRepositoryTests
{
    [Fact]
    public async Task AuthorizationService_UsingEfRepositories_ResolvesAssignedPermission()
    {
        using var dbContext = CreateDbContext();
        var roleRepository = new EfRoleRepository(dbContext);
        var permissionRepository = new EfPermissionRepository(dbContext);
        var rolePermissionRepository = new EfRolePermissionRepository(dbContext);
        var userRoleRepository = new EfUserRoleRepository(dbContext);

        var role = new Role { Code = "Admin", Name = "管理员" };
        var permission = new Permission { Code = "Test.View", DisplayName = "查看测试", ModuleId = "Test" };

        await roleRepository.CreateAsync(role);
        await permissionRepository.CreateAsync(permission);
        await rolePermissionRepository.CreateBatchAsync(new[]
        {
            new RolePermission { RoleId = role.Id, PermissionId = permission.Id }
        });
        await userRoleRepository.CreateBatchAsync(new[]
        {
            new UserRole { UserId = "user1", RoleId = role.Id }
        });

        var service = new AuthorizationService(
            userRoleRepository,
            rolePermissionRepository,
            permissionRepository,
            roleRepository);

        var hasPermission = await service.HasPermissionAsync("user1", "Test.View");

        Assert.True(hasPermission);
    }

    private static EfPlatformDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<EfPlatformDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new EfPlatformDbContext(options, AuthorizationModelDefinitions.All);
    }
}
