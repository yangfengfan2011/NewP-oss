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

    [Fact]
    public void LoadFromFile_ReadsReferenceDisplayRule()
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
                        "name": "CompanyId",
                        "uiType": "lookup",
                        "storage": {
                          "type": "guid",
                          "nullable": true
                        },
                        "dataSource": {
                          "type": "provider",
                          "key": "organization.companies"
                        },
                        "display": {
                          "type": "reference",
                          "targetModel": "Company",
                          "targetIdField": "EID",
                          "targetDisplayField": "ENAME"
                        }
                      }
                    ]
                  }
                }
                """);

            var document = new JsonModelMetadataDocumentLoader().LoadFromFile(path);

            var display = Assert.Single(document.Model.Fields).Display;
            Assert.NotNull(display);
            Assert.Equal("reference", display!.Type);
            Assert.Equal("Company", display.TargetModel);
            Assert.Equal("EID", display.TargetIdField);
            Assert.Equal("ENAME", display.TargetDisplayField);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
