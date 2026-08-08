using ICP.Platform.HostApi.Acceptance;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed class PersonnelAcceptanceFixtureTests
{
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
