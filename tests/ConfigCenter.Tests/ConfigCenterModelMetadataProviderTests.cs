using ICP.Platform.Modules.ConfigCenter.Persistence.Descriptors;
using Xunit;

namespace ICP.Platform.Modules.ConfigCenter.Tests;

public class ConfigCenterModelMetadataProviderTests
{
    [Fact]
    public void LoadConfigItem_ReturnsDefinitionFromMetadataFile()
    {
        var provider = new ConfigCenterModelMetadataProvider();

        var definition = provider.LoadConfigItem();

        Assert.Equal("ConfigItem", definition.Name);
        Assert.Contains(definition.Fields, static x => x.Name == "EID" && x.IsPrimaryKey);
        Assert.Contains(definition.Fields, static x => x.Name == "ENAME");
        Assert.Contains(definition.Fields, static x => x.Name == "Value");
    }
}
