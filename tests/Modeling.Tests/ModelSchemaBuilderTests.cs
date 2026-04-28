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
                            Width = 50,
                            Section = "basic"
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
                        },
                        DataSource = new ModelMetadataDataSource
                        {
                            Type = "model",
                            Key = "Owner",
                            Model = "User"
                        }
                    },
                    new ModelMetadataField
                    {
                        Name = "Description"
                    }
                ]
            }
        };

        var schema = new ModelSchemaBuilder().Build(document);

        Assert.Equal("ConfigItem", schema.ModelName);

        var nameField = Assert.Single(schema.Fields, static x => x.Name == "ENAME");
        Assert.Equal("text", nameField.UiType);
        Assert.Equal("Model.ConfigItem.Field.ENAME", nameField.LabelKey);
        Assert.NotNull(nameField.Layout);
        Assert.Equal("010", nameField.Layout!.Position);
        Assert.Equal(50, nameField.Layout.Width);
        Assert.Equal("basic", nameField.Layout.Section);
        Assert.NotNull(nameField.Form);
        Assert.Equal("editable", nameField.Form!.CreateMode);
        Assert.Equal("editable", nameField.Form.EditMode);
        Assert.NotNull(nameField.View);
        Assert.True(nameField.View!.Visible);
        Assert.NotNull(nameField.DataSource);
        Assert.Equal("model", nameField.DataSource!.Type);
        Assert.Equal("Owner", nameField.DataSource.Key);
        Assert.Equal("User", nameField.DataSource.Model);

        var descriptionField = Assert.Single(schema.Fields, static x => x.Name == "Description");
        Assert.Equal("readonly", descriptionField.UiType);
        Assert.Null(descriptionField.Layout);
        Assert.Null(descriptionField.Form);
        Assert.Null(descriptionField.View);
        Assert.Null(descriptionField.DataSource);
    }
}
