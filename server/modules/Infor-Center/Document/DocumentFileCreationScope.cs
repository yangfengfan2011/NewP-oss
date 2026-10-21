using ICP.Platform.Modules.Files.Contracts;
using ICP.Platform.Persistence.EfCore;

namespace ICP.Platform.Modules.Document;

/// <summary>模块内首次创建证明，仅在已成功 nested Create 的同一外层事务内有效。</summary>
public sealed class DocumentFileCreationScope(EfPlatformDbContext db)
{
    private DocumentCurrentIdentity? _identity;
    private Guid? _transaction;
    private bool _copy;
    private FileOwnerOperationContext? _source;

    /// <summary>以当前事务和已核对真实身份启动证明；作用域退出立即失效。</summary>
    public IDisposable Begin(DocumentCurrentIdentity identity, bool copying = false, FileOwnerOperationContext? source = null)
    {
        if (_identity is not null || db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Document.CreationScopeInvalid");
        _identity = identity;
        _copy = copying;
        _source = source;
        _transaction = db.Database.CurrentTransaction.TransactionId;
        return new Scope(this);
    }

    // 只有Document完整Create成功后才建立新来源；第一次确认仍须同scope/事务及真实Added关联。
    internal IDisposable BeginCreated(DocumentCurrentIdentity identity, out FileOwnerOperationContext source)
    {
        source = new CreatedContext((string)identity.Master["EID"]!);
        return Begin(identity, source: source);
    }

    // 已确认重试只辨本owner私有来源的类型和对象事实；actor/lease与持久回链由Files和policy重新核验。
    internal string? CreatedObjectId(FileOwnerOperationContext? source) => source is CreatedContext created ? created.ObjectId : null;

    // 新创建与尚未迁移的Copy分别保留自己的来源形状，不把新业务冒充旧执行合同。
    private static string? ObjectId(FileOwnerOperationContext source) => source switch
    {
        CreatedContext created => created.ObjectId,
        FileOwnerCreationContext legacy => legacy.ObjectId,
        _ => null
    };
    private sealed record CreatedContext(string ObjectId) : FileOwnerOperationContext;

    /// <summary>确认仅匹配同一事务的真实当前版次，不从客户端接收授权标志。</summary>
    public bool ProvesIteration(string iterationId) => _identity is not null
        && _transaction == db.Database.CurrentTransaction?.TransactionId
        && Equals(_identity.Iteration["EID"], iterationId);

    /// <summary>上传确认必须消费当前新建流程交接的同一typed来源，不复用其他事务或其他对象的证明。</summary>
    public bool ProvesIteration(string iterationId, FileOwnerOperationContext source) => ProvesIteration(iterationId)
        && ReferenceEquals(_source, source) && Equals(_identity!.Master["EID"], ObjectId(source));

    /// <summary>复制证明仅由所属复制行为在真实新regular事务内建立，不能以旧上传证明替代。</summary>
    public bool ProvesCopyIteration(string iterationId, FileOwnerOperationContext source) => _copy && ProvesIteration(iterationId, source);

    // 生命周期结束清除证明，后续已有对象无法借上次创建追加文件。
    private sealed class Scope(DocumentFileCreationScope owner) : IDisposable
    {
        /// <inheritdoc/>
        public void Dispose() { owner._identity = null; owner._transaction = null; owner._copy = false; owner._source = null; }
    }
}
