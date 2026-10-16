using ICP.Platform.Sdk;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Crud.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace ICP.Platform.Modules.Workspace;

/// <summary>工作空间只登记自己的应用服务；空间身份和入口由根 capsule 与 authoring 拥有。</summary>
public sealed class WorkspaceModule : ModuleCapsuleBindingBase
{
    public override string BehaviorKey => "workspace.backend";

    /// <inheritdoc/>
    public override void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<WorkspaceSpaces>();
        services.AddScoped<WorkspacePositions>();
        services.AddScoped<WorkspaceContents>();
        services.AddScoped<WorkspaceReferenceAudit>();
        services.AddScoped<WorkspaceTreeOrder>();
        services.AddObjectBehavior<WorkspaceObjectCopyBehavior>(this, WorkspaceObjectCopyBehavior.BehaviorDefinition,
            (provider, _) => new(() => provider.GetRequiredService<WorkspaceContents>(),
                provider.GetRequiredService<Func<ICP.Platform.Crud.ObjectOperations.IObjectOperationExecutor>>()));
        services.AddScoped<ICP.Platform.Crud.Runtime.ILinkTreeRootPolicy, WorkspaceTreeRoots>();
        // 写行为执行时再解析含展示服务的内容应用服务，避免对象执行器与 Link reader 的构造循环。
        services.AddObjectBehavior<WorkspaceObjectCreation>(this, WorkspaceObjectCreation.BehaviorDefinition,
            (provider, _) => new(() => provider.GetRequiredService<WorkspaceContents>(),
                provider.GetRequiredService<Func<ICP.Platform.Crud.ObjectOperations.IObjectOperationExecutor>>()));
        services.AddObjectBehavior<WorkspaceObjectMutation>(this, WorkspaceObjectMutation.BehaviorDefinition,
            (provider, _) => new(() => provider.GetRequiredService<WorkspaceContents>(),
                provider.GetRequiredService<Func<ICP.Platform.Crud.ObjectOperations.IObjectOperationExecutor>>()));
        services.AddScoped<WorkspaceFolderScope>();
        services.AddScoped<ICP.Platform.Crud.LinkQueries.ILinkQueryEndpointConstraint>(provider => provider.GetRequiredService<WorkspaceFolderScope>());
        services.AddScoped<ICP.Platform.Crud.LinkQueries.ILinkQuerySourcePolicy, WorkspaceContentQueryPolicy>();
        services.AddScoped<ICP.Platform.Crud.LinkQueries.ILinkTreeSourceReadPolicy, WorkspaceContentReadPredicate>();
        services.AddSingleton<WorkspaceFolderReadDescriptor>();
        services.AddSingleton(new DataScopeResource("workspace.folder", ModuleId, "WorkspaceFolder", "EID"));
    }
}
