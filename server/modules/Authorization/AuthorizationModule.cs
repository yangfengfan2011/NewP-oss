using ICP.Platform.Kernel.Abstractions;
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
        // 注册核心服务
        services.AddSingleton<IAuthorizationService, AuthorizationService>();
        services.AddSingleton<IRoleService, RoleService>();
        services.AddSingleton<IPermissionService, PermissionService>();

        // 注册仓储
        services.AddSingleton<IRoleRepository, RoleRepository>();
        services.AddSingleton<IPermissionRepository, PermissionRepository>();
        services.AddSingleton<IRolePermissionRepository, RolePermissionRepository>();
        services.AddSingleton<IUserRoleRepository, UserRoleRepository>();
    }

    public IReadOnlyList<PermissionDescriptor> GetPermissions()
    {
        return new[]
        {
            new PermissionDescriptor
            {
                Code = "Authorization.Role.View",
                DisplayName = "查看角色",
                Description = "允许查看角色列表和详情",
                ModuleId = ModuleId,
                Group = "角色管理"
            },
            new PermissionDescriptor
            {
                Code = "Authorization.Role.Create",
                DisplayName = "创建角色",
                Description = "允许创建新角色",
                ModuleId = ModuleId,
                Group = "角色管理"
            },
            new PermissionDescriptor
            {
                Code = "Authorization.Role.Edit",
                DisplayName = "编辑角色",
                Description = "允许编辑角色信息",
                ModuleId = ModuleId,
                Group = "角色管理"
            },
            new PermissionDescriptor
            {
                Code = "Authorization.Role.Delete",
                DisplayName = "删除角色",
                Description = "允许删除角色",
                ModuleId = ModuleId,
                Group = "角色管理"
            },
            new PermissionDescriptor
            {
                Code = "Authorization.Permission.View",
                DisplayName = "查看权限",
                Description = "允许查看权限列表",
                ModuleId = ModuleId,
                Group = "权限管理"
            },
            new PermissionDescriptor
            {
                Code = "Authorization.Permission.Assign",
                DisplayName = "分配权限",
                Description = "允许为角色分配权限",
                ModuleId = ModuleId,
                Group = "权限管理"
            },
            new PermissionDescriptor
            {
                Code = "Authorization.User.AssignRole",
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
                RequiredPermission = "Authorization.Role.View",
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
                RequiredPermission = "Authorization.Role.View",
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
                RequiredPermission = "Authorization.Permission.View",
                ModuleId = ModuleId
            }
        };
    }
}
