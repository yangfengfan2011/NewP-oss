using ICP.Platform.Modules.Organization;
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
}
