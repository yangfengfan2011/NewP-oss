using ICP.Platform.Modeling.Metadata;
using ICP.Platform.Modeling.Validation;
using Xunit;

namespace ICP.Platform.Modeling.Tests;

public class ModelMetadataValidatorTests
{
    [Fact]
    public void Validate_RejectsEditableServerOwnedAuditFieldWithExactLocation()
    {
        var document = DocumentWithFields(new ModelMetadataField
        {
            Name = "UpdatedAt",
            Form = new ModelMetadataFormSection
            {
                Edit = new ModelMetadataFormModeRule { Mode = "editable" }
            }
        });

        var error = Assert.Throws<InvalidOperationException>(() => new ModelMetadataValidator().Validate(document));

        Assert.Contains("Organization", error.Message, StringComparison.Ordinal);
        Assert.Contains("Person", error.Message, StringComparison.Ordinal);
        Assert.Contains("UpdatedAt", error.Message, StringComparison.Ordinal);
        Assert.Contains("edit", error.Message, StringComparison.Ordinal);
        Assert.Contains("editable", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("EID")]
    [InlineData("LID")]
    public void Validate_AllowsEditableExplicitPrimaryKey(string fieldName)
    {
        var kind = fieldName == "EID" ? "Entity" : "Link";
        var document = new ModelMetadataDocument
        {
            SchemaVersion = "1.0",
            ModuleId = "Example",
            Model = new ModelMetadataModel
            {
                Name = "ExplicitKey",
                Kind = kind,
                LinkedTypes = kind == "Link"
                    ? [new ModelMetadataLinkedType { Parent = "Parent", Child = "Child" }]
                    : [],
                Fields =
                [
                    new ModelMetadataField
                    {
                        Name = fieldName,
                        Form = new ModelMetadataFormSection
                        {
                            Create = new ModelMetadataFormModeRule { Mode = "editable" },
                            Edit = new ModelMetadataFormModeRule { Mode = "editable" }
                        }
                    }
                ]
            }
        };

        new ModelMetadataValidator().Validate(document);
    }

    [Fact]
    public void Validate_ThrowsWhenBuiltinFieldDoesNotMatchKind()
    {
        var document = new ModelMetadataDocument
        {
            SchemaVersion = "1.0",
            ModuleId = "ConfigCenter",
            Model = new ModelMetadataModel
            {
                Name = "ConfigItem",
                Kind = "Entity",
                BuiltinFields = ["LSequence"]
            }
        };

        var validator = new ModelMetadataValidator();

        var ex = Assert.Throws<InvalidOperationException>(() => validator.Validate(document));

        Assert.Contains("builtin", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_ThrowsWhenStorageTypeIsNotDefined()
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
                        Name = "Value",
                        Storage = new ModelMetadataStorageSection
                        {
                            Type = "unknown-type"
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
            }
        };

        var validator = new ModelMetadataValidator();

        var ex = Assert.Throws<InvalidOperationException>(() => validator.Validate(document, typeMap));

        Assert.Contains("storage.type", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_ThrowsWhenLinkDoesNotDefineLinkedTypes()
    {
        var document = new ModelMetadataDocument
        {
            SchemaVersion = "1.0",
            ModuleId = "Organization",
            Model = new ModelMetadataModel
            {
                Name = "Organize",
                Kind = "Link"
            }
        };

        var validator = new ModelMetadataValidator();

        var ex = Assert.Throws<InvalidOperationException>(() => validator.Validate(document));

        Assert.Contains("linkedTypes", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_ThrowsWhenFieldNamesDuplicate()
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
                        Name = "Value",
                        Storage = new ModelMetadataStorageSection
                        {
                            Type = "text"
                        }
                    },
                    new ModelMetadataField
                    {
                        Name = "value",
                        Storage = new ModelMetadataStorageSection
                        {
                            Type = "string"
                        }
                    }
                ]
            }
        };

        var validator = new ModelMetadataValidator();

        var ex = Assert.Throws<InvalidOperationException>(() => validator.Validate(document));

        Assert.Contains("duplicate", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_ThrowsWhenIndexReferencesUnknownField()
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
                        Name = "Value",
                        Storage = new ModelMetadataStorageSection
                        {
                            Type = "text"
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

        var validator = new ModelMetadataValidator();

        var ex = Assert.Throws<InvalidOperationException>(() => validator.Validate(document));

        Assert.Contains("index", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_ThrowsWhenBuiltinFieldDefinesStorage()
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
                        Storage = new ModelMetadataStorageSection
                        {
                            Type = "string"
                        }
                    }
                ]
            }
        };

        var validator = new ModelMetadataValidator();

        var ex = Assert.Throws<InvalidOperationException>(() => validator.Validate(document));

        Assert.Contains("storage", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_RejectsIncludeNullValuesOnNonUniqueIndex()
    {
        var document = DocumentWithFields(new ModelMetadataField
        {
            Name = "ParentId",
            Storage = new ModelMetadataStorageSection { Type = "guid", Nullable = true }
        });
        document.Model.Indexes.Add(new ModelMetadataIndex
        {
            Name = "IX_Person_ParentId",
            Fields = ["ParentId"],
            IncludeNullValues = true
        });

        var error = Assert.Throws<InvalidOperationException>(() => new ModelMetadataValidator().Validate(document));

        Assert.Contains("includeNullValues", error.Message, StringComparison.Ordinal);
        Assert.Contains("unique", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_RejectsDisplayPlanningFailure()
    {
        var field = ReferenceFieldWithDisplay("computed");
        var document = DocumentWithFields(field);

        var ex = Assert.Throws<InvalidOperationException>(() => new ModelMetadataValidator().Validate(document));

        Assert.Contains("CompanyId", ex.Message, StringComparison.Ordinal);
        Assert.Contains("display.type", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("calendarDate", "datetime")]
    [InlineData("instant", "date")]
    public void Validate_RejectsTemporalContractStorageMismatch(string valueType, string storageType)
    {
        var document = DocumentWithFields(new ModelMetadataField
        {
            Name = "TemporalValue",
            Storage = new ModelMetadataStorageSection { Type = storageType, Nullable = true },
            Contract = new ModelMetadataContractSection { ValueType = valueType }
        });

        var error = Assert.Throws<InvalidOperationException>(() => new ModelMetadataValidator().Validate(document));

        Assert.Contains("TemporalValue", error.Message, StringComparison.Ordinal);
        Assert.Contains("storage.type", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static ModelMetadataDocument DocumentWithFields(params ModelMetadataField[] fields) => new()
    {
        SchemaVersion = "1.0",
        ModuleId = "Organization",
        Model = new ModelMetadataModel
        {
            Name = "Person",
            Kind = "Entity",
            Fields = [.. fields]
        }
    };

    private static ModelMetadataField ReferenceFieldWithDisplay(
        string displayType,
        string targetModel = "Company",
        string targetIdField = "EID",
        string targetDisplayField = "ENAME") => new()
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
        Display = ReferenceDisplayRule(displayType, targetModel, targetIdField, targetDisplayField)
    };

    private static ModelMetadataDisplayRule ReferenceDisplayRule(
        string type = "reference",
        string targetModel = "Company",
        string targetIdField = "EID",
        string targetDisplayField = "ENAME") => new()
    {
        Type = type,
        TargetModel = targetModel,
        TargetIdField = targetIdField,
        TargetDisplayField = targetDisplayField
    };
}
