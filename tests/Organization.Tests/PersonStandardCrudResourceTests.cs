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
    public void LegacyPersonController_IsRemoved()
    {
        var legacyType = typeof(OrganizationModule).Assembly.GetType(
            "ICP.Platform.Modules.Organization.Controllers.Persons" + "Controller");

        Assert.Null(legacyType);
        Assert.NotNull(typeof(PersonUserLinksController));
    }
}
