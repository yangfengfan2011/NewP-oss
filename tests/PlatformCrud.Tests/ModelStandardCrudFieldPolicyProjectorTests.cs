using System.Text.Json;
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Planning;
using ICP.Platform.Modeling.Metadata;
using ICP.Platform.Modeling;
using ICP.Platform.Modeling.Definitions;
using ICP.Platform.Modeling.Projection;
using Xunit;

namespace ICP.Platform.Crud.Tests;

public sealed class ModelStandardCrudFieldPolicyProjectorTests
{
    [Fact]
    public void Project_KeepsExplicitPrimaryKeyCallerOwned()
    {
        var document = new ModelMetadataDocument
        {
            SchemaVersion = "1.0",
            ModuleId = "Example",
            Model = new ModelMetadataModel
            {
                Name = "ExplicitKey",
                Kind = "Entity",
                Fields =
                [
                    new ModelMetadataField
                    {
                        Name = "EID",
                        Form = new ModelMetadataFormSection
                        {
                            Create = new ModelMetadataFormModeRule { Mode = "editable", Required = true },
                            Edit = new ModelMetadataFormModeRule { Mode = "editable", Required = true }
                        }
                    }
                ]
            }
        };
        var model = new ModelMetadataProjector().Project(document);
        var fieldSet = new StandardCrudFieldSet(
            ListFields: ["EID"],
            DetailFields: ["EID"],
            CreateFields: ["EID"],
            UpdateFields: ["EID"]);

        var policy = Assert.Single(new ModelStandardCrudFieldPolicyProjector().Project(document, model, fieldSet));

        Assert.Equal(StandardCrudFieldValueOwner.Caller, policy.ValueOwner);
        Assert.True(policy.CreateInputRequired);
        Assert.True(policy.UpdateNonEmpty);
    }

    [Fact]
    public void Project_PreservesArtifactAuditOwnershipForNonFixedFieldName()
    {
        var document = new ModelMetadataDocument
        {
            SchemaVersion = "1.0",
            ModuleId = "Example",
            Model = new ModelMetadataModel
            {
                Name = "ExternalAudit",
                Kind = "Entity",
                Fields =
                [
                    new ModelMetadataField
                    {
                        Name = "ImportedStamp",
                        Storage = new ModelMetadataStorageSection { Type = "string" },
                        Form = new ModelMetadataFormSection
                        {
                            Create = new ModelMetadataFormModeRule { Mode = "editable", Required = true },
                            Edit = new ModelMetadataFormModeRule { Mode = "editable", Required = true }
                        }
                    }
                ]
            }
        };
        var model = new ModelDefinition(
            "ExternalAudit",
            "ExternalAudit",
            ModelKind.Entity,
            [new FieldDefinition(
                "ImportedStamp",
                "ImportedStamp",
                FieldKind.Audit,
                FieldValueType.Text,
                "string",
                true,
                false,
                true)],
            []);
        var fieldSet = new StandardCrudFieldSet(
            ListFields: ["ImportedStamp"],
            DetailFields: ["ImportedStamp"],
            CreateFields: ["ImportedStamp"],
            UpdateFields: ["ImportedStamp"]);

        var policy = Assert.Single(new ModelStandardCrudFieldPolicyProjector().Project(document, model, fieldSet));

        Assert.Equal(StandardCrudFieldValueOwner.Server, policy.ValueOwner);
        Assert.False(policy.CreateInputRequired);
        Assert.False(policy.UpdateNonEmpty);
    }

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
        Assert.True(source.Trim);
        Assert.Equal(256, source.MaxLength);

        var createdBy = Assert.Single(policies, static policy => policy.Field == "CreatedBy");
        Assert.True(createdBy.StorageRequired);
        Assert.False(createdBy.CreateInputRequired);
        Assert.False(createdBy.UpdateNonEmpty);
        Assert.Equal(StandardCrudFieldValueOwner.Server, createdBy.ValueOwner);
        Assert.Null(createdBy.CreateDefaultValue);
    }
}
