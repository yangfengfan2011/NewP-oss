using ICP.Platform.Authentication.Contracts;
using ICP.Platform.Modeling;
using ICP.Platform.Modules.Authentication;
using ICP.Platform.Modules.Authentication.Application;
using ICP.Platform.Modules.Authentication.Persistence.Descriptors;
using ICP.Platform.Sdk;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace ICP.Platform.Authentication.Tests;

public sealed class AuthenticationFoundationModelTests
{
    [Fact]
    public void Module_UsesRequiredAuthenticationCapsule()
    {
        var module = new AuthenticationModule();

        Assert.Equal("Authentication", module.ModuleId);
        Assert.Equal("authentication.backend", Assert.IsAssignableFrom<IModuleBehaviorBinding>(module).BehaviorKey);
    }

    [Fact]
    public void Module_RegistersSessionRegistryAndPurgeCapability()
    {
        var services = new ServiceCollection();

        new AuthenticationModule().ConfigureServices(services);

        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IAuthenticationSessionRegistry));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IAuthenticationSessionPurgeService));
    }

    [Fact]
    public void Module_BindsConfigurableFixedLifetime()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{AuthenticationSessionOptions.SectionName}:Lifetime"] = "02:00:00"
            })
            .Build());
        new AuthenticationModule().ConfigureServices(services);

        using var provider = services.BuildServiceProvider();

        Assert.Equal(TimeSpan.FromHours(2), provider.GetRequiredService<IOptions<AuthenticationSessionOptions>>().Value.Lifetime);
    }

    [Fact]
    public void Metadata_DefinesSessionRegistryWithoutRawToken()
    {
        var session = Assert.Single(new AuthenticationModelMetadataProvider().GetDefinitions());

        Assert.Equal("AuthenticationSession", session.Name);
        Assert.Contains(session.Fields, static field => field is { Name: "UserEID", Required: true });
        Assert.Contains(session.Fields, static field => field is { Name: "TokenHash", Required: true });
        Assert.DoesNotContain(session.Fields, static field => field.Name.Contains("RawToken", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(session.Fields, static field => field is { Name: "SessionType", ValueType: FieldValueType.Enum, Required: true });
        Assert.Contains(session.Fields, static field => field is { Name: "CreatedBy", Required: true, IsAuditField: true });
        Assert.Contains(session.Fields, static field => field is { Name: "CreatedAt", Required: true, IsAuditField: true });
        Assert.Contains(session.Fields, static field => field is { Name: "UpdatedBy", Required: true, IsAuditField: true });
        Assert.Contains(session.Fields, static field => field is { Name: "UpdatedAt", Required: true, IsAuditField: true });
        Assert.Contains(session.Indexes, static index => index is { Name: "UX_AuthenticationSession_TokenHash", IsUnique: true });
        Assert.Contains(session.ForeignKeys, static foreignKey =>
            foreignKey is { Name: "FK_AuthenticationSession_User", PrincipalModelKey: "Identity/User" });
    }
}
