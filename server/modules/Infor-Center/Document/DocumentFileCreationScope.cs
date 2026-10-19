using ICP.Platform.Modules.Files.Contracts;
using ICP.Platform.Persistence.EfCore;

namespace ICP.Platform.Modules.Document;

/// <summary>模块内首次创建证明，仅在已成功 nested Create 的同一外层事务内有效。</summary>
public sealed class DocumentFileCreationScope(EfPlatformDbContext db)
{
    private DocumentCurrentIdentity? _identity;
    private Guid? _transaction;
    private bool _copy;
    private FileOwnerCreationContext? _source;

    /// <summary>以当前事务和已核对真实身份启动证明；作用域退出立即失效。</summary>
    public IDisposable Begin(DocumentCurrentIdentity identity, bool copying = false, FileOwnerCreationContext? source = null)
    {
        if (_identity is not null || db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Document.CreationScopeInvalid");
        _identity = identity;
        _copy = copying;
        _source = source;
        _transaction = db.Database.CurrentTransaction.TransactionId;
        return new Scope(this);
    }

    /// <summary>确认仅匹配同一事务的真实当前版次，不从客户端接收授权标志。</summary>
    public bool ProvesIteration(string iterationId) => _identity is not null
        && _transaction == db.Database.CurrentTransaction?.TransactionId
        && Equals(_identity.Iteration["EID"], iterationId);

    /// <summary>上传确认必须消费当前新建流程交接的同一typed来源，不复用其他事务或其他对象的证明。</summary>
    public bool ProvesIteration(string iterationId, FileOwnerCreationContext source) => ProvesIteration(iterationId)
        && ReferenceEquals(_source, source) && Equals(_identity!.Master["EID"], source.ObjectId);

    /// <summary>复制证明仅由所属复制行为在真实新regular事务内建立，不能以旧上传证明替代。</summary>
    public bool ProvesCopyIteration(string iterationId) => _copy && ProvesIteration(iterationId);

    // 生命周期结束清除证明，后续已有对象无法借上次创建追加文件。
    private sealed class Scope(DocumentFileCreationScope owner) : IDisposable
    {
        /// <inheritdoc/>
        public void Dispose() { owner._identity = null; owner._transaction = null; owner._copy = false; owner._source = null; }
    }
}
