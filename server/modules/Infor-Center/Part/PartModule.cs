using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Extensions;
using ICP.Platform.Crud.ObjectOperations;
using ICP.Platform.Sdk;
using Microsoft.Extensions.DependencyInjection;

namespace ICP.Platform.Modules.Part;

/// <summary>零组件独立拥有元数据；标准内核负责事务、审计与写后读取。</summary>
public sealed class PartModule : ModuleCapsuleBindingBase
{
    public override string BehaviorKey => "part.backend";

    /// <inheritdoc/>
    public override void ConfigureServices(IServiceCollection services)
    {
        PartCopyBehavior.RequireFieldClassification(new PartModelMetadataProvider().GetDefinitions().Single().Fields.Select(field => field.Name));
        services.AddStandardCrudKernel();
        services.AddStandardCrudResource(PartCrudDescriptor.Create());
        // 插入与仓储规范重读属于同一事务；提交后的展示才由标准服务降级为成功回执。
        services.AddStandardCrudObjectBehavior(this, new("part.part", ModuleId, "Part",
            [ObjectOperationAction.Create, ObjectOperationAction.Update, ObjectOperationAction.Delete], [], ["EID", "CreatedBy", "CreatedAt", "UpdatedBy", "UpdatedAt"]));
        services.AddObjectBehavior<PartCopyBehavior>(this, PartCopyBehavior.BehaviorDefinition);
        services.AddScoped<IStandardCrudValidator, PartCrudValidator>();
        services.AddScoped<IStandardCrudQueryCustomizer, PartCrudQueryCustomizer>();
    }
}
