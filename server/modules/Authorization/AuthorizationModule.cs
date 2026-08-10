using ICP.Platform.Crud.Extensions;
using ICP.Platform.Authorization.Abstractions;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Kernel.Abstractions;
using ICP.Platform.Modules.Authorization.Persistence.Ef;
using ICP.Platform.Modules.Authorization.Repositories;
using ICP.Platform.Modules.Authorization.Services;
using ICP.Platform.Sdk;
using Microsoft.Extensions.DependencyInjection;

namespace ICP.Platform.Modules.Authorization;

public class AuthorizationModule : ModuleBase, IPermissionProvider, IMenuProvider
{
    public override string ModuleId => "Authorization";
    public override string DisplayName => "权限管理";
    public override string Version => "1.0.0";
    public override string Description => "平台权限与授权管理模块";

    public override void ConfigureServices(IServiceCollection services)
    {
        services.AddPlatformObjectIdGeneration();

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

    public IReadOnlyList<PermissionDescriptor> GetPermissions()
    {
        return new[]
        {
            new PermissionDescriptor
            {
                Code = AuthorizationPermissions.RoleView,
                DisplayName = "查看角色",
                Description = "允许查看角色列表和详情",
                ModuleId = ModuleId,
                Group = "角色管理"
            },
            new PermissionDescriptor
            {
                Code = AuthorizationPermissions.RoleCreate,
                DisplayName = "创建角色",
                Description = "允许创建新角色",
                ModuleId = ModuleId,
                Group = "角色管理"
            },
            new PermissionDescriptor
            {
                Code = AuthorizationPermissions.RoleEdit,
                DisplayName = "编辑角色",
                Description = "允许编辑角色信息",
                ModuleId = ModuleId,
                Group = "角色管理"
            },
            new PermissionDescriptor
            {
                Code = AuthorizationPermissions.RoleDelete,
                DisplayName = "删除角色",
                Description = "允许删除角色",
                ModuleId = ModuleId,
                Group = "角色管理"
            },
            new PermissionDescriptor
            {
                Code = AuthorizationPermissions.PermissionView,
                DisplayName = "查看权限",
                Description = "允许查看权限列表",
                ModuleId = ModuleId,
                Group = "权限管理"
            },
            new PermissionDescriptor
            {
                Code = AuthorizationPermissions.PermissionAssign,
                DisplayName = "分配权限",
                Description = "允许为角色分配权限",
                ModuleId = ModuleId,
                Group = "权限管理"
            },
            new PermissionDescriptor
            {
                Code = AuthorizationPermissions.UserAssignRole,
                DisplayName = "分配用户角色",
                Description = "允许为用户分配角色",
                ModuleId = ModuleId,
                Group = "用户角色"
            }
        };
    }

    public IReadOnlyList<MenuDescriptor> GetMenus()
    {
        return new[]
        {
            new MenuDescriptor
            {
                Id = "Authorization",
                DisplayName = "权限管理",
                ParentId = null,
                Route = "/authorization",
                Icon = "shield",
                Order = 10,
                RequiredPermission = AuthorizationPermissions.RoleView,
                ModuleId = ModuleId
            },
            new MenuDescriptor
            {
                Id = "Authorization.Roles",
                DisplayName = "角色管理",
                ParentId = "Authorization",
                Route = "/authorization/roles",
                Icon = "user-group",
                Order = 1,
                RequiredPermission = AuthorizationPermissions.RoleView,
                ModuleId = ModuleId
            },
            new MenuDescriptor
            {
                Id = "Authorization.Permissions",
                DisplayName = "权限列表",
                ParentId = "Authorization",
                Route = "/authorization/permissions",
                Icon = "key",
                Order = 2,
                RequiredPermission = AuthorizationPermissions.PermissionView,
                ModuleId = ModuleId
            }
        };
    }
}
