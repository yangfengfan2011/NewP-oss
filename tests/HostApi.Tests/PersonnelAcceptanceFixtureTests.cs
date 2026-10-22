using ICP.Platform.HostApi.Acceptance;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed class PersonnelAcceptanceFixtureTests
{
    [Fact]
    public void FixtureReadsOrganizationPrerequisitesThroughNarrowDirectories()
    {
        var source = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "apps",
            "host-api",
            "Acceptance",
            "PersonnelAcceptanceFixture.cs"));

        Assert.Contains("ICompanyDirectory companyDirectory", source, StringComparison.Ordinal);
        Assert.Contains("IDepartmentDirectory departmentDirectory", source, StringComparison.Ordinal);
        Assert.DoesNotContain("companyService.GetAllAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("departmentService.GetByCompanyIdAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("IDepartmentService", source, StringComparison.Ordinal);
        Assert.Contains("FindByResourceKey(\"organization.department\")", source, StringComparison.Ordinal);
    }

    private static string GetRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "ICP-Platform.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("无法定位仓库根目录。");
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public async Task Fixture_IsNotDiscoverableOutsideAcceptanceEnvironment(string environmentName)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = environmentName
        });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Acceptance:Enabled"] = "true",
            ["Acceptance:RunId"] = "0123456789abcdef01234567",
            ["Acceptance:Secret"] = new string('a', 64)
        });

        await using var app = builder.Build();
        app.MapPersonnelAcceptanceFixture(builder.Configuration);

        Assert.Empty(((IEndpointRouteBuilder)app).DataSources);
    }

    [Fact]
    public async Task Fixture_IsDiscoverableOnlyWhenAcceptanceEnvironmentAndFlagAreExplicit()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Acceptance"
        });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Acceptance:Enabled"] = "true",
            ["Acceptance:RunId"] = "0123456789abcdef01234567",
            ["Acceptance:Secret"] = new string('a', 64)
        });

        await using var app = builder.Build();
        app.MapPersonnelAcceptanceFixture(builder.Configuration);

        Assert.NotEmpty(((IEndpointRouteBuilder)app).DataSources);
    }
}
