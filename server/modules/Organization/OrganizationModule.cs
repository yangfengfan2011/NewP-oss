using ICP.Platform.Kernel.Abstractions;
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
        services.AddScoped<ICompanyRepository, EfCompanyRepository>();
        services.AddScoped<IDepartmentRepository, EfDepartmentRepository>();
        services.AddScoped<IPositionRepository, EfPositionRepository>();
        services.AddScoped<IPersonRepository, EfPersonRepository>();
        services.AddScoped<IPersonUserLinkRepository, EfPersonUserLinkRepository>();
        services.AddScoped<ICompanyService, CompanyService>();
        services.AddScoped<IDepartmentService, DepartmentService>();
        services.AddScoped<IPositionService, PositionService>();
        services.AddScoped<IIdentityUserDirectory, NullIdentityUserDirectory>();
        services.AddScoped<IPersonService, PersonService>();
    }

    public IReadOnlyList<PermissionDescriptor> GetPermissions()
    {
        return new[]
        {
            new PermissionDescriptor
            {
                Code = "Organization.Person.View",
                DisplayName = "查看人员",
                Description = "允许查看组织结构中的人员信息",
                ModuleId = ModuleId,
                Group = "人员"
            }
        };
    }
}
