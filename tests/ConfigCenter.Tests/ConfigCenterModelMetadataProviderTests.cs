using ICP.Platform.Modules.ConfigCenter.Persistence.Descriptors;
using Xunit;

namespace ICP.Platform.Modules.ConfigCenter.Tests;

public class ConfigCenterModelMetadataProviderTests
{
    [Fact]
    public void GetAll_LoadsConfigItemAndValueListItemFromMetadataFiles()
    {
        var provider = new ConfigCenterModelMetadataProvider();

        var definitions = provider.GetAll();

        Assert.Equal(2, definitions.Count);
        Assert.Contains(definitions, definition => definition.Name == "ConfigItem" && definition.TableName == "config_items");
        Assert.Contains(definitions, definition => definition.Name == "ValueListItem" && definition.TableName == "value_list_items");
    }

    [Fact]
    public void ConfigCenterModelDefinitions_ReuseSameMetadataChain()
    {
        var provider = new ConfigCenterModelMetadataProvider();

        var configItem = provider.GetRequired("ConfigItem");
        var valueListItem = provider.GetRequired("ValueListItem");

        Assert.Equal(configItem, ConfigCenterModelDefinitions.ConfigItem);
        Assert.Equal(valueListItem, ConfigCenterModelDefinitions.ValueListItem);
        Assert.Equal(provider.GetAll(), ConfigCenterModelDefinitions.All);
    }
}
