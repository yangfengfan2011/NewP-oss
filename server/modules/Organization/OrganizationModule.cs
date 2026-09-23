using ICP.Platform.Modules.Organization.Caching;
using ICP.Platform.Organization.Contracts;
using ICP.Platform.Persistence.Transactions;
using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Extensions;
using ICP.Platform.Crud.ObjectOperations;
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
        services.AddScoped<ICP.Platform.Authorization.Contracts.IDataScopeIdentityProvider, OrganizationDataScopeIdentities>();
        services.AddScoped<ICP.Platform.Authorization.Contracts.IDataScopeBusinessConditionProvider, OrganizationDataScopeConditions>();
        services.AddScoped<ICP.Platform.Authorization.Contracts.IDataScopeEnumFieldProvider, OrganizationDataScopeEnums>();
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
        services.AddScoped<PersonCreationRecorder>();
        services.AddScoped<IStandardCrudCreationRecorder>(static provider =>
            provider.GetRequiredService<PersonCreationRecorder>());
        services.AddScoped<IStandardCrudUpdateRecorder, PersonUpdateRecorder>();
        services.AddStandardCrudObjectBehavior(this, CompanyObjectBehavior.BehaviorDefinition);
        services.AddStandardCrudObjectBehavior(this, DepartmentObjectBehavior.BehaviorDefinition);
        services.AddStandardCrudObjectBehavior(this, PositionObjectBehavior.BehaviorDefinition);
        services.AddStandardCrudObjectBehavior(this, PersonObjectBehavior.BehaviorDefinition);
        foreach (var (kind, model) in new[] { ("company", OrganizationRecordKind.Company), ("department", OrganizationRecordKind.Department), ("position", OrganizationRecordKind.Position), ("organization.person", OrganizationRecordKind.Person) })
            services.AddScoped<ICP.Platform.Modules.Audit.Contracts.IBusinessAuditNameDirectory>(sp =>
                new BusinessAuditOrganizationNames(sp.GetRequiredService<IOrganizationQuery>(), kind, model));
        services.AddStandardCrudResource(PersonCrudDescriptorFactory.Create());
        services.AddStandardCrudResource(PositionCrudDescriptorFactory.Create());
        services.AddStandardCrudResource(CompanyCrudDescriptorFactory.Create());
        services.AddStandardCrudResource(DepartmentCrudDescriptorFactory.Create());
        services.AddScoped<OrganizationEffectiveFacts>(serviceProvider =>
        {
            var db = serviceProvider.GetRequiredService<ICP.Platform.Persistence.EfCore.EfPlatformDbContext>();
            return new OrganizationEffectiveFacts(
                serviceProvider.GetRequiredService<ICP.Platform.Persistence.Abstractions.IPlatformTransactionExecutor>(),
                new PublishedOrganizationFacts(serviceProvider.GetRequiredService<IOrganizationQuery>()),
                new SqlOrganizationFacts(db, new SqlServerDepartmentHierarchyQuery(db)));
        });
        services.AddScoped<ICompanyDirectory>(serviceProvider => new EfCompanyDirectory(
            serviceProvider.GetRequiredService<ICP.Platform.Persistence.EfCore.EfPlatformDbContext>(),
            serviceProvider.GetRequiredService<OrganizationEffectiveFacts>(),
            serviceProvider.GetService<ICP.Platform.Authorization.Contracts.IDataScopeReadPlanProvider>()));
        services.AddScoped<EfDepartmentDirectory>();
        services.AddScoped<IDepartmentDirectory>(serviceProvider =>
            serviceProvider.GetRequiredService<EfDepartmentDirectory>());
        services.AddScoped<IDepartmentHierarchyDirectory>(serviceProvider =>
            serviceProvider.GetRequiredService<EfDepartmentDirectory>());
        services.AddScoped<IPositionDirectory, PlatformQueryPositionDirectory>();
        services.AddScoped<IPersonUserBindingRepository, EfPersonUserBindingRepository>();
        services.AddScoped<IAuthorizationSummaryReader, NullAuthorizationSummaryReader>();
        services.AddScoped<PersonLifecycleActionCore>();
        services.AddScoped<PersonUserBindingActionCore>();
        services.AddScoped<PersonUserBindingService>(static provider => new PersonUserBindingService(
            provider.GetRequiredService<PersonUserBindingActionCore>(),
            provider.GetRequiredService<ICP.Platform.Persistence.Abstractions.IPlatformTransactionExecutor>()));
        services.AddScoped<IPersonUserBindingService>(static provider =>
            provider.GetRequiredService<PersonUserBindingService>());
        services.AddScoped<PersonLifecycleService>(static provider => new PersonLifecycleService(
            provider.GetRequiredService<PersonLifecycleActionCore>(),
            provider.GetRequiredService<ICP.Platform.Persistence.Abstractions.IPlatformTransactionExecutor>()));
        services.AddScoped<IPersonLifecycleService>(static provider =>
            provider.GetRequiredService<PersonLifecycleService>());
        services.AddScoped<IStandardCrudQueryCustomizer, PersonCrudQueryCustomizer>();
        services.AddScoped<IStandardCrudQueryCustomizer, PositionCrudQueryCustomizer>();
        services.AddScoped<IStandardCrudQueryCustomizer, CompanyCrudQueryCustomizer>();
        services.AddScoped<IStandardCrudQueryCustomizer, DepartmentCrudQueryCustomizer>();
        services.AddScoped<IStandardCrudDisplayValueResolver>(provider => new PersonCrudDisplayValueResolver(
            provider.GetRequiredService<IOrganizationQuery>(),
            provider.GetRequiredService<ICP.Platform.Authorization.Contracts.IDataScopeReadableIds>()));
        services.AddScoped<IStandardCrudDisplayValueResolver>(provider => new DepartmentCrudDisplayValueResolver(
            provider.GetRequiredService<IOrganizationQuery>(),
            provider.GetRequiredService<ICP.Platform.Authorization.Contracts.IDataScopeReadableIds>()));
        services.AddScoped<IStandardCrudSummaryProvider>(provider => new PersonUserBindingSummaryProvider(
            provider.GetRequiredService<IOrganizationQuery>(),
            provider.GetRequiredService<ICP.Platform.Identity.Contracts.IIdentityAccountDirectory>(),
            provider.GetRequiredService<ICP.Platform.Authorization.Contracts.IDataScopeReadableIds>()));
        services.AddScoped<IStandardCrudSummaryProvider, PersonAuthorizationSummaryProvider>();
        services.AddScoped<IStandardCrudValidator>(serviceProvider => new PersonCrudValidator(
            serviceProvider.GetRequiredService<ICompanyDirectory>(),
            serviceProvider.GetRequiredService<IDepartmentDirectory>(),
            serviceProvider.GetRequiredService<IPositionDirectory>(),
            serviceProvider.GetRequiredService<IStandardCrudRepository>(),
            serviceProvider.GetRequiredService<OrganizationEffectiveFacts>(),
            serviceProvider.GetService<ICP.Platform.Presentation.ITemporalInputNormalizer>()));
        services.AddScoped<IStandardCrudValidator, CompanyCrudValidator>();
        services.AddScoped<IStandardCrudValidator>(serviceProvider => new DepartmentCrudValidator(
            serviceProvider.GetRequiredService<ICP.Platform.Persistence.EfCore.EfPlatformDbContext>(),
            new SqlServerDepartmentHierarchyQuery(
                serviceProvider.GetRequiredService<ICP.Platform.Persistence.EfCore.EfPlatformDbContext>()),
            serviceProvider.GetRequiredService<OrganizationEffectiveFacts>()));
        services.AddScoped<OrganizationConditionalDeleteExecutor>();
        services.AddStandardCrudConditionalDelete<CompanyCrudDeleteExecutor>();
        services.AddStandardCrudConditionalDelete<DepartmentCrudDeleteExecutor>();
        services.AddStandardCrudConditionalDelete<PositionCrudDeleteExecutor>();
        services.AddStandardCrudConditionalDelete<PersonCrudDeleteGuard>();
    }

}
