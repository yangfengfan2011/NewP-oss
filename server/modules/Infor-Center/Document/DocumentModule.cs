using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Extensions;
using ICP.Platform.Crud.ObjectOperations;
using ICP.Platform.Sdk;
using ICP.Platform.Modules.Document.Contracts;
using ICP.Platform.Modules.Files.Contracts;
using ICP.Platform.Persistence.Planning;
using Microsoft.Extensions.DependencyInjection;

namespace ICP.Platform.Modules.Document;

/// <summary>图文档独立拥有元数据；标准内核负责事务、审计与写后读取。</summary>
public sealed class DocumentModule : ModuleCapsuleBindingBase
{
    public override string BehaviorKey => "document.backend";

    /// <inheritdoc/>
    public override void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton(new ICP.Platform.Authorization.Contracts.DataScopeResourceActionBinding("document.document", ModuleId,
            ["read", "create", "edit", "delete", "authorize", "documentCopy", "documentObjectCopy", "download"]));
        services.AddStandardCrudKernel();
        services.AddStandardCrudResource(provider =>
        {
            var plan = provider.GetRequiredService<CompiledPersistencePlan>();
            var document = plan.GetModelAuthoring(ModuleId, "Document");
            var models = plan.Models.Where(model => model.OwnerModuleId == ModuleId).ToArray();
            DocumentCopyFields.RequireAuthoring(models);
            return DocumentCrudDescriptor.Create(document, models.Single(model => model.LogicalKey == "Document"));
        });
        services.AddScoped<IStandardCrudDisplayValueResolver>(provider => new ICP.Platform.Crud.Ownership.EntityOwnershipDisplayResolver("document.document",
            provider.GetRequiredService<ICP.Platform.Presentation.IUserDisplayNameDirectory>(),
            provider.GetRequiredService<ICP.Platform.Persistence.Ownership.IAccountOwnershipDirectory>()));
        services.AddObjectBehavior<DocumentObjectBehavior>(this, DocumentObjectBehavior.BehaviorDefinition);
        services.AddObjectBehavior<DocumentCopyBehavior>(this, DocumentCopyBehavior.BehaviorDefinition);
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
        services.AddScoped<IDocumentCopies>(provider => provider.GetRequiredService<DocumentFiles>());
        services.AddScoped<IStandardCrudCreationRecorder>(provider => provider.GetRequiredService<DocumentInitialIdentity>());
        services.AddScoped<IStandardCrudUpdateRecorder>(provider => provider.GetRequiredService<DocumentInitialIdentity>());
        services.AddScoped<IStandardCrudValidator, DocumentCrudValidator>();
        services.AddScoped<IStandardCrudQueryCustomizer, DocumentCrudQueryCustomizer>();
        services.AddScoped<IStandardCrudSummaryProvider, DocumentFilesSummaryProvider>();
    }
}
