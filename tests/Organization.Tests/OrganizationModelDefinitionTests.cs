using ICP.Platform.Modeling;
using ICP.Platform.Modeling.Definitions;
using ICP.Platform.Modules.Organization.Persistence.Descriptors;
using Xunit;

namespace ICP.Platform.Modules.Organization.Tests;

public class OrganizationModelDefinitionTests
{
    [Fact]
    public void MetadataDefinitions_UseProtocolFieldsForGenericCodeAndName()
    {
        var definitions = new OrganizationModelMetadataProvider().GetDefinitions();

        AssertProtocolFields(definitions, "Company", expectsCode: true);
        AssertProtocolFields(definitions, "Department", expectsCode: true);
        AssertProtocolFields(definitions, "Position", expectsCode: false);
        AssertProtocolFields(definitions, "Person", expectsCode: false);
    }

    [Fact]
    public void Position_IsGlobalShortTextDirectory()
    {
        var definitions = new OrganizationModelMetadataProvider().GetDefinitions();
        var position = Assert.Single(definitions, definition => definition.Name == "Position");

        Assert.Equal(
            ["EID", "CreatedBy", "CreatedAt", "UpdatedBy", "UpdatedAt", "ENAME", "Description", "IsEnabled"],
            position.Fields.Select(field => field.Name));
        Assert.Contains(position.Fields, field => field.Name == "Description" && !field.Required);
        Assert.Contains(position.Indexes, index =>
            index.Name == "UX_Position_ENAME" && index.IsUnique && index.Fields.SequenceEqual(["ENAME"]));
    }

    [Fact]
    public void Company_UsesStringSortOrderAndGlobalCodeAndNameConstraints()
    {
        var definitions = new OrganizationModelMetadataProvider().GetDefinitions();
        var company = Assert.Single(definitions, definition => definition.Name == "Company");

        Assert.Contains(company.Fields, field => field.Name == "SortOrder" && field.ValueType == FieldValueType.Text);
        Assert.Contains(company.Indexes, index =>
            index.Name == "UX_Company_ECODE" && index.IsUnique && index.Fields.SequenceEqual(["ECODE"]));
        Assert.Contains(company.Indexes, index =>
            index.Name == "UX_Company_ENAME" && index.IsUnique && index.Fields.SequenceEqual(["ENAME"]));
    }

    [Fact]
    public void Department_UsesStringSortOrderAndCompanyParentScopedConstraints()
    {
        var definitions = new OrganizationModelMetadataProvider().GetDefinitions();
        var department = Assert.Single(definitions, definition => definition.Name == "Department");

        Assert.Contains(department.Fields, field => field.Name == "SortOrder" && field.ValueType == FieldValueType.Text);
        Assert.Contains(department.Indexes, index =>
            index.Name == "UX_Department_Company_ECODE"
            && index.IsUnique
            && index.Fields.SequenceEqual(["CompanyId", "ECODE"]));
        Assert.Contains(department.Indexes, index =>
            index.Name == "UX_Department_Company_Parent_ENAME"
            && index.IsUnique
            && index.Fields.SequenceEqual(["CompanyId", "ParentDepartmentId", "ENAME"]));
    }

    [Fact]
    public void LegacyFacade_DefinitionsMatchMetadataProvider()
    {
        var expected = new OrganizationModelMetadataProvider().GetDefinitions();
        var actual = OrganizationModelDefinitions.All;

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
            Assert.Equal(expectedIndex.IncludeNullValues, actualIndex.IncludeNullValues);
        }
    }

    private static void AssertProtocolFields(
        IReadOnlyList<ModelDefinition> definitions,
        string modelName,
        bool expectsCode)
    {
        var definition = Assert.Single(definitions, item => item.Name == modelName);

        Assert.Contains(definition.Fields, static field => field.Name == "EID" && field.IsPrimaryKey);
        Assert.DoesNotContain(definition.Fields, static field => field.Name == "Id");
        Assert.Contains(definition.Fields, static field => field.Name == "ENAME");
        Assert.DoesNotContain(definition.Fields, static field => field.Name == "Name");

        if (expectsCode)
        {
            Assert.Contains(definition.Fields, static field => field.Name == "ECODE");
            Assert.DoesNotContain(definition.Fields, static field => field.Name == "Code");
        }
    }
}
