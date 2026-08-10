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
                        Name = "UserEID",
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
                        Name = "FK_LocalCredential_User",
                        Fields = ["UserEID"],
                        PrincipalModuleId = "Identity",
                        PrincipalModel = "User",
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
        Assert.Equal("FK_LocalCredential_User", foreignKey.Name);
        Assert.Equal(["UserEID"], foreignKey.Fields);
        Assert.Equal("Identity/User", foreignKey.PrincipalModelKey);
        Assert.Equal(["EID"], foreignKey.PrincipalFields);
        Assert.Equal(ForeignKeyDeleteBehavior.Restrict, foreignKey.DeleteBehavior);
    }
}
