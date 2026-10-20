using ICP.Platform.Crud.Extensions;
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
        services.AddSingleton(new ICP.Platform.Modules.Audit.Contracts.ObjectHistoryEventPolicy(ModuleId,
            "dataScope.object.changed", "authorization.dataScopeObject", null,
            ICP.Platform.Modules.Audit.Contracts.ObjectHistoryTargetFormat.QualifiedObjectId, "DataScope.HistoryAuthorizationChanged"));
        services.AddScoped<DataScopeIdentities>();
        services.AddSingleton<IDataScopeResourceCatalog, DataScopeResourceCatalog>();
        services.AddScoped<IDataScopeObjectAccess, DataScopeObjectAccess>();
        services.AddScoped<IDataExecutionAccess, DataExecutionAccess>();
        services.AddScoped<IObjectActionAccess, ObjectActionAccess>();
        services.AddScoped<IButtonExecutionAccess, ButtonExecutionAccess>();
        services.AddScoped<IButtonBusinessExecutor, ButtonBusinessExecutor>();
        services.AddScoped<IButtonQueryReadScope, ButtonQueryReadScope>();
        services.AddScoped<DataExecutionExceptionFilter>();
        services.AddScoped<IDataScopeObjectPermissionQueries>(provider =>
            ActivatorUtilities.CreateInstance<DataScopeObjectPermissionQueries>(provider, provider.GetRequiredService<IDataExecutionAccess>()));
        services.AddScoped<IDataScopeIdentityProvider, AllAuthenticatedDataScopeIdentities>();
        services.AddScoped<IDataScopeIdentityProvider, AuthorizationDataScopeIdentities>();
        services.AddScoped<DataScopeCatalog>();
        services.AddScoped<DataScopeRules>();
        services.AddScoped<DataScopeObjectGrants>();
        services.AddScoped<IDataScopeObjectInitializer, DataScopeObjectInitializer>();
        services.AddScoped<DataScopeObjectInitializationSources>();
        services.AddScoped<DataScopeObjectManagement>(provider =>
            ActivatorUtilities.CreateInstance<DataScopeObjectManagement>(provider, provider.GetRequiredService<IDataExecutionAccess>()));
        services.AddScoped<IDataScopeBatchAuthorization>(provider => provider.GetRequiredService<DataScopeObjectManagement>());
        services.AddScoped<IDataScopeObjectAuthorization>(provider => provider.GetRequiredService<DataScopeObjectManagement>());
        services.AddScoped<IDataScopeReadPlanProvider, DataScopeReadPlans>();
        services.AddScoped<IDataScopeReadableIds>(provider =>
            (DataScopeReadPlans)provider.GetRequiredService<IDataScopeReadPlanProvider>());
        services.AddScoped<IDataScopeObjectDecisionProvider>(provider =>
            (DataScopeReadPlans)provider.GetRequiredService<IDataScopeReadPlanProvider>());

        services.AddStandardCrudKernel();
        services.AddStandardCrudResource(RoleReadResource.Create());
        services.AddScoped<ICP.Platform.Crud.Abstractions.IStandardCrudQueryCustomizer, RoleReadResource>();
        services.AddScoped<RoleManagementQuery>();
        services.AddSingleton<ActiveFeatureCatalog>();
        services.AddScoped<FeatureDecisionSource>();
        services.AddScoped<ICP.Platform.Authorization.Http.IFeatureGrantProjection>(provider => provider.GetRequiredService<FeatureDecisionSource>());
        services.AddScoped<ICP.Platform.Authorization.Http.IFeatureDecisionSource>(provider => provider.GetRequiredService<FeatureDecisionSource>());
        services.AddScoped<FeatureCatalogSynchronizer>();
        services.AddScoped<ICP.Platform.Kernel.ModulePlanning.IFeatureCatalogSynchronizer>(provider => provider.GetRequiredService<FeatureCatalogSynchronizer>());
        services.AddScoped<RoleFeatureAssignments>();
        services.AddScoped<ICP.Platform.Modules.Audit.Contracts.IBusinessAuditNameDirectory, BusinessAuditRoleNames>();
        services.AddScoped<IUserRoleAssignments, UserRoleAssignments>();
        services.AddScoped<ICP.Platform.Modules.Audit.Contracts.IBusinessAuditNameDirectory, BusinessAuditAccountNames>();
        services.AddScoped<ILocalAdministratorCatalog, LocalAdministratorCatalog>();
        // 注册核心服务
        services.AddScoped<IAuthorizationService, AuthorizationService>();
        services.AddScoped<IRoleService, RoleService>();
        services.AddScoped<ILocalAdministratorRoleAssignment, EfLocalAdministratorRoleAssignment>();

        // 注册仓储
        services.AddScoped<IRoleRepository, EfRoleRepository>();
        services.AddScoped<IRolePermissionRepository, EfRolePermissionRepository>();
        services.AddScoped<IUserRoleRepository, EfUserRoleRepository>();
    }

}
