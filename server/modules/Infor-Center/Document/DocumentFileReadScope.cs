using System.Security.Claims;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Modules.Files.Contracts;
using Microsoft.AspNetCore.Http;

namespace ICP.Platform.Modules.Document;

internal enum DocumentFileReadPhase { Metadata, Open, PolicyDownload, PolicyRelease, PolicyCopyMetadata, PolicyCopySource }

/// <summary>只保存当前阶段实际读取的目标事实，不保存允许结果。</summary>
internal sealed record DocumentFileReadFacts(HttpContext Context, string ActorId, string DocumentId, DocumentFileReadPhase Phase,
    string Declaration, string ButtonId, string FeaturePosition, string? VersionId, string? IterationId);

/// <summary>Document原读取位置建立的短作用域；回链阶段必须建立自己的新事实。</summary>
public sealed class DocumentFileReadScope(IHttpContextAccessor http, IButtonExecutionAccess buttons)
{
    private DocumentFileReadFacts? current;

    // 原owner读取完成后绑定当前请求、主体和阶段；退出后旧Source不能继续消费。
    internal IDisposable Begin(string documentId, DocumentFileReadPhase phase, DocumentCurrentIdentity? identity = null)
    {
        var context = http.HttpContext;
        var actor = context?.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (context?.User.Identity?.IsAuthenticated != true || string.IsNullOrWhiteSpace(actor) || string.IsNullOrWhiteSpace(documentId)
            || phase != DocumentFileReadPhase.Open && identity is null
            || identity is not null && !Equals(identity.Master["EID"], documentId))
            throw InvalidSource();
        var previous = current;
        current = new(context, actor, documentId, phase, context.Request.Headers["X-NewP-Data-Declaration"].ToString(),
            context.Request.Headers["X-NewP-Button"].ToString(), context.Request.Headers["X-NewP-Feature"].ToString(), identity?.Version["EID"] as string, identity?.Iteration["EID"] as string);
        return new Restore(() => current = previous);
    }

    // 输入只携带当前scope已取得的目标事实，不从客户端构造Source。
    internal DataExecutionTargetInput Target(string documentId) => new("document.document", documentId, current ?? throw InvalidSource());

    // 仅返回当前阶段的同一事实引用；请求、主体、目标或阶段变化均拒绝。
    internal DataExecutionResolvedTarget Resolve(DataExecutionTargetInput target)
    {
        if (target.Source is not DocumentFileReadFacts facts || !IsCurrent(facts, target.ResourceKey, target.ObjectId))
            throw InvalidSource();
        return new(target.ResourceKey, facts.DocumentId, facts);
    }

    // 文件打开只交接本owner在实际Open阶段建立的私有来源，不把Button身份或反馈结果当成允许。
    internal FileOwnerOperationContext DownloadContext(string documentId)
    {
        var facts = current ?? throw InvalidSource();
        if (facts.Phase != DocumentFileReadPhase.Open || !IsCurrent(facts, "document.document", documentId)) throw InvalidSource();
        return new DownloadOwnerContext(this, facts, buttons.BindHandler(typeof(DocumentFiles)));
    }

    // policy在自己的真实回链读取前重新核对同owner/HTTP/主体/按钮/功能和仍活动的Open阶段。
    internal string? DownloadObjectId(FileOwnerOperationContext? source) => source is DownloadOwnerContext owned
        && ReferenceEquals(owned.Owner, this) && owned.Facts.Phase == DocumentFileReadPhase.Open
        && IsCurrent(owned.Facts, "document.document", owned.Facts.DocumentId)
        && buttons.BindHandler(typeof(DocumentFiles)) == owned.Invocation ? owned.Facts.DocumentId : null;

    // 元数据、复制、释放与下载共享同一事实生命周期，但每个阶段必须消费自身当前引用。
    private bool IsCurrent(DocumentFileReadFacts facts, string resourceKey, string? objectId) => ReferenceEquals(current, facts)
        && ReferenceEquals(http.HttpContext, facts.Context) && facts.Context.User.Identity?.IsAuthenticated == true
        && facts.ActorId == facts.Context.User.FindFirstValue(ClaimTypes.NameIdentifier)
        && resourceKey == "document.document" && objectId == facts.DocumentId
        && facts.Declaration == facts.Context.Request.Headers["X-NewP-Data-Declaration"].ToString()
        && facts.ButtonId == facts.Context.Request.Headers["X-NewP-Button"].ToString()
        && facts.FeaturePosition == facts.Context.Request.Headers["X-NewP-Feature"].ToString()
        && (facts.Phase == DocumentFileReadPhase.Open || facts.VersionId is not null && facts.IterationId is not null);

    private sealed record DownloadOwnerContext(DocumentFileReadScope Owner, DocumentFileReadFacts Facts, ButtonBusinessInvocation Invocation) : FileOwnerOperationContext;

    // 无法对应本阶段真实读取事实时统一拒绝来源，不能退回直接判权。
    private static ActionDataAccessException InvalidSource() => new(400, "Document.FileReadContextInvalid");
    private sealed class Restore(Action restore) : IDisposable
    {
        private Action? restore = restore;
        // 恢复上层阶段的事实引用；同一阶段只能归还一次。
        public void Dispose() => Interlocked.Exchange(ref restore, null)?.Invoke();
    }
}

/// <summary>共享执行器消费本次owner读取的实际事实，不增加重复读取或接受客户端Source。</summary>
public sealed class DocumentFileReadResolver(DocumentFileReadScope scope) : IDataExecutionTargetResolver
{
    public string Key => "document.files.currentRead";
    public string ModuleId => "Document";
    /// <inheritdoc/>
    public Task<DataExecutionResolvedTarget?> ResolveAsync(DataExecutionTargetInput target, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return Task.FromResult<DataExecutionResolvedTarget?>(scope.Resolve(target));
    }
}
