using ICP.Platform.Modules.ConfigCenter.Models;
using ICP.Platform.Modules.ConfigCenter.Persistence.Descriptors;
using ICP.Platform.Modules.ConfigCenter.Persistence.Ef;
using ICP.Platform.Persistence.EfCore;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ICP.Platform.Modules.ConfigCenter.Tests;

public class EfValueListRepositoryTests
{
    [Fact]
    public async Task GetByCodeAsync_ReturnsMatchingItem()
    {
        var repository = CreateRepository();

        await repository.CreateAsync(new ValueListItem
        {
            Id = "ValueListItem_00000000000000000000000000000001",
            Code = "STATUS_ACTIVE",
            Name = "启用",
            Description = "启用状态"
        });

        var item = await repository.GetByCodeAsync("STATUS_ACTIVE");

        Assert.NotNull(item);
        Assert.Equal("启用", item!.Name);
    }

    [Fact]
    public async Task UpdateAsync_PersistsChanges()
    {
        var repository = CreateRepository();
        var item = new ValueListItem
        {
            Id = "ValueListItem_00000000000000000000000000000002",
            Code = "STATUS_ACTIVE",
            Name = "启用"
        };

        await repository.CreateAsync(item);
        item.Name = "已启用";
        item.Description = "新的说明";
        await repository.UpdateAsync(item);

        var updated = await repository.GetByIdAsync(item.Id);

        Assert.NotNull(updated);
        Assert.Equal("已启用", updated!.Name);
        Assert.Equal("新的说明", updated.Description);
    }

    private static EfValueListRepository CreateRepository()
    {
        var options = new DbContextOptionsBuilder<EfPlatformDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var dbContext = new EfPlatformDbContext(options, ConfigCenterModelDefinitions.All);
        return new EfValueListRepository(dbContext);
    }
}
