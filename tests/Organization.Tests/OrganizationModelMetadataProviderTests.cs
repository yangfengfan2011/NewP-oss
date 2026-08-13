using ICP.Platform.Modeling;
using ICP.Platform.Modeling.Definitions;
using ICP.Platform.Modeling.Validation;
using ICP.Platform.Modules.Organization.Persistence.Descriptors;
using Xunit;

namespace ICP.Platform.Modules.Organization.Tests;

public class OrganizationModelMetadataProviderTests
{
    [Fact]
    public void GetDefinitions_ReturnsAllOrganizationModels_AndValidatesThem()
    {
        var provider = new OrganizationModelMetadataProvider();

        var definitions = provider.GetDefinitions();

        Assert.Equal(
            ["Company", "Department", "Position", "Person"],
            definitions.Select(definition => definition.Name).ToArray());

        var validator = new ModelDefinitionValidator();
        foreach (var definition in definitions)
        {
            validator.Validate(definition);
        }
    }

    [Fact]
    public void PersonAndPosition_EmploymentStatus_AreProjectedAsEnum()
    {
        var provider = new OrganizationModelMetadataProvider();

        var definitions = provider.GetDefinitions();

        var person = Assert.Single(definitions, definition => definition.Name == "Person");
        var position = Assert.Single(definitions, definition => definition.Name == "Position");

        Assert.Contains(person.Fields, static field =>
            field.Name == "EmploymentStatus"
            && field.ValueType == FieldValueType.Enum);

        Assert.Contains(position.Fields, static field =>
            field.Name == "EmploymentStatus"
            && field.ValueType == FieldValueType.Enum);
    }

    [Fact]
    public void Person_OwnsPermanentUserBindingFieldsAndUniqueUserConstraint()
    {
        var provider = new OrganizationModelMetadataProvider();

        var definitions = provider.GetDefinitions();

        var person = Assert.Single(definitions, definition => definition.Name == "Person");

        Assert.Contains(person.Fields, static field => field.Name == "AccountEID" && !field.Required);
        Assert.Contains(person.Fields, static field => field.Name == "AccountBoundAt" && !field.Required);
        Assert.Contains(person.Fields, static field => field.Name == "AccountBoundBy" && !field.Required);
        Assert.Contains(person.Fields, static field => field.Name == "AccountBindingReason" && !field.Required);
        Assert.Contains(person.Indexes, static index => index.Name == "UX_Person_AccountEID" && index.IsUnique);
    }

    [Fact]
    public void Person_OrganizationFields_AreProjectedAsOptional()
    {
        var provider = new OrganizationModelMetadataProvider();

        var definitions = provider.GetDefinitions();
        var person = Assert.Single(definitions, definition => definition.Name == "Person");

        Assert.Contains(person.Fields, static field => field.Name == "CompanyId" && !field.Required);
        Assert.Contains(person.Fields, static field => field.Name == "DepartmentId" && !field.Required);
        Assert.Contains(person.Fields, static field => field.Name == "EmploymentStatus" && !field.Required);
    }
}
