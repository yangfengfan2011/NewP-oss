using ICP.Platform.Modules.Authorization.Models;
using ICP.Platform.Modules.Authorization.Repositories;
using ICP.Platform.Modules.Authorization.Services;
using Xunit;

namespace ICP.Platform.Modules.Authorization.Tests;

public class AuthorizationServiceTests
{
    private readonly IAuthorizationService _authorizationService;
    private readonly IRoleRepository _roleRepository;
    private readonly IPermissionRepository _permissionRepository;
    private readonly IRolePermissionRepository _rolePermissionRepository;
    private readonly IUserRoleRepository _userRoleRepository;

    public AuthorizationServiceTests()
    {
        _roleRepository = new RoleRepository();
        _permissionRepository = new PermissionRepository();
        _rolePermissionRepository = new RolePermissionRepository();
        _userRoleRepository = new UserRoleRepository();

        _authorizationService = new AuthorizationService(
            _userRoleRepository,
            _rolePermissionRepository,
            _permissionRepository,
            _roleRepository
        );
    }

    [Fact]
    public async Task HasPermission_UserWithPermission_ReturnsTrue()
    {
        // Arrange
        var userId = "user1";
        var role = new Role { Code = "Admin", Name = "管理员" };
        var permission = new Permission { Code = "Test.View", DisplayName = "查看测试", ModuleId = "Test" };

        await _roleRepository.CreateAsync(role);
        await _permissionRepository.CreateAsync(permission);
        await _rolePermissionRepository.CreateBatchAsync(new[]
        {
            new RolePermission { RoleId = role.Id, PermissionId = permission.Id }
        });
        await _userRoleRepository.CreateBatchAsync(new[]
        {
            new UserRole { UserId = userId, RoleId = role.Id }
        });

        // Act
        var hasPermission = await _authorizationService.HasPermissionAsync(userId, "Test.View");

        // Assert
        Assert.True(hasPermission);
    }

    [Fact]
    public async Task HasPermission_UserWithoutPermission_ReturnsFalse()
    {
        // Arrange
        var userId = "user1";

        // Act
        var hasPermission = await _authorizationService.HasPermissionAsync(userId, "Test.View");

        // Assert
        Assert.False(hasPermission);
    }

    [Fact]
    public async Task HasAnyPermission_UserWithOneOfPermissions_ReturnsTrue()
    {
        // Arrange
        var userId = "user1";
        var role = new Role { Code = "Editor", Name = "编辑者" };
        var permission = new Permission { Code = "Test.Edit", DisplayName = "编辑测试", ModuleId = "Test" };

        await _roleRepository.CreateAsync(role);
        await _permissionRepository.CreateAsync(permission);
        await _rolePermissionRepository.CreateBatchAsync(new[]
        {
            new RolePermission { RoleId = role.Id, PermissionId = permission.Id }
        });
        await _userRoleRepository.CreateBatchAsync(new[]
        {
            new UserRole { UserId = userId, RoleId = role.Id }
        });

        // Act
        var hasAny = await _authorizationService.HasAnyPermissionAsync(userId, new[] { "Test.View", "Test.Edit" });

        // Assert
        Assert.True(hasAny);
    }

    [Fact]
    public async Task HasAllPermissions_UserWithAllPermissions_ReturnsTrue()
    {
        // Arrange
        var userId = "user1";
        var role = new Role { Code = "SuperAdmin", Name = "超级管理员" };
        var permission1 = new Permission { Code = "Test.View", DisplayName = "查看测试", ModuleId = "Test" };
        var permission2 = new Permission { Code = "Test.Edit", DisplayName = "编辑测试", ModuleId = "Test" };

        await _roleRepository.CreateAsync(role);
        await _permissionRepository.CreateAsync(permission1);
        await _permissionRepository.CreateAsync(permission2);
        await _rolePermissionRepository.CreateBatchAsync(new[]
        {
            new RolePermission { RoleId = role.Id, PermissionId = permission1.Id },
            new RolePermission { RoleId = role.Id, PermissionId = permission2.Id }
        });
        await _userRoleRepository.CreateBatchAsync(new[]
        {
            new UserRole { UserId = userId, RoleId = role.Id }
        });

        // Act
        var hasAll = await _authorizationService.HasAllPermissionsAsync(userId, new[] { "Test.View", "Test.Edit" });

        // Assert
        Assert.True(hasAll);
    }

    [Fact]
    public async Task GetUserPermissions_ReturnsAllUserPermissions()
    {
        // Arrange
        var userId = "user1";
        var role = new Role { Code = "Admin", Name = "管理员" };
        var permission1 = new Permission { Code = "Test.View", DisplayName = "查看测试", ModuleId = "Test" };
        var permission2 = new Permission { Code = "Test.Edit", DisplayName = "编辑测试", ModuleId = "Test" };

        await _roleRepository.CreateAsync(role);
        await _permissionRepository.CreateAsync(permission1);
        await _permissionRepository.CreateAsync(permission2);
        await _rolePermissionRepository.CreateBatchAsync(new[]
        {
            new RolePermission { RoleId = role.Id, PermissionId = permission1.Id },
            new RolePermission { RoleId = role.Id, PermissionId = permission2.Id }
        });
        await _userRoleRepository.CreateBatchAsync(new[]
        {
            new UserRole { UserId = userId, RoleId = role.Id }
        });

        // Act
        var permissions = await _authorizationService.GetUserPermissionsAsync(userId);

        // Assert
        Assert.Equal(2, permissions.Count);
        Assert.Contains("Test.View", permissions);
        Assert.Contains("Test.Edit", permissions);
    }

    [Fact]
    public async Task HasPermission_DisabledRole_ReturnsFalse()
    {
        // Arrange
        var userId = "user1";
        var role = new Role { Code = "Admin", Name = "管理员", IsEnabled = false };
        var permission = new Permission { Code = "Test.View", DisplayName = "查看测试", ModuleId = "Test" };

        await _roleRepository.CreateAsync(role);
        await _permissionRepository.CreateAsync(permission);
        await _rolePermissionRepository.CreateBatchAsync(new[]
        {
            new RolePermission { RoleId = role.Id, PermissionId = permission.Id }
        });
        await _userRoleRepository.CreateBatchAsync(new[]
        {
            new UserRole { UserId = userId, RoleId = role.Id }
        });

        // Act
        var hasPermission = await _authorizationService.HasPermissionAsync(userId, "Test.View");

        // Assert
        Assert.False(hasPermission);
    }
}
