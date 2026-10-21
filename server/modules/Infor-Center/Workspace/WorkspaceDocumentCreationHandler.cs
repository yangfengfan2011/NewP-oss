using System.Collections.Immutable;
using System.Text.Json;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Crud.Models;
using ICP.Platform.Crud.ObjectOperations;
using ICP.Platform.Modules.Document.Contracts;
using ICP.Platform.Modules.Files.Contracts;

namespace ICP.Platform.Modules.Workspace;

/// <summary>文件准备的正式位置、业务字段和实际请求流；文件凭据不充当Document对象身份。</summary>
public sealed record WorkspaceDocumentUploadInput(string ActorId, WorkspacePosition Position,
    IReadOnlyDictionary<string, JsonElement> Values, Stream Content, string FileName, string ContentType);
/// <summary>原事务外准备的完整候选与原输入，不保存允许结果或用于后续保存。</summary>
public sealed record WorkspaceDocumentUploadState(WorkspaceDocumentUploadInput Input, IReadOnlyDictionary<string, object?> Candidate);

/// <summary>Stage在事务外核父资格、准备Document候选并交平台，获准后才按原格式顺序流式暂存。</summary>
public sealed class WorkspaceDocumentUploadHandler(WorkspaceContents contents, IDocumentFiles documents, IFileLifecycleService files)
    : IButtonBusinessHandler<WorkspaceDocumentUploadInput, WorkspaceDocumentUploadState, StagedFileInfo>
{
    /// <inheritdoc/>
    public async Task<StagedFileInfo> WithinBoundaryAsync(WorkspaceDocumentUploadInput input, string actionKey,
        Func<WorkspaceDocumentUploadState, CancellationToken, Task<StagedFileInfo>> execute, CancellationToken cancellationToken)
    {
        var owned = input with { Position = input.Position with { LinkIds = input.Position.LinkIds.ToArray() } };
        var preparation = await contents.PrepareDocumentUploadCandidateAsync(owned.ActorId, owned.Position, owned.Values, cancellationToken);
        if (!preparation.Succeeded) throw new WorkspaceCreationRejectedException(preparation.Error!);
        return await execute(new(owned, preparation.Detail!.Fields), cancellationToken);
    }
    /// <inheritdoc/>
    public ButtonBusinessTarget Target(WorkspaceDocumentUploadState state) => new ButtonCreationTarget("document.document", "Document", state.Candidate);
    /// <inheritdoc/>
    public Task<StagedFileInfo> ExecuteAsync(WorkspaceDocumentUploadState state, CancellationToken cancellationToken)
    {
        documents.ValidateUpload(state.Input.FileName, state.Input.ContentType);
        return files.StageAsync(state.Input.Content, state.Input.FileName, state.Input.ContentType, "document.fileLink", cancellationToken);
    }
}

/// <summary>完整Document创建输入，省略uploads保留原无文件创建。</summary>
public sealed record WorkspaceDocumentCreationInput(string ActorId, WorkspacePosition Position,
    IReadOnlyDictionary<string, JsonElement> Values, DocumentUploadSet? Uploads);
/// <summary>实际Document writer的同一候选与唯一持久化续体；Files后续仍由原root事务协调。</summary>
public sealed class WorkspaceDocumentCreationState
{
    public IReadOnlyDictionary<string, object?> Candidate { get; }
    internal Func<CancellationToken, Task<StandardCrudMutationResult>> Persist { get; }
    // 只在真实owner writer准备完整候选后建立，不允许客户端提供候选或persist。
    internal WorkspaceDocumentCreationState(IReadOnlyDictionary<string, object?> candidate,
        Func<CancellationToken, Task<StandardCrudMutationResult>> persist) { Candidate = candidate; Persist = persist; }
}

/// <summary>保留Workspace图锁及原Document完整创建链；自动按钮检查接在实际新候选与persist之间。</summary>
public sealed class WorkspaceDocumentCreationHandler(Func<IObjectOperationExecutor> operations)
    : IButtonBusinessHandler<WorkspaceDocumentCreationInput, WorkspaceDocumentCreationState, StandardCrudMutationResult>
{
    internal const string ContinuationField = "DocumentCreationContinuation";
    /// <inheritdoc/>
    public async Task<StandardCrudMutationResult> WithinBoundaryAsync(WorkspaceDocumentCreationInput input, string actionKey,
        Func<WorkspaceDocumentCreationState, CancellationToken, Task<StandardCrudMutationResult>> execute, CancellationToken cancellationToken)
    {
        var owned = input with { Position = input.Position with { LinkIds = input.Position.LinkIds.ToArray() } };
        var result = await operations().ExecuteAsync(WorkspaceObjectCreation.BehaviorDefinition.ObjectKey, WorkspaceObjectCreation.CreateDocument, null,
            new Dictionary<string, object?> { ["ActorId"] = owned.ActorId, ["Position"] = owned.Position, ["Values"] = owned.Values,
                ["Uploads"] = owned.Uploads, [ContinuationField] = new WorkspaceDocumentCreationContinuation(owned, execute) }, cancellationToken);
        if (!result.Succeeded) throw new WorkspaceCreationRejectedException(result.Error!);
        return result;
    }
    /// <inheritdoc/>
    public ButtonBusinessTarget Target(WorkspaceDocumentCreationState state) => new ButtonCreationTarget("document.document", "Document", state.Candidate);
    /// <inheritdoc/>
    public Task<StandardCrudMutationResult> ExecuteAsync(WorkspaceDocumentCreationState state, CancellationToken cancellationToken) => state.Persist(cancellationToken);
}

internal sealed class WorkspaceDocumentCreationContinuation
{
    private readonly string actor;
    private readonly string space;
    private readonly string root;
    private readonly ImmutableArray<string> path;
    internal Func<WorkspaceDocumentCreationState, CancellationToken, Task<StandardCrudMutationResult>> Execute { get; }
    // 只固定原受保护的actor/出现位置；Values与Uploads继续消费原Before业务输入，不携带资格缓存。
    internal WorkspaceDocumentCreationContinuation(WorkspaceDocumentCreationInput input,
        Func<WorkspaceDocumentCreationState, CancellationToken, Task<StandardCrudMutationResult>> execute)
    { actor = input.ActorId; space = input.Position.SpaceId; root = input.Position.RootFolderId; path = input.Position.LinkIds.ToImmutableArray(); Execute = execute; }
    // 同一原root输入必须按值相符；Document和Files随后独立核实际候选及上传凭据。
    internal WorkspaceDocumentCreationInput ReadInput(ObjectOperationContext context)
    {
        if (context.Action != WorkspaceObjectCreation.CreateDocument || context.Values.GetValueOrDefault("ActorId") is not string current || current != actor
            || context.Values.GetValueOrDefault("Position") is not WorkspacePosition position || position.SpaceId != space || position.RootFolderId != root
            || !position.LinkIds.SequenceEqual(path, StringComparer.Ordinal)) throw new InvalidOperationException("WORKSPACE_BUTTON_INPUT_INVALID");
        return new(actor, new(space, root, path.ToArray()), (IReadOnlyDictionary<string, JsonElement>)context.Values["Values"]!,
            context.Values.GetValueOrDefault("Uploads") as DocumentUploadSet);
    }
}
