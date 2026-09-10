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
        services.AddScoped<IStandardCrudCreationRecorder, PersonCreationRecorder>();
        foreach (var (kind, model) in new[] { ("company", "Company"), ("department", "Department"), ("position", "Position"), ("organization.person", "Person") })
            services.AddScoped<ICP.Platform.Modules.Audit.Contracts.IBusinessAuditNameDirectory>(sp =>
                new BusinessAuditOrganizationNames(sp.GetRequiredService<ICP.Platform.Persistence.EfCore.EfPlatformDbContext>(), kind, model));
        services.AddStandardCrudResource(PersonCrudDescriptorFactory.Create());
        services.AddStandardCrudResource(PositionCrudDescriptorFactory.Create());
        services.AddStandardCrudResource(CompanyCrudDescriptorFactory.Create());
        services.AddStandardCrudResource(DepartmentCrudDescriptorFactory.Create());
        services.AddAuthorization(OrganizationTreeAccess.Configure);
        services.AddScoped<OrganizationTreeReader>();
        services.AddScoped<ICompanyDirectory, EfCompanyDirectory>();
        services.AddScoped<EfDepartmentDirectory>();
        services.AddScoped<IDepartmentDirectory>(serviceProvider =>
            serviceProvider.GetRequiredService<EfDepartmentDirectory>());
        services.AddScoped<IDepartmentHierarchyDirectory>(serviceProvider =>
            serviceProvider.GetRequiredService<EfDepartmentDirectory>());
        services.AddScoped<IPositionDirectory, PlatformQueryPositionDirectory>();
        services.AddScoped<IPersonUserBindingRepository, EfPersonUserBindingRepository>();
        services.AddScoped<IAuthorizationSummaryReader, NullAuthorizationSummaryReader>();
        services.AddScoped<IPersonUserBindingService, PersonUserBindingService>();
        services.AddScoped<IPersonLifecycleService, PersonLifecycleService>();
        services.AddScoped<IStandardCrudQueryCustomizer, PersonCrudQueryCustomizer>();
        services.AddScoped<IStandardCrudQueryCustomizer, PositionCrudQueryCustomizer>();
        services.AddScoped<IStandardCrudQueryCustomizer, CompanyCrudQueryCustomizer>();
        services.AddScoped<IStandardCrudQueryCustomizer, DepartmentCrudQueryCustomizer>();
        services.AddScoped<IStandardCrudDisplayValueResolver, PersonCrudDisplayValueResolver>();
        services.AddScoped<IStandardCrudDisplayValueResolver, DepartmentCrudDisplayValueResolver>();
        services.AddScoped<IStandardCrudSummaryProvider, PersonUserBindingSummaryProvider>();
        services.AddScoped<IStandardCrudSummaryProvider, PersonAuthorizationSummaryProvider>();
        services.AddScoped<IStandardCrudValidator, PersonCrudValidator>();
        services.AddScoped<IStandardCrudValidator, CompanyCrudValidator>();
        services.AddScoped<IStandardCrudValidator, DepartmentCrudValidator>();
        services.AddScoped<CompanyCrudDeleteExecutor>();
        services.AddScoped<IStandardCrudDeleteGuard>(serviceProvider =>
            serviceProvider.GetRequiredService<CompanyCrudDeleteExecutor>());
        services.AddScoped<IStandardCrudConditionalDeleteExecutor>(serviceProvider =>
            serviceProvider.GetRequiredService<CompanyCrudDeleteExecutor>());
        services.AddScoped<DepartmentCrudDeleteExecutor>();
        services.AddScoped<IStandardCrudDeleteGuard>(serviceProvider =>
            serviceProvider.GetRequiredService<DepartmentCrudDeleteExecutor>());
        services.AddScoped<IStandardCrudConditionalDeleteExecutor>(serviceProvider =>
            serviceProvider.GetRequiredService<DepartmentCrudDeleteExecutor>());
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
