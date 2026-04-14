using ICP.Platform.Modeling.Metadata;
using Xunit;

namespace ICP.Platform.Modeling.Tests;

public class JsonModelMetadataDocumentLoaderTests
{
    [Fact]
    public void Load_ReadsModelMetadataDocument()
    {
        var tempPath = Path.GetTempFileName();

        try
        {
            File.WriteAllText(
                tempPath,
                """
                {
                  "schemaVersion": "1.0",
                  "moduleId": "ConfigCenter",
                  "model": {
                    "name": "ConfigItem",
                    "kind": "Entity",
                    "builtinFields": [ "ENAME" ],
                    "fields": []
                  }
                }
                """);

            var loader = new JsonModelMetadataDocumentLoader();

            var document = loader.Load(tempPath);

            Assert.Equal("ConfigCenter", document.ModuleId);
            Assert.Equal("ConfigItem", document.Model.Name);
            Assert.Equal("Entity", document.Model.Kind);
        }
        finally
        {
            File.Delete(tempPath);
        }
    }
}
