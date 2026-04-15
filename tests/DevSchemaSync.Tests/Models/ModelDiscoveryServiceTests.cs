using ICP.Platform.Tools.DevSchemaSync.Configuration;
using ICP.Platform.Tools.DevSchemaSync.Models;
using Xunit;

namespace ICP.Platform.DevSchemaSync.Tests.Models;

public class ModelDiscoveryServiceTests
{
    [Fact]
    public void DiscoverAll_FindsConfigCenterModelFromModulesTree()
    {
        var repositoryRoot = RepositoryRootLocator.Find();
        var service = new ModelDiscoveryService(new SingleRootModelDocumentLoader());

        var documents = service.DiscoverAll(repositoryRoot);

        Assert.Contains(documents, document => document.ModuleId == "ConfigCenter" && document.ModelName == "ConfigItem");
    }

    [Fact]
    public void DiscoverAll_SortsByModuleIdThenModelName()
    {
        var sampleRepositoryRoot = RepositoryRootLocator.Find();
        var sampleFile = Path.Combine(sampleRepositoryRoot, "modules", "ConfigCenter", "metadata", "models", "ConfigItem.model.json");
        var sampleJson = File.ReadAllText(sampleFile);

        var tempRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var alphaPath = Path.Combine(tempRoot, "modules", "Alpha", "metadata", "models", "ConfigItem.model.json");
        var configCenterPath = Path.Combine(tempRoot, "modules", "ConfigCenter", "metadata", "models", "ConfigItem.model.json");

        Directory.CreateDirectory(Path.GetDirectoryName(alphaPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(configCenterPath)!);
        File.WriteAllText(alphaPath, sampleJson);
        File.WriteAllText(configCenterPath, sampleJson);

        try
        {
            var service = new ModelDiscoveryService(new SingleRootModelDocumentLoader());
            var documents = service.DiscoverAll(tempRoot);

            Assert.Equal(2, documents.Count);
            Assert.Equal("Alpha", documents[0].ModuleId);
            Assert.Equal("ConfigCenter", documents[1].ModuleId);
            Assert.All(documents, document => Assert.Equal("ConfigItem", document.ModelName));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }
}
