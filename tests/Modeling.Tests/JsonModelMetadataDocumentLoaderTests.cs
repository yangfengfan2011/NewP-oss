using ICP.Platform.Modeling.Metadata;
using Xunit;

namespace ICP.Platform.Modeling.Tests;

public class JsonModelMetadataDocumentLoaderTests
{
    [Fact]
    public void LoadFromFile_ReadsModelNameAndKind()
    {
        var path = Path.GetTempFileName();

        try
        {
            File.WriteAllText(
                path,
                """
                {
                  "schemaVersion": "1.0",
                  "moduleId": "ConfigCenter",
                  "model": {
                    "name": "ConfigItem",
                    "kind": "Entity"
                  }
                }
                """);

            var loader = new JsonModelMetadataDocumentLoader();

            var document = loader.LoadFromFile(path);

            Assert.Equal("ConfigCenter", document.ModuleId);
            Assert.Equal("ConfigItem", document.Model.Name);
            Assert.Equal("Entity", document.Model.Kind);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
