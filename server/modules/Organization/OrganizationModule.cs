using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Extensions;
using ICP.Platform.Modules.Organization.Crud;
using ICP.Platform.Modules.Organization.Persistence.Ef;
using ICP.Platform.Modules.Organization.Repositories;
using ICP.Platform.Modules.Organization.Services;
using ICP.Platform.Sdk;
using Microsoft.Extensions.DependencyInjection;

namespace ICP.Platform.Modules.Organization;

public sealed class OrganizationModule : ModuleCapsuleBindingBase
{
    public override string BehaviorKey => "organization.backend";

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

}
