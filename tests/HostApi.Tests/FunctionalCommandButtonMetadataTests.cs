using System.Reflection;
using System.Text.Json;
using ICP.Platform.Authorization.Http;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Modules.Authorization;
using ICP.Platform.Modules.Authorization.Controllers;
using ICP.Platform.Modules.Identity;
using ICP.Platform.Modules.Identity.Controllers;
using ICP.Platform.Modules.Organization;
using ICP.Platform.Modules.Organization.Controllers;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

[Trait("Category", "ModuleMetadataConsumer")]
public sealed class FunctionalCommandButtonMetadataTests
{
    [Fact]
    public void ActualCommandHandlersAndEveryPositivePreparationUseRegisteredFeatureIdentities()
    {
        var compiled = new CanonicalModulePlanCompiler().Compile("functional-command-buttons",
            new ModuleCapsuleCandidateReader().ReadBuiltIn(Path.Combine(AppContext.BaseDirectory, "modules")));
        Assert.True(compiled.IsSuccess, JsonSerializer.Serialize(compiled.Diagnostics));
        var plan = compiled.Plan!;
        var services = new ServiceCollection();
        new OrganizationModule().ConfigureServices(services);
        new IdentityModule().ConfigureServices(services);
        new AuthorizationModule().ConfigureServices(services);
        using var provider = services.BuildServiceProvider();
        var registrations = provider.GetRequiredService<IServiceProviderIsService>();
        foreach (var controller in new[] { typeof(PersonLifecycleController), typeof(PersonUserBindingsController),
            typeof(OrganizationInitializationController), typeof(IdentityAccountsController), typeof(RoleController),
            typeof(UserRoleController), typeof(FeatureWorkbenchController), typeof(DataScopeRulesController) })
        foreach (var method in controller.GetMethods().Where(method => method.GetCustomAttribute<ButtonBusinessHandlerAttribute>() is not null))
        {
            var handler = method.GetCustomAttribute<ButtonBusinessHandlerAttribute>()!.HandlerType;
            Assert.True(registrations.IsService(handler), handler.FullName);
            var behavior = Assert.Single(method.GetCustomAttributes<RequireFeatureAttribute>()).BehaviorKey;
            var buttons = plan.ActionDataAccessCatalog.Where(button => button.BusinessBinding == behavior).ToArray();
            Assert.True(buttons.Length > 0, controller.Name + "." + method.Name + " has no registered button for " + behavior);
            Assert.All(buttons, button => { Assert.Null(button.Object); Assert.Empty(button.Actions); });
        }
        var http = new FeatureHttpTestRequests(plan.FeatureCatalog, plan.ActionDataAccessCatalog);
        foreach (var point in new[] { http.Identity("create"), http.Role("create"), http.Role("userRoles"),
            http.Organization("persons", "linkUser", "detail.custom"), http.Organization("persons", "enable", "list.rowActions"),
            http.Organization("persons", "disable", "list.rowActions"), http.Workbench("authorization.workbench.save"),
            http.Workbench("authorization.dataScope.save") })
            Assert.Equal(point.BehaviorKey, http.Declaration(point.BehaviorKey, point).BusinessBinding);
    }
}
