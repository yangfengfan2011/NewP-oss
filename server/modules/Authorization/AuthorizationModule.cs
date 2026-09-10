using ICP.Platform.Crud.Extensions;
using ICP.Platform.Authorization.Abstractions;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Modules.Authorization.Persistence.Ef;
using ICP.Platform.Modules.Authorization.Repositories;
using ICP.Platform.Modules.Authorization.Services;
using ICP.Platform.Sdk;
using Microsoft.Extensions.DependencyInjection;
namespace ICP.Platform.Modules.Authorization;
/// <summary>Authorization 通过单一 capsule 绑定既有授权内核与角色管理。</summary>
public class AuthorizationModule : ModuleCapsuleBindingBase
{
    public override string BehaviorKey => "authorization.backend";
    /// <inheritdoc/>
    public override void ConfigureServices(IServiceCollection services)
    {
        services.AddPlatformObjectIdGeneration();

        services.AddStandardCrudKernel();
        services.AddStandardCrudResource(RoleReadResource.Create());
        services.AddScoped<ICP.Platform.Crud.Abstractions.IStandardCrudQueryCustomizer, RoleReadResource>();
        services.AddScoped<RoleManagementQuery>();
        services.AddSingleton<ActivePermissionCatalog>();
        services.AddScoped<ICP.Platform.Modules.Audit.Contracts.IBusinessAuditNameDirectory, BusinessAuditRoleNames>();
        services.AddScoped<Controllers.LocalAdministratorManagementFilter>();
        services.AddScoped<IRolePermissionWriter, RolePermissionWriter>();
        services.AddScoped<IUserRoleAssignments, UserRoleAssignments>();
        services.AddScoped<ICP.Platform.Modules.Audit.Contracts.IBusinessAuditNameDirectory, BusinessAuditAccountNames>();
        services.AddScoped<ILocalAdministratorCatalog, LocalAdministratorCatalog>();
        // 注册核心服务
        services.AddScoped<IAuthorizationService, AuthorizationService>();
        services.AddScoped<IPermissionDecisionSource, AuthorizationPermissionDecisionSource>();
        services.AddScoped<IRoleService, RoleService>();
        services.AddScoped<IPermissionService, PermissionService>();
        services.AddScoped<ILocalAdministratorRoleAssignment, EfLocalAdministratorRoleAssignment>();

        // 注册仓储
        services.AddScoped<IRoleRepository, EfRoleRepository>();
        services.AddScoped<IPermissionRepository, EfPermissionRepository>();
        services.AddScoped<IRolePermissionRepository, EfRolePermissionRepository>();
        services.AddScoped<IUserRoleRepository, EfUserRoleRepository>();
    }

}
