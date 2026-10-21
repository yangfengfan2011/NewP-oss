using ICP.Platform.Sdk;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Crud.Extensions;
using ICP.Platform.Modules.Audit.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace ICP.Platform.Modules.Workspace;

/// <summary>工作空间只登记自己的应用服务；空间身份和入口由根 capsule 与 authoring 拥有。</summary>
public sealed class WorkspaceModule : ModuleCapsuleBindingBase
{
    public override string BehaviorKey => "workspace.backend";

    /// <inheritdoc/>
    public override void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton(new ICP.Platform.Authorization.Contracts.DataScopeResourceActionBinding("workspace.folder", ModuleId,
            ["read", "create", "edit", "delete", "authorize", "workspaceCopy"]));
        services.AddScoped<WorkspaceSpaceInitialization>();
        services.AddScoped<WorkspaceSpaces>();
        services.AddScoped<WorkspacePositions>();
        // 资源读取约束在调用时复核原位置，惰性取得解析器以避开FolderScope与LinkTreeReader的构造环。
        services.AddScoped<Func<WorkspacePositions>>(provider => () => provider.GetRequiredService<WorkspacePositions>());
        services.AddScoped<WorkspaceContents>();
        services.AddScoped<WorkspaceReorderHandler>();
        services.AddScoped<WorkspaceObjectCopyPreparationHandler>();
        services.AddScoped<WorkspaceObjectCopyHandler>();
        services.AddScoped<WorkspaceObjectCopyResultHandler>();
        services.AddScoped<WorkspaceRemoveReferencesHandler>();
        services.AddScoped<WorkspaceClipboardSelectionHandler>();
        services.AddScoped<WorkspacePasteReferencesHandler>();
        services.AddScoped<WorkspaceMutationHandler>();
        services.AddScoped<WorkspaceEditValuesHandler>();
        services.AddScoped<WorkspacePartCreationHandler>();
        services.AddScoped<WorkspaceDocumentCreationHandler>();
        services.AddScoped<WorkspaceDocumentUploadHandler>();
        services.AddScoped<WorkspaceFolderCreationHandler>();
        services.AddScoped<WorkspaceHistoryButtonHandler>();
        services.AddScoped<WorkspaceOwnershipTransferButtonHandler>();
        services.AddScoped<WorkspaceOwnershipInitializationButtonHandler>();
        services.AddScoped<WorkspaceAuthorizationSaveHandler>();
        services.AddScoped<WorkspaceAuthorizationReadHandler>();
        services.AddScoped<WorkspaceAuthorizationBatchSaveHandler>();
        services.AddScoped<WorkspaceAuthorizationBatchReadHandler>();
        services.AddSingleton(new DataScopeObjectInitializationBinding(ModuleId, "workspace.folder",
            WorkspaceFolderCopyExecutions.Caller(), "submit", "parent", "candidate"));
        services.AddSingleton(new DataScopeObjectInitializationBinding(ModuleId, "workspace.folder",
            WorkspaceFolderCopyExecutions.StructureCaller(), "submit", "parent", "candidate"));
        services.AddSingleton(new DataScopeObjectInitializationCreatedParentsBinding(ModuleId, "workspace.folder",
            WorkspaceFolderCopyExecutions.StructureCaller(), "submit", "candidate"));
        services.AddScoped<ICP.Platform.Crud.Ownership.EntityOwnershipTransfer>();
        services.AddScoped<ICP.Platform.Crud.Ownership.EntityOwnershipTransferDisplay>(provider => new(
            provider.GetRequiredService<ICP.Platform.Persistence.EfCore.EfPlatformDbContext>(),
            provider.GetRequiredService<IDataScopeResourceCatalog>(),
            provider.GetRequiredService<ICP.Platform.Crud.Abstractions.IStandardCrudResourceRegistry>(),
            OwnershipReads(provider, "workspace.ownership.display.targets", typeof(ICP.Platform.Crud.Ownership.EntityOwnershipTransferDisplay),
                nameof(ICP.Platform.Crud.Ownership.EntityOwnershipTransferDisplay.FindNamesAsync)),
            provider.GetRequiredService<IDataScopeObjectAccess>()));
        services.AddScoped<ICP.Platform.Crud.Ownership.IEntityOwnershipTransferReceiver, WorkspaceOwnershipTransferReceiver>();
        services.AddScoped<WorkspaceTransferNotifications>(provider => new(
            provider.GetRequiredService<ICP.Platform.Persistence.EfCore.EfPlatformDbContext>(),
            provider.GetRequiredService<ICP.Platform.Crud.Ownership.EntityOwnershipTransferDisplay>(),
            provider.GetRequiredService<ICP.Platform.Presentation.IUserDisplayNameDirectory>(),
            OwnershipReads(provider, "workspace.ownership.display.senders", typeof(WorkspaceTransferNotifications), nameof(WorkspaceTransferNotifications.ListAsync)),
            provider.GetRequiredService<WorkspaceContents>()));
        services.AddScoped<WorkspaceReferenceAudit>();
        services.AddScoped<ICP.Platform.Modules.Audit.Contracts.IBusinessAuditNameDirectory, WorkspaceFolderAuditNames>();
        RegisterHistory(services);
        services.AddScoped<ICP.Platform.Crud.Abstractions.IStandardCrudDisplayValueResolver>(provider =>
            new ICP.Platform.Crud.Ownership.EntityOwnershipDisplayResolver("workspace.folder",
                provider.GetRequiredService<ICP.Platform.Presentation.IUserDisplayNameDirectory>(),
                provider.GetRequiredService<ICP.Platform.Persistence.Ownership.IAccountOwnershipDirectory>()));
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
        services.AddKeyedScoped<IDataScopeResourceReadConstraint>("workspace.folder.scope", (provider, _) =>
            provider.GetRequiredService<WorkspaceFolderScope>());
        services.AddSingleton(new DataScopeResourceReadConstraintBinding("workspace.folder.scope", "workspace.folder", ModuleId));
        services.AddScoped<ICP.Platform.Crud.LinkQueries.ILinkQueryEndpointConstraint>(provider => provider.GetRequiredService<WorkspaceFolderScope>());
        services.AddScoped<ICP.Platform.Authorization.Contracts.IDataExecutionTargetResolver, WorkspacePositionDataExecutionResolver>();
        services.AddScoped<ICP.Platform.Authorization.Contracts.IDataExecutionTargetResolver, WorkspaceClipboardEndpointDataExecutionResolver>();
        services.AddScoped<ICP.Platform.Authorization.Contracts.IDataExecutionTargetResolver, WorkspacePermissionTargetReadResolver>();
        services.AddScoped<ICP.Platform.Crud.LinkQueries.ILinkQuerySourcePolicy, WorkspaceContentQueryPolicy>();
        services.AddScoped<ICP.Platform.Crud.LinkQueries.ILinkTreeSourceReadPolicy, WorkspaceContentReadPredicate>();
        services.AddSingleton<WorkspaceFolderReadDescriptor>();
        services.AddSingleton(new DataScopeResource("workspace.folder", ModuleId, "WorkspaceFolder", "EID", "workspace.folder.scope"));
    }

    // 只有通知真正读取名称时才解析执行器，避免读取约束、内容服务与显示投影构造闭环。
    private static IDataScopeReadableIds OwnershipReads(IServiceProvider provider, string execution, Type caller, string member) =>
        new DataExecutionReadableIds(() => provider.GetRequiredService<IDataExecutionAccess>(), new(execution, "workspace.backend", caller, member));

    // 只将本owner真实writer的固定schema映射到规范对象；LID始终是记录事实，不变成授权目标。
    private void RegisterHistory(IServiceCollection services)
    {
        services.AddSingleton(new ObjectHistoryEventPolicy(ModuleId, "workspace.folder.parentAuthorizationInitialized", "workspace.folder", "workspace.folder",
            ObjectHistoryTargetFormat.DeclaredFields, "Workspace.HistoryParentAuthorizationInitialized")
        { FieldAssociations = [new("PEID", ObjectHistoryFieldSide.After, "workspace.folder"), new("CEID", ObjectHistoryFieldSide.After, "workspace.folder")],
            SubjectField = new("CEID", ObjectHistoryFieldSide.After, "workspace.folder"), TextSnapshot = new("ObjectName", "ObjectCode") });
        foreach (var (eventType, description) in new[] {
            ("workspace.folder.created", "Workspace.HistoryFolderCreated"),
            ("workspace.folder.updated", "Workspace.HistoryFolderUpdated"),
            ("workspace.folder.deleted", "Workspace.HistoryFolderDeleted"),
            ("entity.ownership.initialized", "Workspace.HistoryOwnershipInitialized"),
            ("entity.ownership.backfilled", "Workspace.HistoryOwnershipBackfilled"),
            ("entity.ownership.transferred", "Workspace.HistoryOwnershipTransferred") })
            services.AddSingleton(new ObjectHistoryEventPolicy(ModuleId, eventType, "workspace.folder", "workspace.folder",
                ObjectHistoryTargetFormat.ObjectId, description)
            { TextSnapshot = eventType.StartsWith("workspace.folder.", StringComparison.Ordinal) ? new("ObjectName", "ObjectCode") : null });

        (string EventType, string Description, ObjectHistoryFieldSide Side, string[] Parents, string[] Children)[] relations =
        [
            ("workspace.reference.added", "Workspace.HistoryReferenceAdded", ObjectHistoryFieldSide.After, ["PEID"], ["CEID"]),
            ("workspace.reference.removed", "Workspace.HistoryReferenceRemoved", ObjectHistoryFieldSide.Before, ["PEID"], ["CEID"]),
            ("workspace.reference.copied", "Workspace.HistoryReferenceCopied", ObjectHistoryFieldSide.After, ["SourcePEID", "PEID"], ["CEID"]),
            ("workspace.reference.moved", "Workspace.HistoryReferenceMoved", ObjectHistoryFieldSide.After, ["SourcePEID", "PEID"], ["CEID"]),
            ("workspace.reference.reordered", "Workspace.HistoryReferenceReordered", ObjectHistoryFieldSide.After, ["PEID"], ["CEID"]),
            ("workspace.object.copied", "Workspace.HistoryObjectCopied", ObjectHistoryFieldSide.After, ["SourcePEID", "PEID"], ["SourceObject", "CEID"])
        ];
        foreach (var relation in relations)
        {
            services.AddSingleton(new ObjectHistoryEventPolicy(ModuleId, relation.EventType, "workspace.content", null,
                ObjectHistoryTargetFormat.DeclaredFields, relation.Description)
            { FieldAssociations = relation.Children.Select(field => new ObjectHistoryFieldAssociation(field, relation.Side)).ToArray(),
                ObjectTypeField = new("ChildModelKey", relation.Side), SubjectField = new("CEID", relation.Side) });
            services.AddSingleton(new ObjectHistoryEventPolicy(ModuleId, relation.EventType, "workspace.content", "workspace.folder",
                ObjectHistoryTargetFormat.DeclaredFields, relation.Description)
            { FieldAssociations = relation.Parents.Select(field => new ObjectHistoryFieldAssociation(field, relation.Side, "workspace.folder")).ToArray(),
                ObjectTypeField = new("ChildModelKey", relation.Side), SubjectField = new("CEID", relation.Side) });
        }
        foreach (var (eventType, description, fields) in new (string, string, string[])[] {
            ("workspace.folder.shallowCopied", "Workspace.HistoryFolderShallowCopied", ["SourcePEID", "PEID", "CEID"]),
            ("workspace.folder.structureCopied", "Workspace.HistoryFolderStructureCopied", ["SourcePEID", "CEID"]) })
            services.AddSingleton(new ObjectHistoryEventPolicy(ModuleId, eventType, "workspace.folder", "workspace.folder",
                ObjectHistoryTargetFormat.DeclaredFields, description)
            { FieldAssociations = fields.Select(field => new ObjectHistoryFieldAssociation(field, ObjectHistoryFieldSide.After, "workspace.folder")).ToArray(),
                SubjectField = new("CEID", ObjectHistoryFieldSide.After, "workspace.folder") });
    }
}
