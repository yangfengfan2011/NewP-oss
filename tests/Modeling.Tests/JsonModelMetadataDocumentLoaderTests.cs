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

    [Fact]
    public void LoadFromFile_ReadsDataSourceKey()
    {
        var path = Path.GetTempFileName();

        try
        {
            File.WriteAllText(
                path,
                """
                {
                  "schemaVersion": "1.0",
                  "moduleId": "Organization",
                  "model": {
                    "name": "Person",
                    "kind": "Entity",
                    "fields": [
                      {
                        "name": "EmploymentStatus",
                        "dataSource": {
                          "type": "enum",
                          "key": "EmploymentStatus"
                        }
                      }
                    ]
                  }
                }
                """);

            var loader = new JsonModelMetadataDocumentLoader();

            var document = loader.LoadFromFile(path);

            var field = Assert.Single(document.Model.Fields);
            Assert.NotNull(field.DataSource);
            Assert.Equal("enum", field.DataSource!.Type);
            Assert.Equal("EmploymentStatus", field.DataSource.Key);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
