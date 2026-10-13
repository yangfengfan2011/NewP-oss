using ICP.Platform.Modeling;
using ICP.Platform.Modeling.Definitions;
using ICP.Platform.Modules.Identity;
using ICP.Platform.Modules.Identity.Persistence.Descriptors;
using ICP.Platform.Sdk;
using ICP.Platform.Identity.Contracts;
using ICP.Platform.Modules.Identity.Application;
using ICP.Platform.Modules.Identity.Repositories;
using ICP.Platform.Query.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using System.Text;
using System.Text.Json;
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
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IIdentityLoginApplicationService));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IPlatformQueryExecutor));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IValidateOptions<IdentityCredentialOptions>));
    }

    [Fact]
    public void AccountCrudResource_UsesSharedReadsWithoutExposingGenericWrites()
    {
        var descriptor = IdentityAccountCrudResource.Create();

        Assert.False(descriptor.MapStandardEndpoints);
        Assert.Empty(descriptor.FieldSet.CreateFields);
        Assert.Empty(descriptor.FieldSet.UpdateFields);
        Assert.Equal("api/identity/accounts", descriptor.Route);
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
            foreignKey is { Name: "FK_LocalCredential_IdentityAccount", PrincipalModelKey: "IdentityAccount" });

        var securityEvent = Assert.Single(definitions, static model => model.Name == "IdentitySecurityEvent");
        AssertAuditFields(securityEvent);
        var occurredAt = Assert.Single(securityEvent.Fields, static field => field.Name == "OccurredAt");
        Assert.Equal(FieldValueType.Instant, occurredAt.ValueType);
        Assert.Equal("instant", occurredAt.DisplayRule?.Type);
        Assert.Equal("dateTime", occurredAt.DisplayRule?.Format);
        Assert.Contains(securityEvent.ForeignKeys, static foreignKey =>
            foreignKey is { Name: "FK_IdentitySecurityEvent_TargetIdentityAccount", PrincipalModelKey: "IdentityAccount" });
    }

    [Fact]
    public void AccountListMetadata_OwnsProductionNavigationAndDetailCapability()
    {
        var root = FindRepositoryRoot();
        using var listView = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(root, @"frontend\modules\platform\identity\metadata\views\AccountList.view.json"),
            Encoding.UTF8));
        var view = listView.RootElement.GetProperty("view");

        Assert.Equal("identity.account.list", view.GetProperty("key").GetString());
        Assert.Equal("IdentityAccount", view.GetProperty("model").GetString());
        Assert.Equal("identity.account.detail", view.GetProperty("detailViewKey").GetString());
        Assert.Equal("ECODE", view.GetProperty("query").GetProperty("defaultSort").GetProperty("field").GetString());
        Assert.Equal(
            ["query", "status"],
            view.GetProperty("query").GetProperty("fields").EnumerateArray()
                .Select(field => field.GetProperty("key").GetString()));
        Assert.Equal(
            ["isPersonnelBound", "isLocalAdministrator"],
            view.GetProperty("advancedFilter").GetProperty("sections")[0].GetProperty("fields").EnumerateArray()
                .Select(field => field.GetProperty("key").GetString()));
        Assert.Equal(
            ["edit", "enable", "disable", "credentialReset", "delete"],
            view.GetProperty("rowActions").EnumerateArray().Select(action => action.GetProperty("key").GetString()));
        Assert.All(view.GetProperty("rowActions").EnumerateArray(), action =>
        {
            Assert.False(action.TryGetProperty("visibleWhen", out _));
            Assert.False(action.TryGetProperty("disabledWhen", out _));
        });

        using var navigation = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(root, @"frontend\modules\platform\identity\metadata\navigation\Identity.nav.json"),
            Encoding.UTF8));
        var accountEntry = navigation.RootElement.GetProperty("navigation").GetProperty("items").EnumerateArray()
            .Single(item => item.GetProperty("key").GetString() == "identity.accounts");
        Assert.Equal("standard-list", accountEntry.GetProperty("entryType").GetString());
        Assert.Equal("identity.account.list", accountEntry.GetProperty("targetKey").GetString());
        Assert.True(accountEntry.GetProperty("localAdministratorOnly").GetBoolean());

        using var capsule = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(root, @"frontend\modules\platform\identity\newp.plugin.json"),
            Encoding.UTF8));
        Assert.Equal(
            ["identity.account.list", "identity.account.detail"],
            capsule.RootElement.GetProperty("frontend").GetProperty("bindings").EnumerateArray()
                .Select(binding => binding.GetString()));

        using var detailView = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(root, @"frontend\modules\platform\identity\metadata\views\AccountDetail.view.json"),
            Encoding.UTF8));
        var detail = detailView.RootElement.GetProperty("view");
        Assert.Equal("identity.account.detail", detail.GetProperty("key").GetString());
        Assert.Equal("IdentityAccount", detail.GetProperty("model").GetString());
        Assert.Equal(
            ["basic", "protection", "audit"],
            detail.GetProperty("sections").EnumerateArray().Select(section => section.GetProperty("key").GetString()));
        Assert.Equal(["securityEvents"], detail.GetProperty("summaryBlocks").EnumerateArray().Select(block => block.GetString()));
        Assert.Equal(
            ["edit", "enable", "disable", "credentialReset", "delete"],
            detail.GetProperty("pageActions").EnumerateArray().Select(action => action.GetProperty("key").GetString()));
    }

    private static void AssertAuditFields(ModelDefinition definition)
    {
        Assert.Contains(definition.Fields, static field => field is { Name: "CreatedBy", Required: true, IsAuditField: true });
        Assert.Contains(definition.Fields, static field => field is { Name: "CreatedAt", Required: true, IsAuditField: true });
        Assert.Contains(definition.Fields, static field => field is { Name: "UpdatedBy", Required: true, IsAuditField: true });
        Assert.Contains(definition.Fields, static field => field is { Name: "UpdatedAt", Required: true, IsAuditField: true });
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "tests", "Identity.Tests", "Identity.Tests.csproj")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("未能定位仓库根目录。");
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
