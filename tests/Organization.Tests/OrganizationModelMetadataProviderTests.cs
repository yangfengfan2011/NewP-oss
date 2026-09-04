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
    public void Person_EmploymentStatus_IsProjectedAsEnum()
    {
        var provider = new OrganizationModelMetadataProvider();

        var definitions = provider.GetDefinitions();

        var person = Assert.Single(definitions, definition => definition.Name == "Person");
        Assert.Contains(person.Fields, static field =>
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

    [Fact]
    public void Person_CompanyReferenceDisplayRule_IsProjectedFromMetadata()
    {
        var definitions = new OrganizationModelMetadataProvider().GetDefinitions();
        var person = Assert.Single(definitions, definition => definition.Name == "Person");

        var display = Assert.Single(person.Fields, field => field.Name == "CompanyId").DisplayRule;

        Assert.NotNull(display);
        Assert.Equal("reference", display!.Type);
        Assert.Equal("Company", display.TargetModel);
        Assert.Equal("EID", display.TargetIdField);
        Assert.Equal("ENAME", display.TargetDisplayField);
    }

    [Fact]
    public void Person_PositionReference_UsesNullableRestrictForeignKey()
    {
        var definitions = new OrganizationModelMetadataProvider().GetDefinitions();
        var person = Assert.Single(definitions, definition => definition.Name == "Person");

        var positionId = Assert.Single(person.Fields, field => field.Name == "PositionId");
        var foreignKey = Assert.Single(person.ForeignKeys, item => item.Name == "FK_Person_Position");

        Assert.False(positionId.Required);
        Assert.Equal(["PositionId"], foreignKey.Fields);
        Assert.Equal("Organization/Position", foreignKey.PrincipalModelKey);
        Assert.Equal(["EID"], foreignKey.PrincipalFields);
        Assert.Equal(ForeignKeyDeleteBehavior.Restrict, foreignKey.DeleteBehavior);
    }

    [Fact]
    public void CompanyReferences_UseRestrictForeignKeys()
    {
        var definitions = new OrganizationModelMetadataProvider().GetDefinitions();
        var department = Assert.Single(definitions, definition => definition.Name == "Department");
        var person = Assert.Single(definitions, definition => definition.Name == "Person");

        var departmentCompany = Assert.Single(department.ForeignKeys, item => item.Name == "FK_Department_Company");
        Assert.Equal(["CompanyId"], departmentCompany.Fields);
        Assert.Equal("Organization/Company", departmentCompany.PrincipalModelKey);
        Assert.Equal(ForeignKeyDeleteBehavior.Restrict, departmentCompany.DeleteBehavior);

        var personCompany = Assert.Single(person.ForeignKeys, item => item.Name == "FK_Person_Company");
        Assert.Equal(["CompanyId"], personCompany.Fields);
        Assert.Equal("Organization/Company", personCompany.PrincipalModelKey);
        Assert.Equal(ForeignKeyDeleteBehavior.Restrict, personCompany.DeleteBehavior);

        var personDepartment = Assert.Single(
            person.ForeignKeys,
            item => item.Name == "FK_Person_Company_Department");
        Assert.Equal(["CompanyId", "DepartmentId"], personDepartment.Fields);
        Assert.Equal("Organization/Department", personDepartment.PrincipalModelKey);
        Assert.Equal(["CompanyId", "EID"], personDepartment.PrincipalFields);
        Assert.Equal(ForeignKeyDeleteBehavior.Restrict, personDepartment.DeleteBehavior);

        Assert.Contains(
            department.Indexes,
            index => index.Name == "UX_Department_Company_ECODE"
                && index.IsUnique
                && index.Fields.SequenceEqual(["CompanyId", "ECODE"]));
        Assert.Contains(
            department.Indexes,
            index => index.Name == "UX_Department_Company_ENAME"
                && index.IsUnique
                && index.Fields.SequenceEqual(["CompanyId", "ENAME"]));
    }

    [Fact]
    public void Position_FormAllowsStandardCreateAndEditLifecycle()
    {
        var document = new OrganizationModelMetadataProvider().GetDocument("Position");
        var fields = document.Model.Fields.ToDictionary(static field => field.Name, StringComparer.Ordinal);

        Assert.Equal("editable", fields["ENAME"].Form!.Create!.Mode);
        Assert.True(fields["ENAME"].Form!.Create!.Required);
        Assert.Equal("editable", fields["ENAME"].Form!.Edit!.Mode);
        Assert.True(fields["ENAME"].Form!.Edit!.Required);
        Assert.Equal("editable", fields["Description"].Form!.Create!.Mode);
        Assert.Equal("editable", fields["Description"].Form!.Edit!.Mode);
        Assert.Equal("hidden", fields["IsEnabled"].Form!.Create!.Mode);
        Assert.Equal("editable", fields["IsEnabled"].Form!.Edit!.Mode);
    }
}
