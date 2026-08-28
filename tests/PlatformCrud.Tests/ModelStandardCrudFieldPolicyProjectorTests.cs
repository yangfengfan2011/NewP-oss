using System.Text.Json;
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Planning;
using ICP.Platform.Modeling.Metadata;
using ICP.Platform.Modeling.Projection;
using Xunit;

namespace ICP.Platform.Crud.Tests;

public sealed class ModelStandardCrudFieldPolicyProjectorTests
{
    [Fact]
    public void Project_SeparatesStorageInputOwnershipAndCreateDefaultFromOneModelAuthoringFact()
    {
        var document = new ModelMetadataDocument
        {
            SchemaVersion = "1.0",
            ModuleId = "Example",
            Model = new ModelMetadataModel
            {
                Name = "ExampleRecord",
                Kind = "Entity",
                Fields =
                [
                    new ModelMetadataField
                    {
                        Name = "RecordSource",
                        Storage = new ModelMetadataStorageSection
                        {
                            Type = "string",
                            Nullable = false
                        },
                        Form = new ModelMetadataFormSection
                        {
                            Create = new ModelMetadataFormModeRule
                            {
                                Mode = "editable",
                                Required = true,
                                DefaultValue = JsonSerializer.SerializeToElement("Manual")
                            },
                            Edit = new ModelMetadataFormModeRule
                            {
                                Mode = "editable",
                                Required = true
                            }
                        }
                    }
                ]
            }
        };
        var model = new ModelMetadataProjector().Project(document);
        var fieldSet = new StandardCrudFieldSet(
            ListFields: ["RecordSource"],
            DetailFields: ["RecordSource", "CreatedBy"],
            CreateFields: ["RecordSource"],
            UpdateFields: ["RecordSource"]);

        var policies = new ModelStandardCrudFieldPolicyProjector().Project(document, model, fieldSet);

        var source = Assert.Single(policies, static policy => policy.Field == "RecordSource");
        Assert.True(source.StorageRequired);
        Assert.True(source.CreateInputRequired);
        Assert.True(source.UpdateNonEmpty);
        Assert.Equal(StandardCrudFieldValueOwner.Caller, source.ValueOwner);
        Assert.Equal("Manual", source.CreateDefaultValue);

        var createdBy = Assert.Single(policies, static policy => policy.Field == "CreatedBy");
        Assert.True(createdBy.StorageRequired);
        Assert.False(createdBy.CreateInputRequired);
        Assert.False(createdBy.UpdateNonEmpty);
        Assert.Equal(StandardCrudFieldValueOwner.Server, createdBy.ValueOwner);
        Assert.Null(createdBy.CreateDefaultValue);
    }
}
