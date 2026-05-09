using ICP.Platform.Crud.ObjectIds;
using ICP.Platform.Modules.ConfigCenter.DTOs;
using ICP.Platform.Modules.ConfigCenter.Repositories;
using ICP.Platform.Modules.ConfigCenter.Services;
using Xunit;

namespace ICP.Platform.Modules.ConfigCenter.Tests;

public class ConfigCenterModuleTests
{
    [Fact]
    public void GetMenus_IncludesValueListMenu()
    {
        var module = new ConfigCenterModule();

        var menus = module.GetMenus();

        Assert.Contains(
            menus,
            menu => menu.Id == "ConfigCenter.ValueLists" &&
                    menu.ParentId == "ConfigCenter" &&
                    menu.Route == "/config/value-lists");
    }

    [Fact]
    public async Task CreateConfigAsync_AssignsConfigItemPrefixedId()
    {
        var service = new ConfigService(
            new ConfigRepository(),
            new FixedObjectIdGenerator("ConfigItem_00000000000000000000000000000001"));

        var created = await service.CreateConfigAsync(new ConfigItemDto
        {
            Key = "Test.Protocol",
            Type = "string",
            ModuleId = "Test",
            DisplayName = "协议测试"
        });

        Assert.Equal("ConfigItem_00000000000000000000000000000001", created.Id);
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
