using System.Text.Json;
using ICP.Platform.Kernel.ModulePlanning;
using Xunit;

namespace ICP.Platform.Kernel.Tests;

public sealed class ButtonObjectArrayMetadataTests
{
    [Fact]
    public void HeterogeneousButton_UsesTheExistingLinkEndpointObjectBindings()
    {
        var declaration = Read("""[{"object":"Folder","actions":["read"]},{"object":"Part","actions":["read"]}]""");
        Assert.Null(declaration.Object);
        Assert.Empty(declaration.Actions);
        Assert.Equal(new[] { "test.folder", "part.part" }, declaration.Resources);
        Assert.Equal(new[] { "Folder", "Part" }, declaration.ObjectRequirements!.Select(item => item.Object));
        Assert.All(declaration.ObjectRequirements!, requirement => Assert.Equal(new[] { "read" }, requirement.Actions));
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("[{\"object\":\"Part\",\"actions\":[\"read\"]},{\"object\":\"Part\",\"actions\":[\"read\"]}]")]
    [InlineData("[{\"object\":\"Unknown\",\"actions\":[\"read\"]}]")]
    [InlineData("[{\"object\":\"Part\",\"actions\":[\"read\"],\"target\":\"other\"}]")]
    [InlineData("[{\"execution\":\"old\",\"resources\":[\"part.part\"]}]")]
    public void InvalidArray_DoesNotBecomeAButtonWithoutDataRequirements(string input) =>
        Assert.Throws<InvalidOperationException>(() => Read(input));

    private static ActionDataAccessDeclaration Read(string input)
    {
        using var navigation = JsonDocument.Parse("""{"navigation":{"items":[{"key":"entry","featureAuthorization":true,"targetKey":"items"}]}}""");
        using var folder = JsonDocument.Parse("""{"Folder":{"kind":"Entity"}}""");
        using var link = JsonDocument.Parse("""{"Content":{"kind":"Link","linkQuery":{"endpoints":{"Folder":{"resourceKey":"test.folder"},"Part":{"resourceKey":"part.part"}}}}}""");
        using var view = JsonDocument.Parse("""{"view":{"key":"items","mode":"list","model":"Folder","resourceKey":"test.folder","rowActions":[{"key":"history","handlerKey":"history","dataAccess":INPUT}]}}"""
            .Replace("INPUT", input, StringComparison.Ordinal));
        var features = StandardFeatureMetadataCompiler.Read("Test", [navigation.RootElement, folder.RootElement, link.RootElement, view.RootElement]);
        return Assert.Single(features.SelectMany(feature => feature.DataAccess), declaration => declaration.ActionKey == "history");
    }
}
