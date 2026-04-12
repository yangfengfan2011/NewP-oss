using ICP.Platform.Modules.Organization;
using ICP.Platform.Modules.Organization.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ICP.Platform.Modules.Organization.Tests;

public class OrganizationModuleTests
{
    [Fact]
    public void ModuleId_ReturnsOrganization()
    {
        var module = new OrganizationModule();

        Assert.Equal("Organization", module.ModuleId);
    }

    [Fact]
    public void GetPermissions_DeclaresPersonViewPermission()
    {
        var module = new OrganizationModule();

        Assert.Contains(module.GetPermissions(), permission => permission.Code == "Organization.Person.View");
    }

    [Fact]
    public void ConfigureServices_RegistersAuthorizationSummaryReader()
    {
        var services = new ServiceCollection();
        var module = new OrganizationModule();

        module.ConfigureServices(services);

        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IAuthorizationSummaryReader) &&
            descriptor.ImplementationType == typeof(NullAuthorizationSummaryReader));
    }
}
