using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Extensions;
using ICP.Platform.Kernel.Abstractions;
using ICP.Platform.Modules.Organization.Crud;
using ICP.Platform.Modules.Organization.Persistence.Ef;
using ICP.Platform.Modules.Organization.Repositories;
using ICP.Platform.Modules.Organization.Services;
using ICP.Platform.Sdk;
using Microsoft.Extensions.DependencyInjection;

namespace ICP.Platform.Modules.Organization;

public sealed class OrganizationModule : ModuleBase, IPermissionProvider
{
    public override string ModuleId => "Organization";

    public override string DisplayName => "组织结构";

    public override string Description => "组织结构管理模块";

    public override void ConfigureServices(IServiceCollection services)
    {
        services.AddStandardCrudKernel();
        services.AddStandardCrudResource(PersonCrudDescriptorFactory.Create());
        services.AddScoped<ICompanyRepository, EfCompanyRepository>();
        services.AddScoped<IDepartmentRepository, EfDepartmentRepository>();
        services.AddScoped<IPositionRepository, EfPositionRepository>();
        services.AddScoped<IPersonUserLinkRepository, EfPersonUserLinkRepository>();
        services.AddScoped<ICompanyService, CompanyService>();
        services.AddScoped<IDepartmentService, DepartmentService>();
        services.AddScoped<IPositionService, PositionService>();
        services.AddScoped<IIdentityUserDirectory, NullIdentityUserDirectory>();
        services.AddScoped<IAuthorizationSummaryReader, NullAuthorizationSummaryReader>();
        services.AddScoped<IPersonUserLinkService, PersonUserLinkService>();
        services.AddScoped<IStandardCrudQueryCustomizer, PersonCrudQueryCustomizer>();
        services.AddScoped<IStandardCrudDisplayValueResolver, PersonCrudDisplayValueResolver>();
        services.AddScoped<IStandardCrudSummaryProvider, PersonUserLinkSummaryProvider>();
        services.AddScoped<IStandardCrudSummaryProvider, PersonAuthorizationSummaryProvider>();
        services.AddScoped<IStandardCrudValidator, PersonCrudValidator>();
        services.AddScoped<IStandardCrudDeleteGuard, PersonCrudDeleteGuard>();
    }

    public IReadOnlyList<PermissionDescriptor> GetPermissions()
    {
        return new[]
        {
            new PermissionDescriptor
            {
                Code = OrganizationPermissions.PersonView,
                DisplayName = "查看人员",
                Description = "允许查看组织结构中的人员信息",
                ModuleId = ModuleId,
                Group = "人员"
            },
            new PermissionDescriptor
            {
                Code = OrganizationPermissions.PersonCreate,
                DisplayName = "新建人员",
                Description = "允许新建组织结构中的人员信息",
                ModuleId = ModuleId,
                Group = "人员"
            },
            new PermissionDescriptor
            {
                Code = OrganizationPermissions.PersonUpdate,
                DisplayName = "更新人员",
                Description = "允许更新、停用或启用组织结构中的人员信息",
                ModuleId = ModuleId,
                Group = "人员"
            },
            new PermissionDescriptor
            {
                Code = OrganizationPermissions.PersonDelete,
                DisplayName = "删除人员",
                Description = "允许删除组织结构中的人员信息",
                ModuleId = ModuleId,
                Group = "人员"
            },
            new PermissionDescriptor
            {
                Code = OrganizationPermissions.PersonLinkUser,
                DisplayName = "关联登录用户",
                Description = "允许为人员关联登录用户",
                ModuleId = ModuleId,
                Group = "人员"
            },
            Permission(OrganizationPermissions.CompanyView, "查看公司", "公司"),
            Permission(OrganizationPermissions.CompanyCreate, "新建公司", "公司"),
            Permission(OrganizationPermissions.CompanyUpdate, "更新公司", "公司"),
            Permission(OrganizationPermissions.DepartmentView, "查看部门", "部门"),
            Permission(OrganizationPermissions.DepartmentCreate, "新建部门", "部门"),
            Permission(OrganizationPermissions.DepartmentUpdate, "更新部门", "部门"),
            Permission(OrganizationPermissions.PositionView, "查看岗位", "岗位"),
            Permission(OrganizationPermissions.PositionCreate, "新建岗位", "岗位"),
            Permission(OrganizationPermissions.PositionUpdate, "更新岗位", "岗位")
        };
    }

    private PermissionDescriptor Permission(string code, string displayName, string group) => new()
    {
        Code = code,
        DisplayName = displayName,
        Description = displayName,
        ModuleId = ModuleId,
        Group = group
    };
}
