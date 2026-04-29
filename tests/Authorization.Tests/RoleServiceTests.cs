using ICP.Platform.Modules.Authorization.DTOs;
using ICP.Platform.Modules.Authorization.Models;
using ICP.Platform.Modules.Authorization.Repositories;
using ICP.Platform.Modules.Authorization.Services;
using Xunit;

namespace ICP.Platform.Modules.Authorization.Tests;

public class RoleServiceTests
{
    private readonly IRoleService _roleService;
    private readonly IRoleRepository _roleRepository;
    private readonly IPermissionRepository _permissionRepository;
    private readonly IRolePermissionRepository _rolePermissionRepository;
    private readonly IUserRoleRepository _userRoleRepository;

    public RoleServiceTests()
    {
        _roleRepository = new RoleRepository();
        _permissionRepository = new PermissionRepository();
        _rolePermissionRepository = new RolePermissionRepository();
        _userRoleRepository = new UserRoleRepository();

        _roleService = new RoleService(
            _roleRepository,
            _permissionRepository,
            _rolePermissionRepository,
            _userRoleRepository
        );
    }

    [Fact]
    public async Task CreateRole_ValidRequest_CreatesRole()
    {
        // Arrange
        var request = new RoleDto
        {
            Code = "TestRole",
            Name = "测试角色",
            Description = "这是一个测试角色"
        };

        // Act
        var result = await _roleService.CreateRoleAsync(request);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("TestRole", result.Code);
        Assert.Equal("测试角色", result.Name);
    }

    [Fact]
    public async Task CreateRole_DuplicateCode_ThrowsException()
    {
        // Arrange
        var request = new RoleDto
        {
            Code = "TestRole",
            Name = "测试角色"
        };

        await _roleService.CreateRoleAsync(request);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _roleService.CreateRoleAsync(request)
        );
    }

    [Fact]
    public async Task UpdateRole_ValidRequest_UpdatesRole()
    {
        // Arrange
        var createRequest = new RoleDto
        {
            Code = "TestRole",
            Name = "测试角色"
        };
        var role = await _roleService.CreateRoleAsync(createRequest);

        var updateRequest = new RoleDto
        {
            Name = "更新后的角色",
            Description = "更新后的描述",
            IsEnabled = false
        };

        // Act
        var result = await _roleService.UpdateRoleAsync(role.Id, updateRequest, updatedBy: "reviewer");

        // Assert
        Assert.Equal("更新后的角色", result.Name);
        Assert.Equal("更新后的描述", result.Description);
        Assert.False(result.IsEnabled);
        Assert.Equal("TestRole", result.Code);
        Assert.Equal("reviewer", result.UpdatedBy);
        Assert.NotNull(result.UpdatedAt);
    }

    [Fact]
    public async Task DeleteRole_BuiltInRole_ThrowsException()
    {
        // Arrange
        var role = new Role
        {
            Code = "SystemAdmin",
            Name = "系统管理员",
            IsBuiltIn = true
        };
        await _roleRepository.CreateAsync(role);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _roleService.DeleteRoleAsync(role.Id)
        );
    }

    [Fact]
    public async Task AssignRolePermissions_ValidRequest_AssignsPermissions()
    {
        // Arrange
        var role = new Role { Code = "TestRole", Name = "测试角色" };
        await _roleRepository.CreateAsync(role);

        var permission1 = new Permission { Code = "Test.View", DisplayName = "查看", ModuleId = "Test" };
        var permission2 = new Permission { Code = "Test.Edit", DisplayName = "编辑", ModuleId = "Test" };
        await _permissionRepository.CreateAsync(permission1);
        await _permissionRepository.CreateAsync(permission2);

        var request = new AssignRolePermissionsRequest
        {
            PermissionCodes = new List<string> { "Test.View", "Test.Edit" }
        };

        // Act
        await _roleService.AssignRolePermissionsAsync(role.Id, request);

        // Assert
        var rolePermissions = await _roleService.GetRolePermissionsAsync(role.Id);
        Assert.Equal(2, rolePermissions.PermissionCodes.Count);
        Assert.Contains("Test.View", rolePermissions.PermissionCodes);
        Assert.Contains("Test.Edit", rolePermissions.PermissionCodes);
    }

    [Fact]
    public async Task AssignUserRoles_ValidRequest_AssignsRoles()
    {
        // Arrange
        var role1 = new Role { Code = "Role1", Name = "角色1" };
        var role2 = new Role { Code = "Role2", Name = "角色2" };
        await _roleRepository.CreateAsync(role1);
        await _roleRepository.CreateAsync(role2);

        var userId = "user1";
        var request = new AssignUserRolesRequest
        {
            RoleCodes = new List<string> { "Role1", "Role2" }
        };

        // Act
        await _roleService.AssignUserRolesAsync(userId, request);

        // Assert
        var userRoles = await _roleService.GetUserRolesAsync(userId);
        Assert.Equal(2, userRoles.RoleCodes.Count);
        Assert.Contains("Role1", userRoles.RoleCodes);
        Assert.Contains("Role2", userRoles.RoleCodes);
    }
}
