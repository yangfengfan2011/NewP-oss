using ICP.Platform.Modules.ConfigCenter.Imports;
using Xunit;

namespace ConfigCenter.Tests;

public class LegacySettingXmlParserTests
{
    private readonly LegacySettingXmlParser _parser;

    public LegacySettingXmlParserTests()
    {
        _parser = new LegacySettingXmlParser();
    }

    [Fact]
    public void Parse_ValidXml_ReturnsCorrectItems()
    {
        var xml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<root>
    <AppSet Key=""TestKey1"" Value=""TestValue1"" ToClient=""true"" Description=""Test description 1"" />
    <AppSet Key=""TestKey2"" Value=""TestValue2"" ToClient=""false"" Description=""Test description 2"" />
</root>";

        var result = _parser.Parse(xml, "TestSettings.setting").ToList();

        Assert.Equal(2, result.Count);

        Assert.Equal("TestKey1", result[0].Key);
        Assert.Equal("TestValue1", result[0].Value);
        Assert.True(result[0].ToClient);
        Assert.Equal("Test description 1", result[0].Description);
        Assert.Equal("TestSettings", result[0].Group);
        Assert.Equal("TestSettings.setting", result[0].SourceFile);

        Assert.Equal("TestKey2", result[1].Key);
        Assert.Equal("TestValue2", result[1].Value);
        Assert.False(result[1].ToClient);
        Assert.Equal("Test description 2", result[1].Description);
        Assert.Equal("TestSettings", result[1].Group);
        Assert.Equal("TestSettings.setting", result[1].SourceFile);
    }

    [Fact]
    public void Parse_MissingKey_ThrowsInvalidOperationException()
    {
        var xml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<root>
    <AppSet Value=""TestValue"" ToClient=""true"" Description=""Test description"" />
</root>";

        var exception = Assert.Throws<InvalidOperationException>(() =>
            _parser.Parse(xml, "TestSettings.setting").ToList());

        Assert.Contains("Key attribute is missing or empty", exception.Message);
    }

    [Fact]
    public void Parse_EmptyKey_ThrowsInvalidOperationException()
    {
        var xml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<root>
    <AppSet Key="""" Value=""TestValue"" />
</root>";

        var exception = Assert.Throws<InvalidOperationException>(() =>
            _parser.Parse(xml, "TestSettings.setting").ToList());

        Assert.Contains("Key attribute is missing or empty", exception.Message);
    }
}
