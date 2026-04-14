using ICP.Platform.Modeling;
using ICP.Platform.Modeling.Metadata;
using ICP.Platform.Modeling.Projection;
using Xunit;

namespace ICP.Platform.Modeling.Tests;

public class ModelMetadataProjectorTests
{
    [Fact]
    public void Project_EntityAddsFixedFieldsAndBuiltinIndexes()
    {
        var document = new ModelMetadataDocument
        {
            SchemaVersion = "1.0",
            ModuleId = "ConfigCenter",
            Model = new ModelMetadataModel
            {
                Name = "ConfigItem",
                Kind = "Entity",
                BuiltinFields = ["ENAME"]
            }
        };

        var projector = new ModelMetadataProjector();

        var definition = projector.Project(document);

        Assert.Contains(definition.Fields, static field => field.Name == "EID" && field.IsPrimaryKey);
        Assert.Contains(definition.Fields, static field => field.Name == "ENAME");
        Assert.Contains(definition.Indexes, static index => index.Name == "UX_ConfigItem_EID" && index.IsUnique);
    }

    [Fact]
    public void Project_UsesStorageTypeMapForProviderStoreType()
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
                        Name = "Title",
                        Storage = new ModelMetadataStorageSection
                        {
                            Type = "string",
                            Nullable = false
                        }
                    }
                ]
            }
        };

        var typeMap = new StorageTypeMapDocument
        {
            Types = new Dictionary<string, StorageTypeDefinition>(StringComparer.OrdinalIgnoreCase)
            {
                ["string"] = new()
                {
                    Capabilities = new StorageTypeCapabilities(),
                    Providers = new Dictionary<string, StorageProviderDefinition>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["SqlServer"] = new()
                        {
                            StoreType = "nvarchar",
                            DefaultLength = 200
                        }
                    }
                }
            }
        };

        var projector = new ModelMetadataProjector();

        var definition = projector.Project(document, typeMap, "SqlServer");

        Assert.Contains(definition.Fields, static field => field.Name == "Title" && field.StoreType == "nvarchar");
    }

    [Fact]
    public void Project_LinkAddsFixedFields()
    {
        var document = new ModelMetadataDocument
        {
            SchemaVersion = "1.0",
            ModuleId = "Organization",
            Model = new ModelMetadataModel
            {
                Name = "Organize",
                Kind = "Link",
                LinkedTypes =
                [
                    new ModelMetadataLinkedType
                    {
                        Parent = "Organization",
                        Child = "Company"
                    }
                ]
            }
        };

        var projector = new ModelMetadataProjector();

        var definition = projector.Project(document);

        Assert.Contains(definition.Fields, static field => field.Name == "LID" && field.IsPrimaryKey);
        Assert.Contains(definition.Fields, static field => field.Name == "PEID");
        Assert.Contains(definition.Fields, static field => field.Name == "CEID");
    }

    [Fact]
    public void Project_AddsBuiltinAndCustomIndexes()
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
                Indexes =
                [
                    new ModelMetadataIndex
                    {
                        Name = "UX_ConfigItem_ENAME",
                        Fields = ["ENAME"],
                        Unique = true
                    }
                ]
            }
        };

        var projector = new ModelMetadataProjector();

        var definition = projector.Project(document);

        Assert.Contains(definition.Indexes, static index => index.Name == "UX_ConfigItem_EID" && index.IsUnique);
        Assert.Contains(definition.Indexes, static index => index.Name == "UX_ConfigItem_ENAME" && index.IsUnique);
    }
}
