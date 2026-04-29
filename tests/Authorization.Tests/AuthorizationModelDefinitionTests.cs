using ICP.Platform.Modeling.Definitions;
using ICP.Platform.Modules.Authorization.Persistence.Descriptors;
using Xunit;

namespace ICP.Platform.Modules.Authorization.Tests;

public class AuthorizationModelDefinitionTests
{
    [Fact]
    public void LegacyFacade_DefinitionsMatchMetadataProvider()
    {
        var expected = new AuthorizationModelMetadataProvider().GetDefinitions();
        var actual = AuthorizationModelDefinitions.All;

        AssertDefinitionsEquivalent(expected, actual);
    }

    private static void AssertDefinitionsEquivalent(
        IReadOnlyList<ModelDefinition> expected,
        IReadOnlyList<ModelDefinition> actual)
    {
        Assert.Equal(expected.Select(static definition => definition.Name), actual.Select(static definition => definition.Name));
        Assert.Equal(expected.Count, actual.Count);

        for (var index = 0; index < expected.Count; index++)
        {
            var expectedDefinition = expected[index];
            var actualDefinition = actual[index];

            Assert.Equal(expectedDefinition.Name, actualDefinition.Name);
            Assert.Equal(expectedDefinition.Kind, actualDefinition.Kind);
            Assert.Equal(expectedDefinition.TableName, actualDefinition.TableName);
            AssertFieldsEquivalent(expectedDefinition.Fields, actualDefinition.Fields);
            AssertIndexesEquivalent(expectedDefinition.Indexes, actualDefinition.Indexes);
        }
    }

    private static void AssertFieldsEquivalent(
        IReadOnlyList<FieldDefinition> expected,
        IReadOnlyList<FieldDefinition> actual)
    {
        Assert.Equal(expected.Count, actual.Count);

        for (var index = 0; index < expected.Count; index++)
        {
            var expectedField = expected[index];
            var actualField = actual[index];

            Assert.Equal(expectedField.Name, actualField.Name);
            Assert.Equal(expectedField.StoreName, actualField.StoreName);
            Assert.Equal(expectedField.Kind, actualField.Kind);
            Assert.Equal(expectedField.ValueType, actualField.ValueType);
            Assert.Equal(expectedField.StoreType, actualField.StoreType);
            Assert.Equal(expectedField.Required, actualField.Required);
            Assert.Equal(expectedField.IsPrimaryKey, actualField.IsPrimaryKey);
            Assert.Equal(expectedField.IsAuditField, actualField.IsAuditField);
        }
    }

    private static void AssertIndexesEquivalent(
        IReadOnlyList<IndexDefinition> expected,
        IReadOnlyList<IndexDefinition> actual)
    {
        Assert.Equal(expected.Count, actual.Count);

        for (var index = 0; index < expected.Count; index++)
        {
            var expectedIndex = expected[index];
            var actualIndex = actual[index];

            Assert.Equal(expectedIndex.Name, actualIndex.Name);
            Assert.Equal(expectedIndex.Fields, actualIndex.Fields);
            Assert.Equal(expectedIndex.IsUnique, actualIndex.IsUnique);
        }
    }
}
