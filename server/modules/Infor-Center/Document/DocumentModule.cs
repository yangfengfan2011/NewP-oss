using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Extensions;
using ICP.Platform.Crud.ObjectOperations;
using ICP.Platform.Sdk;
using Microsoft.Extensions.DependencyInjection;

namespace ICP.Platform.Modules.Document;

/// <summary>图文档独立拥有元数据；标准内核负责事务、审计与写后读取。</summary>
public sealed class DocumentModule : ModuleCapsuleBindingBase
{
    public override string BehaviorKey => "document.backend";

    /// <inheritdoc/>
    public override void ConfigureServices(IServiceCollection services)
    {
        services.AddStandardCrudKernel();
        services.AddStandardCrudResource(DocumentCrudDescriptor.Create());
        // 插入与仓储规范重读属于同一事务；提交后的展示才由标准服务降级为成功回执。
        services.AddStandardCrudObjectBehavior(this, new("document.document", ModuleId, "Document",
            [ObjectOperationAction.Create, ObjectOperationAction.Update, ObjectOperationAction.Delete], [], ["EID", "CreatedBy", "CreatedAt", "UpdatedBy", "UpdatedAt"]));
        services.AddScoped<IStandardCrudValidator, DocumentCrudValidator>();
        services.AddScoped<IStandardCrudQueryCustomizer, DocumentCrudQueryCustomizer>();
    }
}
