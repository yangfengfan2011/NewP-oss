using ICP.Platform.Tools.DevSchemaSync.Configuration;
using ICP.Platform.Tools.DevSchemaSync.Models;
using Xunit;

namespace ICP.Platform.DevSchemaSync.Tests.Models;

public class SingleRootModelDocumentLoaderTests
{
    [Fact]
    public void LoadFromFile_MapsSingleRootModelDocument()
    {
        var repositoryRoot = RepositoryRootLocator.Find();
        var path = Path.Combine(repositoryRoot, "modules", "ConfigCenter", "metadata", "models", "ConfigItem.model.json");

        var loader = new SingleRootModelDocumentLoader();
        var document = loader.LoadFromFile(path);

        Assert.Equal("1.0", document.SchemaVersion);
        Assert.Equal("ConfigCenter", document.ModuleId);
        Assert.Equal("ConfigItem", document.Model.Name);
    }
}
