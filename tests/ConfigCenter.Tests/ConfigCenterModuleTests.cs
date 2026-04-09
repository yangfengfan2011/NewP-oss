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
}
