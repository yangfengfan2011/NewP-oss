using ICP.Platform.Modeling;
using ICP.Platform.Modeling.Definitions;
using ICP.Platform.Modules.Identity;
using ICP.Platform.Modules.Identity.Persistence.Descriptors;
using ICP.Platform.Sdk;
using ICP.Platform.Identity.Contracts;
using ICP.Platform.Modules.Identity.Application;
using ICP.Platform.Modules.Identity.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using Xunit;

namespace ICP.Platform.Identity.Tests;

public sealed class IdentityFoundationModelTests
{
    [Fact]
    public void Module_UsesRequiredIdentityCapsule()
    {
        var module = new IdentityModule();

        Assert.Equal("Identity", module.ModuleId);
        Assert.Equal("identity.backend", Assert.IsAssignableFrom<IModuleBehaviorBinding>(module).BehaviorKey);
    }

    [Fact]
    public void Module_RegistersIdentityApplicationKernelAndStableContracts()
    {
        var services = new ServiceCollection();

        new IdentityModule().ConfigureServices(services);

        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IIdentityRepository));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IIdentityAccountDirectory));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IIdentityLifecycleParticipant));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IIdentityAccountApplicationService));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IValidateOptions<IdentityCredentialOptions>));
    }

    [Fact]
    public void Module_BindsExplicitDevelopmentSharedCredentialConfiguration()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Identity:Credentials:UseSharedDevelopmentCredential"] = "true"
            })
            .Build());
        services.AddSingleton<IWebHostEnvironment>(new TestWebHostEnvironment("Development"));
        new IdentityModule().ConfigureServices(services);

        using var provider = services.BuildServiceProvider();
        Assert.Equal("123456", provider.GetRequiredService<ITemporaryCredentialIssuer>().Issue());
    }

    [Fact]
    public void Metadata_DefinesIdentityAggregateWithNonNullAuditAndRelationships()
    {
        var definitions = new IdentityModelMetadataProvider().GetDefinitions();

        Assert.Equal(["IdentityAccount", "LocalCredential", "IdentitySecurityEvent"], definitions.Select(static model => model.Name));
        var account = Assert.Single(definitions, static model => model.Name == "IdentityAccount");
        AssertAuditFields(account);
        Assert.Contains(account.Fields, static field => field is { Name: "ECODE", Required: true });
        Assert.Contains(account.Fields, static field => field is { Name: "ENAME", Required: true });
        Assert.Contains(account.Fields, static field => field is { Name: "NormalizedECODE", Required: true });
        Assert.Contains(account.Fields, static field => field is { Name: "Status", ValueType: FieldValueType.Enum, Required: true });
        Assert.Contains(account.Fields, static field => field is { Name: "Revision", ValueType: FieldValueType.Integer, Required: true });
        Assert.Contains(account.Indexes, static index => index is { Name: "UX_IdentityAccount_NormalizedECODE", IsUnique: true });

        var credential = Assert.Single(definitions, static model => model.Name == "LocalCredential");
        AssertAuditFields(credential);
        Assert.Contains(credential.Indexes, static index => index is { Name: "UX_LocalCredential_AccountEID", IsUnique: true });
        Assert.Contains(credential.ForeignKeys, static foreignKey =>
            foreignKey is { Name: "FK_LocalCredential_IdentityAccount", PrincipalModelKey: "Identity/IdentityAccount" });

        var securityEvent = Assert.Single(definitions, static model => model.Name == "IdentitySecurityEvent");
        AssertAuditFields(securityEvent);
        Assert.Contains(securityEvent.ForeignKeys, static foreignKey =>
            foreignKey is { Name: "FK_IdentitySecurityEvent_TargetIdentityAccount", PrincipalModelKey: "Identity/IdentityAccount" });
    }

    private static void AssertAuditFields(ModelDefinition definition)
    {
        Assert.Contains(definition.Fields, static field => field is { Name: "CreatedBy", Required: true, IsAuditField: true });
        Assert.Contains(definition.Fields, static field => field is { Name: "CreatedAt", Required: true, IsAuditField: true });
        Assert.Contains(definition.Fields, static field => field is { Name: "UpdatedBy", Required: true, IsAuditField: true });
        Assert.Contains(definition.Fields, static field => field is { Name: "UpdatedAt", Required: true, IsAuditField: true });
    }

    private sealed class TestWebHostEnvironment(string environmentName) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "Identity.Tests";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = string.Empty;
        public string EnvironmentName { get; set; } = environmentName;
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
