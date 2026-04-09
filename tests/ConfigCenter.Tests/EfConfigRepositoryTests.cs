using ICP.Platform.Modules.ConfigCenter.Models;
using ICP.Platform.Modules.ConfigCenter.Persistence.Descriptors;
using ICP.Platform.Modules.ConfigCenter.Persistence.Ef;
using ICP.Platform.Persistence.EfCore;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ICP.Platform.Modules.ConfigCenter.Tests;

public class EfConfigRepositoryTests
{
    [Fact]
    public async Task GetByKeyAsync_ReturnsTenantAndEnvironmentMatchFirst()
    {
        var repository = CreateRepository();

        await repository.CreateAsync(new ConfigItem
        {
            Key = "Test.Key",
            Value = "global",
            Type = "String",
            ModuleId = "Test",
            DisplayName = "Global"
        });

        await repository.CreateAsync(new ConfigItem
        {
            Key = "Test.Key",
            Value = "tenant-prod",
            Type = "String",
            ModuleId = "Test",
            DisplayName = "Specific",
            Environment = "Prod",
            TenantId = "tenant-a"
        });

        var item = await repository.GetByKeyAsync("Test.Key", "Prod", "tenant-a");

        Assert.NotNull(item);
        Assert.Equal("tenant-prod", item!.Value);
    }

    private static EfConfigRepository CreateRepository()
    {
        var options = new DbContextOptionsBuilder<EfPlatformDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var dbContext = new EfPlatformDbContext(options, ConfigCenterModelDefinitions.All);
        return new EfConfigRepository(dbContext);
    }
}
