using System.Reflection;
using ICP.Platform.Authorization.Http;
using ICP.Platform.Crud.Extensions;
using ICP.Platform.Modules.Authorization.Controllers;
using ICP.Platform.Modules.Identity.Controllers;
using Microsoft.AspNetCore.Authorization;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

[Trait("Category", "ModuleMetadataConsumer")]
public sealed class PlatformManagementClassificationTests
{
    [Fact]
    public void ActualManagementMethods_KeepFeatureQualificationAndExistingAdministratorGuards()
    {
        foreach (var (owner, method, behavior, administrator) in new[]
        {
            (typeof(IdentityAccountsController), "Create", "identity.account.create", true),
            (typeof(IdentityAccountsController), "UpdateName", "identity.account.update", true),
            (typeof(IdentityAccountsController), "Enable", "identity.account.enable", true),
            (typeof(IdentityAccountsController), "Disable", "identity.account.disable", true),
            (typeof(IdentityAccountsController), "Delete", "identity.account.delete", true),
            (typeof(IdentityAccountsController), "ResetCredential", "identity.account.credentialReset", true),
            (typeof(RoleController), "CreateRole", "authorization.role.create", false),
            (typeof(RoleController), "UpdateRole", "authorization.role.update", false),
            (typeof(RoleController), "GetAllRoles", "authorization.role.read", false),
            (typeof(UserRoleController), "Candidates", "authorization.userRoles.read", false),
            (typeof(UserRoleController), "GetUserRoles", "authorization.userRoles.read", false),
            (typeof(UserRoleController), "AssignUserRoles", "authorization.role.userRoles", false),
            (typeof(FeatureWorkbenchController), "Catalog", "authorization.workbench.read", false),
            (typeof(FeatureWorkbenchController), "Save", "authorization.workbench.save", false),
            (typeof(DataScopeRulesController), "Catalog", "authorization.dataScope.read", false),
            (typeof(DataScopeRulesController), "Subjects", "authorization.dataScope.read", false),
            (typeof(DataScopeRulesController), "List", "authorization.dataScope.read", false),
            (typeof(DataScopeRulesController), "Save", "authorization.dataScope.save", false),
            (typeof(DataScopeRulesController), "Delete", "authorization.dataScope.save", false)
        })
        {
            var endpoint = owner.GetMethod(method)!;
            Assert.Equal(behavior, Assert.Single(endpoint.GetCustomAttributes<RequireFeatureAttribute>()).BehaviorKey);
            Assert.Equal(administrator, endpoint.GetCustomAttributes<AuthorizeAttribute>().Any(attribute => attribute.Policy == "AdminOnly"));
            Assert.Empty(endpoint.GetCustomAttributes<StandardCrudDataExecutionAttribute>());
        }
        Assert.Single(typeof(RoleController).GetMethod("DeleteRole")!.GetCustomAttributes<DenyAllAttribute>());
        Assert.Single(typeof(IdentityAccountsController).GetMethod("SetStatus")!.GetCustomAttributes<DenyAllAttribute>());
    }
}
