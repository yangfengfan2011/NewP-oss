using System.Collections.Frozen;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Crud.LinkQueries;
using Microsoft.EntityFrameworkCore;

namespace ICP.Platform.Modules.Workspace;

/// <summary>原名称读取或图事务内形成的实际来源与续体，不作为跨请求允许凭证。</summary>
public sealed class WorkspaceFolderCopyState<TResult> : IDisposable
{
    private readonly Func<bool> boundaryActive;
    private readonly Func<CancellationToken, Task<TResult>> execute;
    private bool active = true;
    private int executed;
    internal ButtonBusinessTarget Source { get; }

    // 状态仅由持有原连接或事务的Workspace协调者构造，不能由HTTP输入替换来源。
    internal WorkspaceFolderCopyState(ButtonBusinessTarget source, Func<bool> boundaryActive,
        Func<CancellationToken, Task<TResult>> execute)
    { Source = source; this.boundaryActive = boundaryActive; this.execute = execute; }

    // 平台只在原范围内取得同一状态，不另开事务或重建正式输入。
    internal Task<TResult> WithinAsync(Func<WorkspaceFolderCopyState<TResult>, CancellationToken, Task<TResult>> continuation, CancellationToken token)
    { RequireActive(); return continuation(this, token); }

    // 要求通过后只能执行一次该状态的原业务，不能把已有检查转移到其他闭包。
    internal Task<TResult> ExecuteAsync(CancellationToken token)
    {
        RequireActive();
        if (Interlocked.CompareExchange(ref executed, 1, 0) != 0) throw new InvalidOperationException("WORKSPACE_FOLDER_COPY_REUSED");
        return execute(token);
    }

    // 名称准备结束或离开原图事务后，保留的状态不能再次进入平台。
    private void RequireActive()
    {
        if (!active || !boundaryActive()) throw new InvalidOperationException("WORKSPACE_FOLDER_COPY_BOUNDARY_EXPIRED");
    }

    /// <inheritdoc/>
    public void Dispose() => active = false;
}

/// <summary>名称准备的来源由原连接范围确定，平台自动检查实际Folder要求后继续目标及名称读取。</summary>
public sealed class WorkspaceFolderCopyPreparationHandler
    : IButtonBusinessHandler<WorkspaceFolderCopyState<WorkspaceFolderCopyPreparation>, WorkspaceFolderCopyState<WorkspaceFolderCopyPreparation>, WorkspaceFolderCopyPreparation>
{
    /// <inheritdoc/>
    public Task<WorkspaceFolderCopyPreparation> WithinBoundaryAsync(WorkspaceFolderCopyState<WorkspaceFolderCopyPreparation> input, string actionKey,
        Func<WorkspaceFolderCopyState<WorkspaceFolderCopyPreparation>, CancellationToken, Task<WorkspaceFolderCopyPreparation>> execute, CancellationToken token) => input.WithinAsync(execute, token);
    /// <inheritdoc/>
    public ButtonBusinessTarget Target(WorkspaceFolderCopyState<WorkspaceFolderCopyPreparation> state) => state.Source;
    /// <inheritdoc/>
    public Task<WorkspaceFolderCopyPreparation> ExecuteAsync(WorkspaceFolderCopyState<WorkspaceFolderCopyPreparation> state, CancellationToken token) => state.ExecuteAsync(token);
}

/// <summary>同一来源在原图锁事务内通过按钮要求后，继续完整浅副本或结构业务。</summary>
public sealed class WorkspaceFolderCopyHandler
    : IButtonBusinessHandler<WorkspaceFolderCopyState<WorkspacePasteResult>, WorkspaceFolderCopyState<WorkspacePasteResult>, WorkspacePasteResult>
{
    /// <inheritdoc/>
    public Task<WorkspacePasteResult> WithinBoundaryAsync(WorkspaceFolderCopyState<WorkspacePasteResult> input, string actionKey,
        Func<WorkspaceFolderCopyState<WorkspacePasteResult>, CancellationToken, Task<WorkspacePasteResult>> execute, CancellationToken token) => input.WithinAsync(execute, token);
    /// <inheritdoc/>
    public ButtonBusinessTarget Target(WorkspaceFolderCopyState<WorkspacePasteResult> state) => state.Source;
    /// <inheritdoc/>
    public Task<WorkspacePasteResult> ExecuteAsync(WorkspaceFolderCopyState<WorkspacePasteResult> state, CancellationToken token) => state.ExecuteAsync(token);
}

public sealed partial class WorkspaceContents
{
    // 名称准备不改变事务边界；源父与完整端点读取先完成，平台再检查本次源Folder的用途。
    private async Task<WorkspaceFolderCopyPreparation> PrepareFolderCopyButtonAsync(string actor, string mode, WorkspacePosition sourcePosition,
        string[] linkIds, WorkspacePosition targetPosition, CancellationToken token)
    {
        if (mode != "copy" || linkIds.Length != 1 || string.IsNullOrWhiteSpace(linkIds[0]))
            throw new LinkQueryException(400, "Workspace.FolderCopySourceRequired");
        sourcePosition = FreezeClipboardPosition(sourcePosition);
        targetPosition = FreezeClipboardPosition(targetPosition);
        linkIds = linkIds.ToArray();
        var connection = db.Database.GetDbConnection();
        var openedHere = connection.State != System.Data.ConnectionState.Open;
        try
        {
            if (openedHere) await db.Database.OpenConnectionAsync(token);
            var parent = await positions.ResolveAsync(actor, sourcePosition, token);
            var source = await ReadFolderCopyButtonSourceAsync(parent.FolderId, linkIds, token);
            return await ExecuteFolderCopyButtonAsync(folderCopyPreparationHandler, source,
                () => connection.State == System.Data.ConnectionState.Open, async checkedToken =>
                {
                    await RequireNonRootClipboardTargetAsync(source, checkedToken);
                    var target = await ResolveWritableButtonPositionAsync(actor, targetPosition, checkedToken);
                    var name = await Folders.AsNoTracking().Where(row => EF.Property<string>(row, "EID") == source.Id)
                        .Select(row => EF.Property<string>(row, "ENAME")).SingleAsync(checkedToken);
                    return new WorkspaceFolderCopyPreparation(name, [.. parent.Names, name], target.Names);
                }, token);
        }
        finally { if (openedHere) await db.Database.CloseConnectionAsync(); }
    }

    // 目标是正式LID在当前父下解析的真实Folder；错误存储类型仍先沿原关联读取与用途顺序拒绝。
    private async Task<LinkObjectReference> ReadFolderCopyButtonSourceAsync(string parentId, string[] linkIds, CancellationToken token)
    {
        var source = (await ReadClipboardTargetsAsync(parentId, linkIds, token))[0];
        _ = Resource(source.ModelKey);
        if (!await reader.IsEndpointReadableAsync("workspace.content", source, token))
            throw new LinkQueryException(404, "Workspace.TargetUnavailable");
        if (source.ModelKey != WorkspacePositions.FolderModel)
        {
            if (!await FolderCopyPurposeAllowedAsync(source, token)) throw new LinkQueryException(403, "Workspace.ObjectCopyDenied");
            throw new LinkQueryException(400, "Workspace.FolderCopySourceRequired");
        }
        return source;
    }

    // 直接内容仍由业务在原逐项读取点检查自己的引用复制用途，不沿用源按钮的允许。
    private async Task<bool> FolderCopyPurposeAllowedAsync(LinkObjectReference target, CancellationToken token)
    {
        var action = target.ModelKey switch
        {
            WorkspacePositions.FolderModel => "workspaceCopy", "Part" => "partCopy", "Document" => "documentCopy",
            _ => throw new LinkQueryException(400, "Workspace.InvalidEndpoint")
        };
        return await objectActions.CheckAsync(target.ModelKey, target.Id, [action], token) == DataExecutionDecision.Allowed;
    }

    // 平台唯一消费规范source并继续原业务；状态不保存允许值，也不能离开原连接或事务复用。
    private async Task<TResult> ExecuteFolderCopyButtonAsync<TResult>(
        IButtonBusinessHandler<WorkspaceFolderCopyState<TResult>, WorkspaceFolderCopyState<TResult>, TResult> handler,
        LinkObjectReference source, Func<bool> boundaryActive, Func<CancellationToken, Task<TResult>> execute, CancellationToken token)
    {
        var before = new Dictionary<string, object?> { ["EID"] = source.Id }.ToFrozenDictionary(StringComparer.Ordinal);
        using var state = new WorkspaceFolderCopyState<TResult>(new ButtonExecutionTarget("workspace.folder", "WorkspaceFolder", source.Id, before), boundaryActive, execute);
        try { return await copyButtons.ExecuteAsync(handler, state, token); }
        catch (ActionDataAccessException exception) when (exception.Code is "DataScope.ActionDenied" or "StandardCrud.NotFound")
        { throw new LinkQueryException(exception.Status == 404 ? 404 : 403, exception.Status == 404 ? "Workspace.TargetUnavailable" : "Workspace.ObjectCopyDenied"); }
    }
}
