using ICP.Platform.Modeling.Metadata;
using ICP.Platform.Modeling.Schema;
using Xunit;

namespace ICP.Platform.Modeling.Tests;

public class ModelSchemaBuilderTests
{
    [Fact]
    public void Build_ReturnsResolvedUiTypeAndFormModes()
    {
        var document = new ModelMetadataDocument
        {
            SchemaVersion = "1.0",
            ModuleId = "ConfigCenter",
            Model = new ModelMetadataModel
            {
                Name = "ConfigItem",
                Kind = "Entity",
                BuiltinFields = ["ENAME"],
                Fields =
                [
                    new ModelMetadataField
                    {
                        Name = "ENAME",
                        UiType = "text",
                        Layout = new ModelMetadataLayoutSection
                        {
                            Position = "010",
                            Width = 50
                        },
                        Form = new ModelMetadataFormSection
                        {
                            Create = new ModelMetadataFormModeRule
                            {
                                Mode = "editable"
                            },
                            Edit = new ModelMetadataFormModeRule
                            {
                                Mode = "editable"
                            }
                        },
                        View = new ModelMetadataViewSection
                        {
                            Visible = true
                        }
                    }
                ]
            }
        };

        var schema = new ModelSchemaBuilder().Build(document);

        Assert.Equal("ConfigItem", schema.ModelName);
        Assert.Contains(schema.Fields, static x => x.Name == "ENAME" && x.UiType == "text" && x.LabelKey == "Model.ConfigItem.Field.ENAME");
    }
}
