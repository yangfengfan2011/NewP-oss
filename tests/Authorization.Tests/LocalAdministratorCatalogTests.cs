using ICP.Platform.Crud.ObjectIds;
using ICP.Platform.Modules.Authorization.Models;
using ICP.Platform.Modules.Authorization.Repositories;
using ICP.Platform.Modules.Authorization.Services;
using Xunit;
namespace ICP.Platform.Modules.Authorization.Tests;
public sealed class LocalAdministratorCatalogTests
{
    [Fact]
    public async Task StartupCatalog_ConvergesProtectedRoleWithoutAuditOrUserAssignment()
    {
        var roles = new RoleRepository();
        var permissions = new PermissionRepository();
        var links = new RolePermissionRepository();
        await permissions.CreateAsync(new Permission { Id = "Permission_1", Code = "Test.View", DisplayName = "查看", ModuleId = "Test" });
        await permissions.CreateAsync(new Permission { Id = "Permission_2", Code = "Test.Old", DisplayName = "历史", ModuleId = "Test" });
        var service = new LocalAdministratorCatalog(roles, permissions, links, new PlatformObjectIdGenerator());
        await service.SynchronizeAsync(["Test.Old"]);
        await service.SynchronizeAsync(["Test.View"]);
        await service.SynchronizeAsync(["Test.View"]);
        var admin = Assert.Single(await roles.GetAllAsync());
        Assert.Equal("LocalAdmin", admin.Code);
        Assert.True(admin.IsBuiltIn);
        var configured = Assert.Single(await links.GetByRoleIdAsync(admin.Id));
        Assert.Equal("Permission_1", configured.PermissionId);
    }

    [Fact]
    public async Task PermissionCatalog_SyncKeepsHistoricalRowsButOnlyPublishesCurrentGroups()
    {
        var permissions = new PermissionRepository();
        var service = new PermissionService(permissions, new PlatformObjectIdGenerator(), new ActivePermissionCatalog());
        await service.SyncPermissionsAsync([new() { Code = "Test.Old", DisplayName = "历史", ModuleId = "Test" }]);
        await service.SyncPermissionsAsync([new() { Code = "Test.View", DisplayName = "当前", ModuleId = "Test" }]);
        Assert.Equal(2, (await permissions.GetAllAsync()).Count);
        Assert.Equal("Test.View", Assert.Single(await service.GetAllPermissionsAsync()).Code);
        Assert.Equal("Test.View", Assert.Single(Assert.Single(await service.GetPermissionsByModuleAsync()).Permissions).Code);
    }
}
