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
        services.AddObjectBehavior<DocumentObjectBehavior>(this, DocumentObjectBehavior.BehaviorDefinition);
        services.AddScoped<DocumentInitialIdentity>();
        services.AddScoped<IStandardCrudCreationRecorder>(provider => provider.GetRequiredService<DocumentInitialIdentity>());
        services.AddScoped<IStandardCrudUpdateRecorder>(provider => provider.GetRequiredService<DocumentInitialIdentity>());
        services.AddScoped<IStandardCrudValidator, DocumentCrudValidator>();
        services.AddScoped<IStandardCrudQueryCustomizer, DocumentCrudQueryCustomizer>();
    }
}
