using ICP.Platform.Crud.ObjectIds;
using ICP.Platform.Kernel.Abstractions;
using ICP.Platform.Modules.Authorization.Models;
using ICP.Platform.Modules.Authorization.Repositories;
using ICP.Platform.Modules.Authorization.Services;
using Xunit;

namespace ICP.Platform.Modules.Authorization.Tests;

public class PermissionServiceTests
{
    [Fact]
    public async Task SyncPermissionsAsync_AssignsPermissionPrefixedId()
    {
        var repository = new PermissionRepository();
        var service = new PermissionService(repository, new FixedObjectIdGenerator("Permission_00000000000000000000000000000001"));

        await service.SyncPermissionsAsync(
        [
            new PermissionDescriptor
            {
                Code = "Test.View",
                DisplayName = "查看",
                ModuleId = "Test",
                Group = "测试"
            }
        ]);

        var created = Assert.Single(await repository.GetAllAsync());
        Assert.Equal("Permission_00000000000000000000000000000001", created.Id);
    }

    [Fact]
    public async Task GetAllPermissionsAsync_FillsGeneratedContractFields()
    {
        var repository = new PermissionRepository();
        var createdAt = new DateTime(2026, 04, 29, 10, 30, 00, DateTimeKind.Utc);
        await repository.CreateAsync(new Permission
        {
            Code = "task6.read",
            DisplayName = "任务 6 读取",
            ModuleId = "Task6",
            CreatedAt = createdAt
        });
        var service = new PermissionService(repository, new PlatformObjectIdGenerator());

        var dto = Assert.Single(await service.GetAllPermissionsAsync());

        Assert.Equal(createdAt, dto.CreatedAt);
    }

    private sealed class FixedObjectIdGenerator : IPlatformObjectIdGenerator
    {
        private readonly string _id;

        public FixedObjectIdGenerator(string id)
        {
            _id = id;
        }

        public string NewId(string objectName) => _id;
    }
}
