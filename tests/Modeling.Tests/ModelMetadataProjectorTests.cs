using ICP.Platform.Modeling;
using ICP.Platform.Modeling.Metadata;
using ICP.Platform.Modeling.Projection;
using Xunit;

namespace ICP.Platform.Modeling.Tests;

public class ModelMetadataProjectorTests
{
    [Fact]
    public void Project_AddsEntityBuiltinFieldsAndIgnoresLayout()
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
                        Name = "Value",
                        Storage = new ModelMetadataStorageSection
                        {
                            Type = "text",
                            Nullable = true
                        },
                        Layout = new ModelMetadataLayoutSection
                        {
                            Position = "010",
                            Width = 100
                        }
                    }
                ]
            }
        };

        var definition = new ModelMetadataProjector().Project(document);

        Assert.Equal("ConfigItem", definition.Name);
        Assert.Equal(ModelKind.Entity, definition.Kind);
        Assert.Contains(definition.Fields, static x => x is { Name: "EID", IsPrimaryKey: true, ValueType: FieldValueType.ObjectId });
        Assert.Contains(definition.Fields, static x => x is { Name: "ENAME", ValueType: FieldValueType.Text });
        Assert.Contains(definition.Fields, static x => x is { Name: "Value", ValueType: FieldValueType.Description });
    }

    [Fact]
    public void Project_UsesStorageTypeMapForStringField()
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
                        Name = "Code",
                        Storage = new ModelMetadataStorageSection
                        {
                            Type = "string"
                        }
                    }
                ]
            }
        };

        var typeMap = new StorageTypeMapDocument
        {
            Types = new Dictionary<string, StorageTypeMapEntry>(StringComparer.OrdinalIgnoreCase)
            {
                ["string"] = new()
                {
                    Providers = new Dictionary<string, StorageTypeProviderMap>(StringComparer.OrdinalIgnoreCase)
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

        var definition = new ModelMetadataProjector().Project(document, typeMap, "SqlServer");

        Assert.Contains(definition.Fields, static x => x is { Name: "Code", ValueType: FieldValueType.Text, StoreType: "nvarchar" });
    }

    [Fact]
    public void Project_AddsLinkFixedFields()
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

        var definition = new ModelMetadataProjector().Project(document);

        Assert.Equal(ModelKind.Link, definition.Kind);
        Assert.Contains(definition.Fields, static x => x is { Name: "LID", IsPrimaryKey: true, ValueType: FieldValueType.ObjectId });
        Assert.Contains(definition.Fields, static x => x is { Name: "PEID", ValueType: FieldValueType.ObjectId });
        Assert.Contains(definition.Fields, static x => x is { Name: "CEID", ValueType: FieldValueType.ObjectId });
    }

    [Fact]
    public void Project_AddsBuiltInAndCustomIndexes()
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
                        Name = "Code",
                        Storage = new ModelMetadataStorageSection
                        {
                            Type = "string"
                        }
                    }
                ],
                Indexes =
                [
                    new ModelMetadataIndex
                    {
                        Name = "UX_ConfigItem_Code",
                        Fields = ["Code"],
                        Unique = true
                    }
                ]
            }
        };

        var typeMap = new StorageTypeMapDocument
        {
            Types = new Dictionary<string, StorageTypeMapEntry>(StringComparer.OrdinalIgnoreCase)
            {
                ["string"] = new()
                {
                    Providers = new Dictionary<string, StorageTypeProviderMap>(StringComparer.OrdinalIgnoreCase)
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

        var definition = new ModelMetadataProjector().Project(document, typeMap, "SqlServer");

        Assert.Contains(definition.Indexes, static x => x is { Name: "UX_ConfigItem_EID", IsUnique: true });
        Assert.Contains(definition.Indexes, static x => x is { Name: "UX_ConfigItem_Code", IsUnique: true });
    }
}
