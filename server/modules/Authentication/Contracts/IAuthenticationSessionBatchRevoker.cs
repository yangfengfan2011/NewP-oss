namespace ICP.Platform.Authentication.Contracts;

/// <summary>在当前平台事务中登记一组账号全部会话的永久撤销，统一事务负责最终保存。</summary>
public interface IAuthenticationSessionBatchRevoker
{
    /// <summary>按账号标识集合批量撤销；reason 为稳定原因，actorId 为服务端操作者，返回实际受影响会话数。</summary>
    ValueTask<int> RevokeAccountsAsync(IReadOnlyCollection<string> accountIds, string reason, string actorId,
        CancellationToken cancellationToken = default);
}
