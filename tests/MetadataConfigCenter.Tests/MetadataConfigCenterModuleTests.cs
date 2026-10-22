using ICP.Platform.Modules.MetadataConfigCenter;
using Xunit;

namespace ICP.Platform.Modules.MetadataConfigCenter.Tests;

public class MetadataConfigCenterModuleTests
{
    [Fact]
    public void ModuleId_ReturnsCorrectValue()
    {
        var module = new MetadataConfigCenterModule();

        Assert.Equal("MetadataConfigCenter", module.ModuleId);
    }

    [Fact]
    public void GetMenus_RegistersMetadataModuleOverviewRoute()
    {
        var module = new MetadataConfigCenterModule();

        var menus = module.GetMenus();

        Assert.Contains(
            menus,
            menu => menu.Route == "/metadata-config/modules");
    }
}
