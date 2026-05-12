using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Modules.Organization;
using ICP.Platform.Modules.Organization.Controllers;
using ICP.Platform.Modules.Organization.Crud;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ICP.Platform.Modules.Organization.Tests;

public sealed class PersonStandardCrudResourceTests
{
    [Fact]
    public void OrganizationModule_RegistersPersonStandardCrudDescriptor()
    {
        var services = new ServiceCollection();
        new OrganizationModule().ConfigureServices(services);
        using var provider = services.BuildServiceProvider();

        var registry = provider.GetRequiredService<IStandardCrudResourceRegistry>();
        var descriptor = registry.FindByResourceKey("organization.person");

        Assert.NotNull(descriptor);
        Assert.Equal("api/organization/persons", descriptor!.Route);
        Assert.Equal(StandardCrudDeletePolicy.Conditional, descriptor.DeletePolicy);
    }

    [Fact]
    public void PersonCrudDescriptor_DeclaresRouteSummaryDisplayAndUniqueConstraint()
    {
        var descriptor = PersonCrudDescriptorFactory.Create();

        Assert.Equal("organization.person", descriptor.ResourceKey);
        Assert.Equal("api/organization/persons", descriptor.Route);
        Assert.Contains("CompanyName", descriptor.DisplayValueFields);
        Assert.Contains("DepartmentName", descriptor.DisplayValueFields);
        Assert.Contains("PositionName", descriptor.DisplayValueFields);
        Assert.Contains("userLink", descriptor.SummaryBlockKeys);
        Assert.Contains("authorizationSummary", descriptor.SummaryBlockKeys);
        var constraint = Assert.Single(descriptor.UniqueConstraints);
        Assert.Equal("personNo", constraint.Key);
        Assert.Equal(["PersonNo"], constraint.Fields);
    }

    [Fact]
    public void PersonCrudDescriptor_DeclaresRequiredFieldsWithoutTighteningOptionalOrganizationFields()
    {
        var descriptor = PersonCrudDescriptorFactory.Create();

        var required = descriptor.RequiredFields.ToDictionary(item => item.Field, StringComparer.Ordinal);

        Assert.Equal(StandardCrudRequiredFieldScope.CreateOnly, required["PersonNo"].Scope);
        Assert.Equal("Organization.Person.PersonNoRequired", required["PersonNo"].ErrorCode);
        Assert.Equal(StandardCrudRequiredFieldScope.CreateAndUpdate, required["ENAME"].Scope);
        Assert.Equal("Organization.Person.NameRequired", required["ENAME"].ErrorCode);
        Assert.Equal(StandardCrudRequiredFieldScope.CreateAndUpdate, required["IsEnabled"].Scope);
        Assert.Equal("Organization.Person.EnabledRequired", required["IsEnabled"].ErrorCode);
        Assert.DoesNotContain("CompanyId", required.Keys);
        Assert.DoesNotContain("DepartmentId", required.Keys);
        Assert.DoesNotContain("EmploymentStatus", required.Keys);
    }

    [Fact]
    public void LegacyPersonController_IsRemoved()
    {
        var legacyType = typeof(OrganizationModule).Assembly.GetType(
            "ICP.Platform.Modules.Organization.Controllers.Persons" + "Controller");

        Assert.Null(legacyType);
        Assert.NotNull(typeof(PersonUserLinksController));
    }
}
