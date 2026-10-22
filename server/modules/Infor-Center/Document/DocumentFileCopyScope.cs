using System.Security.Claims;
using ICP.Platform.Modules.Files.Contracts;
using ICP.Platform.Persistence.EfCore;
using Microsoft.AspNetCore.Http;

namespace ICP.Platform.Modules.Document;

internal enum DocumentFileCopyPhase { Metadata, Prepare, Confirm }

/// <summary>只交接Document实际读取产生的来源事实；不存允许结果，不跨请求或事务复用。</summary>
public sealed class DocumentFileCopyScope(EfPlatformDbContext db, IHttpContextAccessor http)
{
    private Origin? current;

    // 原owner取得真实current身份后建立本阶段来源，完整关系身份不从客户端记录中推导。
    internal IDisposable Begin(DocumentCurrentIdentity identity, DocumentFileCopyPhase phase, out FileOwnerOperationContext source)
    {
        var context = http.HttpContext;
        var actor = context?.User.FindFirstValue(ClaimTypes.NameIdentifier);
        var document = identity.Master["EID"] as string;
        var version = identity.Version["EID"] as string;
        var iteration = identity.Iteration["EID"] as string;
        var transaction = db.Database.CurrentTransaction?.TransactionId;
        if (context?.User.Identity?.IsAuthenticated != true || string.IsNullOrWhiteSpace(actor)
            || string.IsNullOrWhiteSpace(document) || string.IsNullOrWhiteSpace(version) || string.IsNullOrWhiteSpace(iteration)
            || !Equals(identity.Master["LatestVersionId"], version) || !Equals(identity.Version["DocumentId"], document)
            || !Equals(identity.Version["CurrentIterationId"], iteration) || !Equals(identity.Iteration["VersionId"], version)
            || !Equals(identity.Iteration["Kind"], "regular")
            || phase == DocumentFileCopyPhase.Prepare && transaction is not null
            || phase == DocumentFileCopyPhase.Confirm && transaction is null)
            throw InvalidSource();
        var previous = current;
        var origin = new Origin(context, actor, document, version, iteration, transaction, phase,
            context.Request.Headers["X-NewP-Feature"].ToString(), context.Request.Headers["X-NewP-Button"].ToString());
        current = origin;
        source = new CopyContext(this, origin);
        return new Scope(this, origin, previous);
    }

    // 只有本scope当前阶段能交给policy；同对象的旧record、其他scope和已结束来源均拒绝。
    internal string? ObjectId(FileOwnerOperationContext? source, FileOwnerOperation operation)
    {
        var origin = Current(source);
        return origin is not null && (operation == FileOwnerOperation.Read && origin.Phase == DocumentFileCopyPhase.Metadata
            || operation == FileOwnerOperation.CopySource && origin.Phase is DocumentFileCopyPhase.Prepare or DocumentFileCopyPhase.Confirm)
            ? origin.DocumentId : null;
    }

    // policy仍独立读取真实回链；本来源不能在读取后换成另一版次或regular。
    internal bool Matches(FileOwnerOperationContext? source, string documentId, string versionId, string iterationId) =>
        Current(source) is { } origin && origin.DocumentId == documentId && origin.VersionId == versionId && origin.IterationId == iterationId;

    // 引用、HTTP主体与事务必须仍是原作用域，不把业务资格反馈存成可复用许可。
    private Origin? Current(FileOwnerOperationContext? source)
    {
        if (source is not CopyContext owned || !ReferenceEquals(owned.Owner, this) || !ReferenceEquals(current, owned.Origin)) return null;
        var origin = owned.Origin;
        var context = http.HttpContext;
        return context is not null && origin.Active && ReferenceEquals(context, origin.Context) && context.User.Identity?.IsAuthenticated == true
            && context.User.FindFirstValue(ClaimTypes.NameIdentifier) == origin.ActorId
            && db.Database.CurrentTransaction?.TransactionId == origin.TransactionId
            && context.Request.Headers["X-NewP-Feature"].ToString() == origin.FeaturePosition
            && context.Request.Headers["X-NewP-Button"].ToString() == origin.ButtonId ? origin : null;
    }

    private sealed record CopyContext(DocumentFileCopyScope Owner, Origin Origin) : FileOwnerOperationContext;
    private sealed record Origin(HttpContext Context, string ActorId, string DocumentId, string VersionId, string IterationId,
        Guid? TransactionId, DocumentFileCopyPhase Phase, string FeaturePosition, string ButtonId)
    { internal bool Active { get; set; } = true; }

    // 作用域错误不回退已登记的旧固定来源。
    private static FileLifecycleException InvalidSource() => new(400, "Document.FileReadContextInvalid");

    private sealed class Scope(DocumentFileCopyScope owner, Origin origin, Origin? previous) : IDisposable
    {
        private DocumentFileCopyScope? owner = owner;
        // 独立标记结束，乱序或重复Dispose不能恢复已结束的外层来源。
        public void Dispose()
        {
            var ending = Interlocked.Exchange(ref owner, null);
            if (ending is null) return;
            origin.Active = false;
            if (ReferenceEquals(ending.current, origin)) ending.current = previous?.Active == true ? previous : null;
        }
    }
}
