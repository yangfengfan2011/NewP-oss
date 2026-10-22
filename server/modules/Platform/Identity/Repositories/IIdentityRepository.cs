using ICP.Platform.Modules.Identity.Models;
using ICP.Platform.Identity.Contracts;

namespace ICP.Platform.Modules.Identity.Repositories;

public interface IIdentityRepository
{
    ValueTask AcquireLocalAdministratorBootstrapLockAsync(CancellationToken cancellationToken = default);

    ValueTask<IdentityAccount?> FindAccountAsync(string accountEid, CancellationToken cancellationToken = default);

    /// <summary>
    /// 按 exact EID 集合批量读取账号聚合，调用方负责决定是否暴露或过滤结果。
    /// </summary>
    ValueTask<IReadOnlyDictionary<string, IdentityAccount>> FindAccountsByIdsAsync(
        IReadOnlyCollection<string> accountEids,
        CancellationToken cancellationToken = default);

    ValueTask<IdentityAccount?> FindAccountByNormalizedCodeAsync(string normalizedCode, CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<IdentityAccount>> SearchAccountsAsync(CancellationToken cancellationToken = default);

    /// <summary>在数据源筛选、稳定排序并截取既有账号目录候选片段，不物化全账号集合。</summary>
    ValueTask<IReadOnlyList<IdentityAccount>> SearchAccountsAsync(IdentityAccountSearch search, CancellationToken cancellationToken = default);

    ValueTask<LocalCredential?> FindCredentialAsync(string accountEid, CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<IdentitySecurityEvent>> SearchSecurityEventsAsync(
        string targetAccountEid,
        CancellationToken cancellationToken = default);

    void AddAccount(IdentityAccount account);

    void AddCredential(LocalCredential credential);

    ValueTask ReplaceCredentialAsync(LocalCredential credential, CancellationToken cancellationToken = default);

    ValueTask RemoveCredentialAsync(string accountEid, CancellationToken cancellationToken = default);

    void AddSecurityEvent(IdentitySecurityEvent securityEvent);

    ValueTask<bool> TryReplaceAccountAsync(IdentityAccount account, long expectedRevision, CancellationToken cancellationToken = default);
}
