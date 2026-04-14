using ICP.Platform.Modeling.Metadata;
using ICP.Platform.Modeling.Validation;
using Xunit;

namespace ICP.Platform.Modeling.Tests;

public class ModelMetadataValidatorTests
{
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
        Assert.Contains("builtinFields", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_ThrowsWhenStorageTypeIsUnknown()
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
                            Type = "unknown"
                        }
                    }
                ]
            }
        };

        var validator = new ModelMetadataValidator();
        var typeMap = new StorageTypeMapDocument
        {
            Types = new Dictionary<string, StorageTypeDefinition>(StringComparer.OrdinalIgnoreCase)
        };

        var ex = Assert.Throws<InvalidOperationException>(() => validator.Validate(document, typeMap));
        Assert.Contains("storage.type", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_ThrowsWhenLinkModelHasNoLinkedTypes()
    {
        var document = new ModelMetadataDocument
        {
            SchemaVersion = "1.0",
            ModuleId = "ConfigCenter",
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
}
