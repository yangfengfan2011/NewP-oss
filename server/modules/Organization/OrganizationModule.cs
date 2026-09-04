using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Extensions;
using ICP.Platform.Modules.Organization.Crud;
using ICP.Platform.Modules.Organization.Directories;
using ICP.Platform.Modules.Organization.Persistence.Ef;
using ICP.Platform.Modules.Organization.Repositories;
using ICP.Platform.Modules.Organization.Services;
using ICP.Platform.Sdk;
using Microsoft.Extensions.DependencyInjection;

namespace ICP.Platform.Modules.Organization;

public sealed class OrganizationModule : ModuleCapsuleBindingBase
{
    public override string BehaviorKey => "organization.backend";

    /// <inheritdoc/>
    public override void ConfigureServices(IServiceCollection services)
    {
        services.AddStandardCrudKernel();
        services.AddStandardCrudResource(PersonCrudDescriptorFactory.Create());
        services.AddStandardCrudResource(PositionCrudDescriptorFactory.Create());
        services.AddStandardCrudResource(CompanyCrudDescriptorFactory.Create());
        services.AddScoped<IDepartmentRepository, EfDepartmentRepository>();
        services.AddScoped<ICompanyDirectory, EfCompanyDirectory>();
        services.AddScoped<IDepartmentDirectory, EfDepartmentDirectory>();
        services.AddScoped<IPositionDirectory, PlatformQueryPositionDirectory>();
        services.AddScoped<IPersonUserBindingRepository, EfPersonUserBindingRepository>();
        services.AddScoped<IDepartmentService, DepartmentService>();
        services.AddScoped<IAuthorizationSummaryReader, NullAuthorizationSummaryReader>();
        services.AddScoped<IPersonUserBindingService, PersonUserBindingService>();
        services.AddScoped<IPersonLifecycleService, PersonLifecycleService>();
        services.AddScoped<IStandardCrudQueryCustomizer, PersonCrudQueryCustomizer>();
        services.AddScoped<IStandardCrudQueryCustomizer, PositionCrudQueryCustomizer>();
        services.AddScoped<IStandardCrudQueryCustomizer, CompanyCrudQueryCustomizer>();
        services.AddScoped<IStandardCrudDisplayValueResolver, PersonCrudDisplayValueResolver>();
        services.AddScoped<IStandardCrudSummaryProvider, PersonUserBindingSummaryProvider>();
        services.AddScoped<IStandardCrudSummaryProvider, PersonAuthorizationSummaryProvider>();
        services.AddScoped<IStandardCrudValidator, PersonCrudValidator>();
        services.AddScoped<IStandardCrudValidator, CompanyCrudValidator>();
        services.AddScoped<CompanyCrudDeleteExecutor>();
        services.AddScoped<IStandardCrudDeleteGuard>(serviceProvider =>
            serviceProvider.GetRequiredService<CompanyCrudDeleteExecutor>());
        services.AddScoped<IStandardCrudConditionalDeleteExecutor>(serviceProvider =>
            serviceProvider.GetRequiredService<CompanyCrudDeleteExecutor>());
        services.AddScoped<PositionCrudDeleteExecutor>();
        services.AddScoped<IStandardCrudDeleteGuard>(serviceProvider =>
            serviceProvider.GetRequiredService<PositionCrudDeleteExecutor>());
        services.AddScoped<IStandardCrudConditionalDeleteExecutor>(serviceProvider =>
            serviceProvider.GetRequiredService<PositionCrudDeleteExecutor>());
        services.AddScoped<PersonCrudDeleteGuard>();
        services.AddScoped<IStandardCrudDeleteGuard>(serviceProvider =>
            serviceProvider.GetRequiredService<PersonCrudDeleteGuard>());
        services.AddScoped<IStandardCrudConditionalDeleteExecutor>(serviceProvider =>
            serviceProvider.GetRequiredService<PersonCrudDeleteGuard>());
    }

}
