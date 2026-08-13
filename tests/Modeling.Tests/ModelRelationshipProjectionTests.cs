using ICP.Platform.Modeling.Definitions;
using ICP.Platform.Modeling.Metadata;
using ICP.Platform.Modeling.Projection;
using Xunit;

namespace ICP.Platform.Modeling.Tests;

public sealed class ModelRelationshipProjectionTests
{
    [Fact]
    public void Project_PreservesIntegerRevisionAndForeignKeyOwnership()
    {
        var document = new ModelMetadataDocument
        {
            SchemaVersion = "1.0",
            ModuleId = "Identity",
            Model = new ModelMetadataModel
            {
                Name = "LocalCredential",
                Kind = "Entity",
                Fields =
                [
                    new ModelMetadataField
                    {
                        Name = "AccountEID",
                        Storage = new ModelMetadataStorageSection { Type = "guid" }
                    },
                    new ModelMetadataField
                    {
                        Name = "Revision",
                        Storage = new ModelMetadataStorageSection { Type = "long" }
                    }
                ],
                ForeignKeys =
                [
                    new ModelMetadataForeignKey
                    {
                        Name = "FK_LocalCredential_IdentityAccount",
                        Fields = ["AccountEID"],
                        PrincipalModuleId = "Identity",
                        PrincipalModel = "IdentityAccount",
                        PrincipalFields = ["EID"],
                        OnDelete = "Restrict"
                    }
                ]
            }
        };

        var definition = new ModelMetadataProjector().Project(document);

        Assert.Contains(definition.Fields, static field =>
            field is { Name: "Revision", ValueType: FieldValueType.Integer });
        var foreignKey = Assert.Single(definition.ForeignKeys);
        Assert.Equal("FK_LocalCredential_IdentityAccount", foreignKey.Name);
        Assert.Equal(["AccountEID"], foreignKey.Fields);
        Assert.Equal("Identity/IdentityAccount", foreignKey.PrincipalModelKey);
        Assert.Equal(["EID"], foreignKey.PrincipalFields);
        Assert.Equal(ForeignKeyDeleteBehavior.Restrict, foreignKey.DeleteBehavior);
    }
}
