namespace ICP.Platform.Persistence.Transactions;

/// <summary>一次已保存的模型变更；空前值表示新增，空后值表示删除。</summary>
public sealed record PlatformRecordChange(string ModelKey, string Id,
    IReadOnlyDictionary<string, object?>? Before, IReadOnlyDictionary<string, object?>? After);

/// <summary>模块装配时将归属模型登记到唯一提交参与者。</summary>
public sealed record PlatformCommitRegistration(string ModelKey, IPlatformCommitParticipant Participant);

public interface IPlatformCommitParticipant
{
    string Name { get; }
    /// <summary>在写入前取得协调权；取得后由唯一事务执行器准备、提交和发布。</summary>
    ValueTask<IPlatformCommitScope> BeginAsync(CancellationToken cancellationToken);
}

public interface IPlatformCommitScope : IAsyncDisposable
{
    /// <summary>
    /// 使用实际保存的前后值准备完整候选；不向读取方发布。
    /// 可登记未缓存模型的事务性变化，由执行器统一保存；准备开始后禁止改变缓存模型。
    /// </summary>
    ValueTask PrepareAsync(IReadOnlyList<PlatformRecordChange> changes, CancellationToken cancellationToken);
    /// <summary>在数据库提交前暂停新读取，封闭提交至发布窗口。</summary>
    void BlockReads();
    /// <summary>数据库确定提交后发布候选。</summary>
    ValueTask PublishAsync();
    /// <summary>提交后失败时封闭旧结果；不得重放业务写入。</summary>
    void MarkUnavailable();
}

public sealed class PlatformCacheUnavailableException : Exception
{
    public string CacheName { get; }
    public bool DatabaseCommitted { get; }
    /// <summary>区分暂不可用与已保存但发布失败，避免调用方错误重放。</summary>
    public PlatformCacheUnavailableException(string cacheName, bool databaseCommitted = false, Exception? inner = null)
        : base(databaseCommitted ? $"已保存，但缓存 {cacheName} 暂时不可用。" : $"缓存 {cacheName} 暂时不可用。", inner)
    { CacheName = cacheName; DatabaseCommitted = databaseCommitted; }
}
