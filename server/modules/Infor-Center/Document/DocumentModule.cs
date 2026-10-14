using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Extensions;
using ICP.Platform.Crud.ObjectOperations;
using ICP.Platform.Sdk;
using ICP.Platform.Modules.Document.Contracts;
using ICP.Platform.Modules.Files.Contracts;
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
        services.AddOptions<DocumentOptions>().BindConfiguration("Document")
            .Validate(options => options.MaxAttachmentsPerDocument is >= 0 and <= 100
                && options.AllowedFileExtensions.Length > 0
                && options.AllowedFileExtensions.All(extension => extension.StartsWith('.') && extension.Length > 1
                    && extension.All(character => character == '.' || char.IsAsciiLetterOrDigit(character))), "Document options are invalid.")
            .ValidateOnStart();
        services.AddScoped<DocumentFileCreationScope>();
        services.AddScoped<DocumentFileOwnerPolicy>();
        services.AddScoped<IFileOwnerPolicy>(provider => provider.GetRequiredService<DocumentFileOwnerPolicy>());
        services.AddSingleton(new FileOwnerPolicyRequirement(DocumentFileOwnerPolicy.Key));
        services.AddScoped<DocumentFiles>();
        services.AddScoped<IDocumentFiles>(provider => provider.GetRequiredService<DocumentFiles>());
        services.AddScoped<IStandardCrudCreationRecorder>(provider => provider.GetRequiredService<DocumentInitialIdentity>());
        services.AddScoped<IStandardCrudUpdateRecorder>(provider => provider.GetRequiredService<DocumentInitialIdentity>());
        services.AddScoped<IStandardCrudValidator, DocumentCrudValidator>();
        services.AddScoped<IStandardCrudQueryCustomizer, DocumentCrudQueryCustomizer>();
        services.AddScoped<IStandardCrudSummaryProvider, DocumentFilesSummaryProvider>();
    }
}
