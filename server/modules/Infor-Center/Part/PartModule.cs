using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Extensions;
using ICP.Platform.Crud.ObjectOperations;
using ICP.Platform.Sdk;
using ICP.Platform.Persistence.Planning;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ICP.Platform.Modules.Part;

/// <summary>零组件独立拥有元数据；标准内核负责事务、审计与写后读取。</summary>
public sealed class PartModule : ModuleCapsuleBindingBase
{
    public override string BehaviorKey => "part.backend";

    /// <inheritdoc/>
    public override void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton(new ICP.Platform.Authorization.Contracts.DataScopeResourceActionBinding("part.part", ModuleId,
            ["read", "create", "edit", "delete", "authorize", "history", "partCopy", "partObjectCopy"]));
        services.AddSingleton(new ICP.Platform.Modules.Audit.Contracts.ObjectHistoryEventPolicy(ModuleId,
            "entity.ownership.transferred", "part.part", "part.part",
            ICP.Platform.Modules.Audit.Contracts.ObjectHistoryTargetFormat.ObjectId, "Part.HistoryOwnershipTransferred"));
        services.AddSingleton(new ICP.Platform.Modules.Audit.Contracts.ObjectHistoryEventPolicy(ModuleId,
            "entity.ownership.initialized", "part.part", "part.part",
            ICP.Platform.Modules.Audit.Contracts.ObjectHistoryTargetFormat.ObjectId, "Part.HistoryOwnershipInitialized"));
        services.AddSingleton(new ICP.Platform.Modules.Audit.Contracts.ObjectHistoryEventPolicy(ModuleId,
            "entity.ownership.backfilled", "part.part", "part.part",
            ICP.Platform.Modules.Audit.Contracts.ObjectHistoryTargetFormat.ObjectId, "Part.HistoryOwnershipBackfilled"));
        services.AddSingleton(new ICP.Platform.Modules.Audit.Contracts.ObjectHistoryEventPolicy(ModuleId,
            "part.created", "part.part", "part.part",
            ICP.Platform.Modules.Audit.Contracts.ObjectHistoryTargetFormat.ObjectId, "Part.HistoryCreated")
        { TextSnapshot = new("ObjectName", "ObjectCode") });
        services.AddSingleton(new ICP.Platform.Modules.Audit.Contracts.ObjectHistoryEventPolicy(ModuleId,
            "part.updated", "part.part", "part.part",
            ICP.Platform.Modules.Audit.Contracts.ObjectHistoryTargetFormat.ObjectId, "Part.HistoryUpdated")
        { TextSnapshot = new("ObjectName", "ObjectCode") });
        services.AddSingleton(new ICP.Platform.Modules.Audit.Contracts.ObjectHistoryEventPolicy(ModuleId,
            "part.deleted", "part.part", "part.part",
            ICP.Platform.Modules.Audit.Contracts.ObjectHistoryTargetFormat.ObjectId, "Part.HistoryDeleted")
        { TextSnapshot = new("ObjectName", "ObjectCode") });
        services.AddStandardCrudKernel();
        services.TryAddScoped<ICP.Platform.Crud.Ownership.EntityOwnershipTransfer>();
        services.AddStandardCrudResource(provider =>
        {
            var plan = provider.GetRequiredService<CompiledPersistencePlan>();
            var document = plan.GetModelAuthoring(ModuleId, "Part");
            var model = plan.Models.Single(model => model.OwnerModuleId == ModuleId && model.LogicalKey == "Part");
            PartCopyBehavior.RequireFieldClassification(model.Fields.Select(field => field.Name));
            return PartCrudDescriptor.Create(document, model);
        });
        services.AddScoped<IStandardCrudDisplayValueResolver>(provider => new ICP.Platform.Crud.Ownership.EntityOwnershipDisplayResolver("part.part",
            provider.GetRequiredService<ICP.Platform.Presentation.IUserDisplayNameDirectory>(),
            provider.GetRequiredService<ICP.Platform.Persistence.Ownership.IAccountOwnershipDirectory>()));
        // 插入与仓储规范重读属于同一事务；提交后的展示才由标准服务降级为成功回执。
        services.AddScoped<PartBusinessRecorder>();
        services.AddScoped<IStandardCrudCreationRecorder>(provider => provider.GetRequiredService<PartBusinessRecorder>());
        services.AddScoped<IStandardCrudUpdateRecorder>(provider => provider.GetRequiredService<PartBusinessRecorder>());
        services.AddObjectBehavior<PartObjectBehavior>(this, PartObjectBehavior.BehaviorDefinition);
        services.AddObjectBehavior<PartCopyBehavior>(this, PartCopyBehavior.BehaviorDefinition);
        services.AddScoped<IStandardCrudValidator, PartCrudValidator>();
        services.AddScoped<IStandardCrudQueryCustomizer, PartCrudQueryCustomizer>();
    }
}
