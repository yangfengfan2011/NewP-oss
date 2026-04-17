using ICP.Platform.Modeling.Validation;
using ICP.Platform.Modules.Organization.Persistence.Descriptors;
using Xunit;

namespace ICP.Platform.Modules.Organization.Tests;

public class OrganizationModelDefinitionTests
{
    [Fact]
    public void Definitions_ContainExpectedOrganizationModels()
    {
        var definitions = OrganizationModelDefinitions.All;
        var validator = new ModelDefinitionValidator();

        Assert.Contains(definitions, static definition => definition.Name == "Company");
        Assert.Contains(definitions, static definition => definition.Name == "Department");
        Assert.Contains(definitions, static definition => definition.Name == "Position");
        Assert.Contains(definitions, static definition => definition.Name == "Person");
        Assert.Contains(definitions, static definition => definition.Name == "PersonUserLink");

        foreach (var definition in definitions)
        {
            validator.Validate(definition);
        }
    }

    [Fact]
    public void Person_DefinitionUsesPascalCaseStoreNames()
    {
        var person = Assert.Single(OrganizationModelDefinitions.All, item => item.Name == "Person");

        Assert.Equal("Person", person.TableName);
        Assert.Contains(person.Fields, static field => field.Name == "Id" && field.StoreName == "Id");
        Assert.Contains(person.Fields, static field => field.Name == "CompanyId" && field.StoreName == "CompanyId");
        Assert.Contains(person.Fields, static field => field.Name == "EmploymentStatus" && field.StoreName == "EmploymentStatus");
    }
}
