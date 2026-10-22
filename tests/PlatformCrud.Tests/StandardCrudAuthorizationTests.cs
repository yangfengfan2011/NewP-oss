using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Runtime;
using Xunit;

namespace ICP.Platform.Crud.Tests;

public sealed class StandardCrudAuthorizationTests
{
    [Theory]
    [InlineData(StandardCrudOperation.List, "organization.person.read")]
    [InlineData(StandardCrudOperation.Detail, "organization.person.read")]
    [InlineData(StandardCrudOperation.Options, "organization.person.lookup")]
    [InlineData(StandardCrudOperation.Export, "organization.person.export")]
    [InlineData(StandardCrudOperation.Create, "organization.person.create")]
    [InlineData(StandardCrudOperation.Update, "organization.person.update")]
    [InlineData(StandardCrudOperation.Disable, "organization.person.disable")]
    [InlineData(StandardCrudOperation.Enable, "organization.person.enable")]
    [InlineData(StandardCrudOperation.Delete, "organization.person.delete")]
    public void ByFeature_MapsOperationToFixedPermission(
        StandardCrudOperation operation,
        string expectedPermission)
    {
        var authorization = StandardCrudAuthorization.ByFeature("organization.person", Enum.GetValues<StandardCrudOperation>());

        var permission = authorization.GetFeatureBehavior(operation);

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
                StandardCrudAuthorizationFailureCodes.FeatureBehaviorStemMissing,
                StandardCrudAuthorizationFailureCodes.OperationUnmapped
            ],
            exception.Diagnostics.Select(static diagnostic => diagnostic.Code));
    }

    [Fact]
    public void Validate_WhenResourceRepeatsSameStem_ReportsDuplicateDeclaration()
    {
        var descriptors = new[]
        {
            Descriptor("organization.person", StandardCrudAuthorization.ByFeature("Organization.Person")),
            Descriptor("organization.person", StandardCrudAuthorization.ByFeature("Organization.Person"))
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
            Descriptor("organization.person", StandardCrudAuthorization.ByFeature("Organization.Person")),
            Descriptor("organization.person", StandardCrudAuthorization.ByFeature("Other.Person"))
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
