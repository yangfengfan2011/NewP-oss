using ICP.Platform.Modules.Authorization.Models;
using ICP.Platform.Modules.Authorization.Repositories;
using ICP.Platform.Modules.Authorization.Services;
using Xunit;

namespace ICP.Platform.Modules.Authorization.Tests;

public class PermissionServiceTests
{
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
        var service = new PermissionService(repository);

        var dto = Assert.Single(await service.GetAllPermissionsAsync());

        Assert.Equal(createdAt, dto.CreatedAt);
    }
}
