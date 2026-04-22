using ICP.Platform.Modeling;
using ICP.Platform.Modeling.Metadata;
using ICP.Platform.Modeling.Projection;
using Xunit;

namespace ICP.Platform.Modeling.Tests;

public class ModelMetadataContractProjectionTests
{
    [Theory]
    [InlineData("enum", FieldValueType.Enum, "string")]
    [InlineData("objectid", FieldValueType.ObjectId, "guid")]
    [InlineData("bool", FieldValueType.Boolean, "bool")]
    [InlineData("datetime", FieldValueType.DateTime, "datetime")]
    [InlineData("description", FieldValueType.Description, "json")]
    [InlineData("text", FieldValueType.Text, "string")]
    [InlineData("unknown", FieldValueType.Text, "string")]
    public void Project_UsesContractValueTypeWhenPresent(
        string contractValueType,
        FieldValueType expectedValueType,
        string expectedStoreType)
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
                    CreateField(
                        "Status",
                        contractValueType,
                        storageType: expectedStoreType)
                ]
            }
        };

        var definition = new ModelMetadataProjector().Project(document);

        var field = Assert.Single(definition.Fields, static x => x.Name == "Status");
        Assert.Equal(expectedValueType, field.ValueType);
        Assert.Equal(expectedStoreType, field.StoreType);
    }

    private static ModelMetadataField CreateField(string name, string contractValueType, string storageType)
    {
        var field = new ModelMetadataField
        {
            Name = name,
            Contract = new ModelMetadataContractSection
            {
                ValueType = contractValueType
            },
            Storage = new ModelMetadataStorageSection
            {
                Type = storageType
            }
        };

        return field;
    }
}
