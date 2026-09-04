using ICP.Platform.Modeling;
using ICP.Platform.Modeling.Definitions;
using ICP.Platform.Modeling.Validation;
using Xunit;

namespace ICP.Platform.Modeling.Tests;

public class ModelDefinitionValidatorTests
{
    [Fact]
    public void Validate_ThrowsWhenFieldNamesDuplicate()
    {
        var definition = new ModelDefinition(
            "ConfigItem",
            "config_items",
            ModelKind.Entity,
            new[]
            {
                new FieldDefinition("Id", "id", FieldKind.Scalar, FieldValueType.Text, "string", true, true, false),
                new FieldDefinition("Id", "config_id", FieldKind.Scalar, FieldValueType.Text, "string", true, false, false)
            },
            Array.Empty<IndexDefinition>());

        var validator = new ModelDefinitionValidator();

        var ex = Assert.Throws<InvalidOperationException>(() => validator.Validate(definition));
        Assert.Contains("duplicate field", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_ThrowsWhenIncludeNullValuesHasNoNullableUniqueScope()
    {
        var definition = new ModelDefinition(
            "ConfigItem",
            "config_items",
            ModelKind.Entity,
            [new FieldDefinition("Id", "id", FieldKind.Scalar, FieldValueType.Text, "string", true, true, false)],
            [new IndexDefinition("IX_ConfigItem_Id", ["Id"], IsUnique: false, IncludeNullValues: true)]);

        var error = Assert.Throws<InvalidOperationException>(() => new ModelDefinitionValidator().Validate(definition));

        Assert.Contains("include-null", error.Message, StringComparison.OrdinalIgnoreCase);
    }
}
