using ICP.Platform.Modeling.Validation;
using ICP.Platform.Modules.ConfigCenter.Persistence.Descriptors;
using Xunit;

namespace ICP.Platform.Modules.ConfigCenter.Tests;

public class ConfigCenterModelDefinitionTests
{
    [Fact]
    public void ConfigItem_DefinitionContainsPrimaryKeyAndUniqueIndex()
    {
        var definition = ConfigCenterModelDefinitions.ConfigItem;

        Assert.Contains(definition.Fields, static field => field.Name == "Id" && field.IsPrimaryKey);
        Assert.Contains(
            definition.Indexes,
            static index => index.Name == "UX_ConfigItem_Key_Environment_Tenant" && index.IsUnique);

        var validator = new ModelDefinitionValidator();
        validator.Validate(definition);
    }
}
