using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Runtime;
using Xunit;

namespace ICP.Platform.Crud.Tests;

public sealed class StandardCrudAuthorizationTests
{
    [Theory]
    [InlineData(StandardCrudOperation.List, "Organization.Person.View")]
    [InlineData(StandardCrudOperation.Detail, "Organization.Person.View")]
    [InlineData(StandardCrudOperation.Options, "Organization.Person.View")]
    [InlineData(StandardCrudOperation.Export, "Organization.Person.View")]
    [InlineData(StandardCrudOperation.Create, "Organization.Person.Create")]
    [InlineData(StandardCrudOperation.Update, "Organization.Person.Update")]
    [InlineData(StandardCrudOperation.Disable, "Organization.Person.Update")]
    [InlineData(StandardCrudOperation.Enable, "Organization.Person.Update")]
    [InlineData(StandardCrudOperation.Delete, "Organization.Person.Delete")]
    public void ByConvention_MapsOperationToFixedPermission(
        StandardCrudOperation operation,
        string expectedPermission)
    {
        var authorization = StandardCrudAuthorization.ByConvention("Organization.Person");

        var permission = authorization.GetPermissionCode(operation);

        Assert.Equal(expectedPermission, permission);
    }

    [Fact]
    public void Validate_AggregatesMissingStemAndUnknownOperationInStableOrder()
    {
        var descriptor = Descriptor("organization.person", authorization: null);

        var exception = Assert.Throws<StandardCrudAuthorizationValidationException>(() =>
            StandardCrudAuthorizationValidator.Validate(
                [descriptor],
                [StandardCrudOperation.List, (StandardCrudOperation)999]));

        Assert.Equal(
            [
                StandardCrudAuthorizationFailureCodes.OperationUnmapped,
                StandardCrudAuthorizationFailureCodes.PermissionStemMissing
            ],
            exception.Diagnostics.Select(static diagnostic => diagnostic.Code));
    }

    [Fact]
    public void Validate_WhenResourceRepeatsSameStem_ReportsDuplicateDeclaration()
    {
        var descriptors = new[]
        {
            Descriptor("organization.person", StandardCrudAuthorization.ByConvention("Organization.Person")),
            Descriptor("organization.person", StandardCrudAuthorization.ByConvention("Organization.Person"))
        };

        var exception = Assert.Throws<StandardCrudAuthorizationValidationException>(() =>
            StandardCrudAuthorizationValidator.Validate(descriptors, [StandardCrudOperation.List]));

        var diagnostic = Assert.Single(exception.Diagnostics);
        Assert.Equal(StandardCrudAuthorizationFailureCodes.DeclarationDuplicate, diagnostic.Code);
    }

    [Fact]
    public void Validate_WhenResourceRepeatsDifferentStems_ReportsConflictingDeclaration()
    {
        var descriptors = new[]
        {
            Descriptor("organization.person", StandardCrudAuthorization.ByConvention("Organization.Person")),
            Descriptor("organization.person", StandardCrudAuthorization.ByConvention("Other.Person"))
        };

        var exception = Assert.Throws<StandardCrudAuthorizationValidationException>(() =>
            StandardCrudAuthorizationValidator.Validate(descriptors, [StandardCrudOperation.List]));

        var diagnostic = Assert.Single(exception.Diagnostics);
        Assert.Equal(StandardCrudAuthorizationFailureCodes.DeclarationConflict, diagnostic.Code);
    }

    private static StandardCrudDescriptor Descriptor(
        string resourceKey,
        StandardCrudAuthorization? authorization) => new()
    {
        ResourceKey = resourceKey,
        ModuleId = "Organization",
        ModelName = "Person",
        Route = "api/organization/persons",
        IdField = "EID",
        DisplayField = "ENAME",
        FieldSet = new StandardCrudFieldSet(
            ListFields: ["EID", "ENAME"],
            DetailFields: ["EID", "ENAME"],
            CreateFields: ["ENAME"],
            UpdateFields: ["ENAME"]),
        Authorization = authorization
    };
}
