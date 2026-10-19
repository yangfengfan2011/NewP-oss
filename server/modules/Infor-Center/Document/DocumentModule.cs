using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Extensions;
using ICP.Platform.Crud.ObjectOperations;
using ICP.Platform.Sdk;
using ICP.Platform.Modules.Document.Contracts;
using ICP.Platform.Modules.Files.Contracts;
using ICP.Platform.Persistence.Planning;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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
        services.AddSingleton(new ICP.Platform.Modules.Audit.Contracts.ObjectHistoryEventPolicy(ModuleId,
            "entity.ownership.transferred", "document.document", "document.document",
            ICP.Platform.Modules.Audit.Contracts.ObjectHistoryTargetFormat.ObjectId, "Document.HistoryOwnershipTransferred"));
        services.AddSingleton(new ICP.Platform.Modules.Audit.Contracts.ObjectHistoryEventPolicy(ModuleId,
            "entity.ownership.initialized", "document.document", "document.document",
            ICP.Platform.Modules.Audit.Contracts.ObjectHistoryTargetFormat.ObjectId, "Document.HistoryOwnershipInitialized"));
        services.AddSingleton(new ICP.Platform.Modules.Audit.Contracts.ObjectHistoryEventPolicy(ModuleId,
            "entity.ownership.backfilled", "document.document", "document.document",
            ICP.Platform.Modules.Audit.Contracts.ObjectHistoryTargetFormat.ObjectId, "Document.HistoryOwnershipBackfilled"));
        services.AddSingleton(new ICP.Platform.Modules.Audit.Contracts.ObjectHistoryEventPolicy(ModuleId,
            "document.created", "document.document", "document.document",
            ICP.Platform.Modules.Audit.Contracts.ObjectHistoryTargetFormat.ObjectId, "Document.HistoryCreated")
        { TextSnapshot = new("ObjectName", "ObjectCode") });
        services.AddSingleton(new ICP.Platform.Modules.Audit.Contracts.ObjectHistoryEventPolicy(ModuleId,
            "document.updated", "document.document", "document.document",
            ICP.Platform.Modules.Audit.Contracts.ObjectHistoryTargetFormat.ObjectId, "Document.HistoryUpdated")
        { TextSnapshot = new("ObjectName", "ObjectCode") });
        services.AddSingleton(new ICP.Platform.Modules.Audit.Contracts.ObjectHistoryEventPolicy(ModuleId,
            "document.deleted", "document.document", "document.document",
            ICP.Platform.Modules.Audit.Contracts.ObjectHistoryTargetFormat.ObjectId, "Document.HistoryDeleted")
        { TextSnapshot = new("ObjectName", "ObjectCode") });
        services.AddSingleton(new ICP.Platform.Modules.Audit.Contracts.ObjectHistoryEventPolicy(ModuleId,
            "document.iteration.initialized", "document.document", "document.document",
            ICP.Platform.Modules.Audit.Contracts.ObjectHistoryTargetFormat.ObjectId, "Document.HistoryIterationInitialized")
        { TextSnapshot = new("ObjectName", "ObjectCode") });
        services.AddSingleton(new ICP.Platform.Modules.Audit.Contracts.ObjectHistoryEventPolicy(ModuleId,
            "document.files.created", "document.document", "document.document",
            ICP.Platform.Modules.Audit.Contracts.ObjectHistoryTargetFormat.ObjectId, "Document.HistoryFilesCreated")
        { TextSnapshot = new("ObjectName", "ObjectCode") });
        services.AddSingleton(new ICP.Platform.Modules.Audit.Contracts.ObjectHistoryEventPolicy(ModuleId,
            "document.files.copied", "document.document", "document.document",
            ICP.Platform.Modules.Audit.Contracts.ObjectHistoryTargetFormat.ObjectId, "Document.HistoryFilesCopied")
        { TextSnapshot = new("ObjectName", "ObjectCode") });
        services.AddSingleton(new ICP.Platform.Modules.Audit.Contracts.ObjectHistoryEventPolicy(ModuleId,
            "document.files.released", "document.document", "document.document",
            ICP.Platform.Modules.Audit.Contracts.ObjectHistoryTargetFormat.ObjectId, "Document.HistoryFilesReleased")
        { TextSnapshot = new("ObjectName", "ObjectCode") });
        services.AddStandardCrudKernel();
        services.TryAddScoped<ICP.Platform.Crud.Ownership.EntityOwnershipTransfer>();
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
        services.AddScoped<DocumentFileReadScope>();
        services.AddScoped<ICP.Platform.Authorization.Contracts.IDataExecutionTargetResolver, DocumentFileReadResolver>();
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
