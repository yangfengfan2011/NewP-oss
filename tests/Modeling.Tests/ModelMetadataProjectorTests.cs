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
                            Type = "string",
                            Nullable = true
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
                        Unique = true,
                        IncludeNullValues = true
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
        Assert.Contains(definition.Indexes, static x => x is
            { Name: "UX_ConfigItem_Code", IsUnique: true, IncludeNullValues: true });
    }

    [Fact]
    public void Project_TreatsModelDataSourceFieldsAsReferencesWithoutChangingStorageType()
    {
        var document = new ModelMetadataDocument
        {
            SchemaVersion = "1.0",
            ModuleId = "Organization",
            Model = new ModelMetadataModel
            {
                Name = "Person",
                Kind = "Entity",
                Fields =
                [
                    new ModelMetadataField
                    {
                        Name = "CompanyId",
                        Storage = new ModelMetadataStorageSection
                        {
                            Type = "guid",
                            Nullable = true
                        },
                        DataSource = new ModelMetadataDataSource
                        {
                            Type = "model",
                            Key = "Company",
                            Model = "Company"
                        }
                    }
                ]
            }
        };

        var definition = new ModelMetadataProjector().Project(document);

        Assert.Contains(definition.Fields, static field =>
            field is { Name: "CompanyId", Kind: FieldKind.Reference, ValueType: FieldValueType.ObjectId, StoreType: "guid" });
    }

    [Fact]
    public void Project_CarriesReferenceDisplayRuleIntoFieldDefinition()
    {
        var document = new ModelMetadataDocument
        {
            SchemaVersion = "1.0",
            ModuleId = "Organization",
            Model = new ModelMetadataModel
            {
                Name = "Person",
                Kind = "Entity",
                Fields =
                [
                    new ModelMetadataField
                    {
                        Name = "CompanyId",
                        UiType = "lookup",
                        Storage = new ModelMetadataStorageSection
                        {
                            Type = "guid",
                            Nullable = true
                        },
                        DataSource = new ModelMetadataDataSource
                        {
                            Type = "provider",
                            Key = "organization.companies"
                        },
                        Display = new ModelMetadataDisplayRule
                        {
                            Type = "reference",
                            TargetModel = "Company",
                            TargetIdField = "EID",
                            TargetDisplayField = "ENAME"
                        }
                    }
                ]
            }
        };

        var definition = new ModelMetadataProjector().Project(document);

        var display = Assert.Single(definition.Fields, field => field.Name == "CompanyId").DisplayRule;
        Assert.NotNull(display);
        Assert.Equal("reference", display!.Type);
        Assert.Equal("Company", display.TargetModel);
        Assert.Equal("EID", display.TargetIdField);
        Assert.Equal("ENAME", display.TargetDisplayField);
    }

    [Fact]
    public void Project_CarriesKernelFieldDisplayRuleIntoFieldDefinition()
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
                        Name = "CreatedBy",
                        Contract = new ModelMetadataContractSection { ValueType = "user" },
                        Display = new ModelMetadataDisplayRule
                        {
                            Type = "user",
                            TargetModel = "IdentityAccount",
                            TargetIdField = "EID",
                            TargetDisplayField = "ENAME"
                        }
                    }
                ]
            }
        };

        var definition = new ModelMetadataProjector().Project(document);

        var display = Assert.Single(definition.Fields, field => field.Name == "CreatedBy").DisplayRule;
        Assert.NotNull(display);
        Assert.Equal("user", display!.Type);
        Assert.Equal("IdentityAccount", display.TargetModel);
        Assert.Equal("EID", display.TargetIdField);
        Assert.Equal("ENAME", display.TargetDisplayField);
    }

    [Fact]
    public void Project_TreatsProviderLookupDataSourceFieldsAsReferencesWithoutChangingStorageType()
    {
        var document = new ModelMetadataDocument
        {
            SchemaVersion = "1.0",
            ModuleId = "Organization",
            Model = new ModelMetadataModel
            {
                Name = "Person",
                Kind = "Entity",
                Fields =
                [
                    new ModelMetadataField
                    {
                        Name = "CompanyId",
                        Storage = new ModelMetadataStorageSection
                        {
                            Type = "guid",
                            Nullable = true
                        },
                        UiType = "lookup",
                        DataSource = new ModelMetadataDataSource
                        {
                            Type = "provider",
                            Key = "organization.companies"
                        }
                    }
                ]
            }
        };

        var definition = new ModelMetadataProjector().Project(document);

        Assert.Contains(definition.Fields, static field =>
            field is { Name: "CompanyId", Kind: FieldKind.Reference, ValueType: FieldValueType.ObjectId, StoreType: "guid" });
    }

    [Fact]
    public void Project_AddsEntityFixedFieldsAndSkipsBuiltinFieldAuthoringEntry()
    {
        var document = new ModelMetadataDocument
        {
            SchemaVersion = "1.0",
            ModuleId = "ConfigCenter",
            Model = new ModelMetadataModel
            {
                Name = "ConfigItem",
                Kind = "Entity",
                BuiltinFields = ["ENAME", "ECODE"],
                Fields =
                [
                    new ModelMetadataField
                    {
                        Name = "ENAME",
                        UiType = "text"
                    },
                    new ModelMetadataField
                    {
                        Name = "IsEnabled",
                        Storage = new ModelMetadataStorageSection
                        {
                            Type = "bool"
                        }
                    },
                    new ModelMetadataField
                    {
                        Name = "Payload",
                        Storage = new ModelMetadataStorageSection
                        {
                            Type = "json",
                            Nullable = true
                        }
                    },
                    new ModelMetadataField
                    {
                        Name = "PublishedAt",
                        Storage = new ModelMetadataStorageSection
                        {
                            Type = "datetime",
                            Nullable = true
                        }
                    }
                ]
            }
        };

        var definition = new ModelMetadataProjector().Project(document);

        Assert.Contains(definition.Fields, static x => x is { Name: "EID", IsPrimaryKey: true, ValueType: FieldValueType.ObjectId });
        Assert.Contains(definition.Fields, static x => x is { Name: "CreatedBy", Kind: FieldKind.Audit, ValueType: FieldValueType.User, Required: true, IsAuditField: true });
        Assert.Contains(definition.Fields, static x => x is { Name: "CreatedAt", Kind: FieldKind.Audit, ValueType: FieldValueType.DateTime, Required: true, IsAuditField: true });
        Assert.Contains(definition.Fields, static x => x is { Name: "UpdatedBy", Kind: FieldKind.Audit, ValueType: FieldValueType.User, Required: true, IsAuditField: true });
        Assert.Contains(definition.Fields, static x => x is { Name: "UpdatedAt", Kind: FieldKind.Audit, ValueType: FieldValueType.DateTime, Required: true, IsAuditField: true });
        Assert.Contains(definition.Fields, static x => x is { Name: "ENAME", ValueType: FieldValueType.Text });
        Assert.Contains(definition.Fields, static x => x is { Name: "ECODE", ValueType: FieldValueType.Text });
        Assert.Equal(1, definition.Fields.Count(static x => x.Name == "ENAME"));
        Assert.Contains(definition.Fields, static x => x is { Name: "IsEnabled", ValueType: FieldValueType.Boolean });
        Assert.Contains(definition.Fields, static x => x is { Name: "Payload", ValueType: FieldValueType.Description });
        Assert.Contains(definition.Fields, static x => x is { Name: "PublishedAt", ValueType: FieldValueType.DateTime });
    }

    [Fact]
    public void Project_FieldContractValueTypeUser_ProjectsUserValueType()
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
                        Name = "ReviewerUserEID",
                        Storage = new ModelMetadataStorageSection
                        {
                            Type = "string",
                            Nullable = true
                        },
                        Contract = new ModelMetadataContractSection
                        {
                            ValueType = "user"
                        }
                    }
                ]
            }
        };

        var definition = new ModelMetadataProjector().Project(document);

        Assert.Contains(definition.Fields, static field =>
            field is { Name: "ReviewerUserEID", ValueType: FieldValueType.User });
    }

    [Fact]
    public void Project_AddsLinkFixedFieldsAndOptionalBuiltinFields()
    {
        var document = new ModelMetadataDocument
        {
            SchemaVersion = "1.0",
            ModuleId = "Organization",
            Model = new ModelMetadataModel
            {
                Name = "Organize",
                Kind = "Link",
                BuiltinFields = ["LSequence"],
                LinkedTypes =
                [
                    new ModelMetadataLinkedType
                    {
                        Parent = "Organization",
                        Child = "Company"
                    }
                ],
                Fields =
                [
                    new ModelMetadataField
                    {
                        Name = "LSequence",
                        UiType = "text"
                    },
                    new ModelMetadataField
                    {
                        Name = "LinkToken",
                        Storage = new ModelMetadataStorageSection
                        {
                            Type = "guid"
                        }
                    }
                ]
            }
        };

        var definition = new ModelMetadataProjector().Project(document);

        Assert.Contains(definition.Fields, static x => x is { Name: "LID", IsPrimaryKey: true, ValueType: FieldValueType.ObjectId });
        Assert.Contains(definition.Fields, static x => x is { Name: "PEID", Kind: FieldKind.Reference, ValueType: FieldValueType.ObjectId });
        Assert.Contains(definition.Fields, static x => x is { Name: "CEID", Kind: FieldKind.Reference, ValueType: FieldValueType.ObjectId });
        Assert.Contains(definition.Fields, static x => x is { Name: "LCreatedBy", Kind: FieldKind.Audit, ValueType: FieldValueType.User, IsAuditField: true });
        Assert.Contains(definition.Fields, static x => x is { Name: "LCreatedAt", Kind: FieldKind.Audit, ValueType: FieldValueType.DateTime, IsAuditField: true });
        Assert.Contains(definition.Fields, static x => x is { Name: "LUpdatedBy", Kind: FieldKind.Audit, ValueType: FieldValueType.User, IsAuditField: true });
        Assert.Contains(definition.Fields, static x => x is { Name: "LUpdatedAt", Kind: FieldKind.Audit, ValueType: FieldValueType.DateTime, IsAuditField: true });
        Assert.Contains(definition.Fields, static x => x.Name == "LSequence");
        Assert.Equal(1, definition.Fields.Count(static x => x.Name == "LSequence"));
        Assert.Contains(definition.Fields, static x => x is { Name: "LinkToken", ValueType: FieldValueType.ObjectId });
    }
}
