using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Modules.Files.Contracts;
using Microsoft.EntityFrameworkCore;

namespace ICP.Platform.Modules.Document;

/// <summary>专用下载API的正式业务输入；文件关联只用于owner回链，不作为Document授权目标。</summary>
public sealed record DocumentFileDownloadInput(string DocumentId, string FileLinkId);

/// <summary>本次业务读取取得的真实Document，执行阶段只用于窄化原错误翻译，不保存允许结果。</summary>
public sealed class DocumentFileDownloadState
{
    internal DocumentFileDownloadInput Input { get; }
    internal IReadOnlyDictionary<string, object?> Before { get; }
    internal bool ExecutionStarted { get; set; }
    // 只由原业务read与真实对象查询成功后创建。
    internal DocumentFileDownloadState(DocumentFileDownloadInput input, IReadOnlyDictionary<string, object?> before)
    { Input = input; Before = before; }
}

public sealed partial class DocumentFiles : IButtonBusinessHandler<DocumentFileDownloadInput, DocumentFileDownloadState, FileUsageContent>
{
    /// <inheritdoc/>
    async Task<FileUsageContent> IButtonBusinessHandler<DocumentFileDownloadInput, DocumentFileDownloadState, FileUsageContent>.WithinBoundaryAsync(
        DocumentFileDownloadInput input, string actionKey, Func<DocumentFileDownloadState, CancellationToken, Task<FileUsageContent>> execute, CancellationToken token)
    {
        var before = await RequireReadAsync(input.DocumentId, token);
        using var phase = readScope.Begin(input.DocumentId, DocumentFileReadPhase.Open);
        var state = new DocumentFileDownloadState(input, before);
        try { return await execute(state, token); }
        catch (ActionDataAccessException exception) when (!state.ExecutionStarted
            && (exception.Status == 404 && exception.Code == "StandardCrud.NotFound" || exception.Status == 403 && exception.Code == "DataScope.ActionDenied"))
        {
            // 只翻译平台目标检查的已知拒绝；policy、回链与I/O开始后任何同类故障原样传播。
            throw new FileLifecycleException(exception.Status, exception.Status == 404 ? "Files.UsageUnavailable" : "Document.DownloadDenied");
        }
    }

    /// <inheritdoc/>
    ButtonBusinessTarget IButtonBusinessHandler<DocumentFileDownloadInput, DocumentFileDownloadState, FileUsageContent>.Target(DocumentFileDownloadState state) =>
        new ButtonExecutionTarget("document.document", "Document", state.Input.DocumentId, state.Before);

    /// <inheritdoc/>
    async Task<FileUsageContent> IButtonBusinessHandler<DocumentFileDownloadInput, DocumentFileDownloadState, FileUsageContent>.ExecuteAsync(
        DocumentFileDownloadState state, CancellationToken token)
    {
        state.ExecutionStarted = true;
        var current = await identities.ReadCurrentAsync(state.Input.DocumentId, token);
        var link = await db.Set<Dictionary<string, object?>>("DocumentFileLink").AsNoTracking().SingleOrDefaultAsync(row =>
            EF.Property<string>(row, "LID") == state.Input.FileLinkId && EF.Property<string>(row, "PEID") == (string)current.Iteration["EID"]!, token);
        if (link is null) throw new FileLifecycleException(404, "Files.UsageUnavailable");
        return await files.OpenUsageAsync(Reference(link), readScope.DownloadContext(state.Input.DocumentId), token);
    }
}
