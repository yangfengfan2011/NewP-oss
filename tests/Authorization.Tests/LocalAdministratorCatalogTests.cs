using ICP.Platform.Crud.ObjectIds;
using ICP.Platform.Modules.Authorization.Repositories;
using ICP.Platform.Modules.Authorization.Services;
using Xunit;
namespace ICP.Platform.Modules.Authorization.Tests;
public sealed class LocalAdministratorCatalogTests
{
    [Theory]
    [InlineData("LocalAdmin", true)]
    [InlineData("localadmin", true)]
    [InlineData("", false)]
    [InlineData(" ", false)]
    [InlineData(" LocalAdmin", false)]
    [InlineData("LocalAdmin ", false)]
    [InlineData(null, false)]
    public void MatchesRoleCode_PreservesOrdinalIgnoreCaseWithoutTrimming(string? code, bool expected)
    {
        Assert.Equal(expected, LocalAdministratorCatalog.MatchesRoleCode(code));
    }

    [Fact]
    public async Task StartupCatalog_PreservesProtectedRoleIdentityAcrossRestarts()
    {
        var roles = new RoleRepository();
        var service = new LocalAdministratorCatalog(roles, new PlatformObjectIdGenerator());
        await service.EnsureAsync();
        var original = Assert.Single(await roles.GetAllAsync());
        await service.EnsureAsync();
        var current = Assert.Single(await roles.GetAllAsync());
        Assert.Equal(original.Id, current.Id);
        Assert.Equal("LocalAdmin", current.Code);
        Assert.True(current.IsBuiltIn);
    }
}
