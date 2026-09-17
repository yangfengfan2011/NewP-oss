using ICP.Platform.Modules.Organization.Caching;
using ICP.Platform.Organization.Contracts;
using ICP.Platform.Persistence.Transactions;
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
        services.AddSingleton<OrganizationCache>();
        services.AddSingleton<IPlatformCacheStatus>(sp => sp.GetRequiredService<OrganizationCache>());
        services.AddScoped<Microsoft.AspNetCore.Authorization.IAuthorizationHandler, OrganizationAvailabilityHandler>();
        services.AddSingleton<IOrganizationQuery, OrganizationQuery>();
        services.AddScoped<ICP.Platform.Identity.Contracts.IIdentityAdmissionPolicy, OrganizationAdmissionPolicy>();
        services.AddScoped<OrganizationAdmissionCommitParticipant>();
        foreach (var model in new[] { "Company", "Department", "Position", "Person" })
            services.AddScoped(sp => new PlatformCommitRegistration($"Organization/{model}", sp.GetRequiredService<OrganizationAdmissionCommitParticipant>()));
        services.AddHostedService<OrganizationCacheInitializer>();
        services.AddScoped<IStandardCrudCreationRecorder, PersonCreationRecorder>();
        services.AddScoped<IStandardCrudUpdateRecorder, PersonUpdateRecorder>();
        foreach (var (kind, model) in new[] { ("company", OrganizationRecordKind.Company), ("department", OrganizationRecordKind.Department), ("position", OrganizationRecordKind.Position), ("organization.person", OrganizationRecordKind.Person) })
            services.AddScoped<ICP.Platform.Modules.Audit.Contracts.IBusinessAuditNameDirectory>(sp =>
                new BusinessAuditOrganizationNames(sp.GetRequiredService<IOrganizationQuery>(), kind, model));
        services.AddStandardCrudResource(PersonCrudDescriptorFactory.Create());
        services.AddStandardCrudResource(PositionCrudDescriptorFactory.Create());
        services.AddStandardCrudResource(CompanyCrudDescriptorFactory.Create());
        services.AddStandardCrudResource(DepartmentCrudDescriptorFactory.Create());
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
        services.AddStandardCrudConditionalDelete<CompanyCrudDeleteExecutor>();
        services.AddStandardCrudConditionalDelete<DepartmentCrudDeleteExecutor>();
        services.AddStandardCrudConditionalDelete<PositionCrudDeleteExecutor>();
        services.AddStandardCrudConditionalDelete<PersonCrudDeleteGuard>();
    }

}
