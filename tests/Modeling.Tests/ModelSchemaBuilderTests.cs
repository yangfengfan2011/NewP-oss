using ICP.Platform.Modeling.Metadata;
using ICP.Platform.Modeling.Schema;
using Xunit;

namespace ICP.Platform.Modeling.Tests;

public class ModelSchemaBuilderTests
{
    [Fact]
    public void Build_CreatesDefaultLabelKey()
    {
        var document = new ModelMetadataDocument
        {
            SchemaVersion = "1.0",
            ModuleId = "ConfigCenter",
            Model = new ModelMetadataModel
            {
                Name = "ConfigItem",
                Kind = "Entity",
                Fields =
                [
                    new ModelMetadataField
                    {
                        Name = "ENAME"
                    }
                ]
            }
        };

        var builder = new ModelSchemaBuilder();

        var schema = builder.Build(document);

        Assert.Equal("ConfigItem", schema.ModelName);
        Assert.Contains(
            schema.Fields,
            static field => field.Name == "ENAME" && field.LabelKey == "Model.ConfigItem.Field.ENAME");
    }
}
