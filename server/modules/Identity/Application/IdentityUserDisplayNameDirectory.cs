using ICP.Platform.Identity.Contracts;
using ICP.Platform.Presentation;

namespace ICP.Platform.Modules.Identity.Application;

/// <summary>将 Identity 的稳定批量目录收窄到展示名称，避免公共投影依赖账号持久化或递归读取 CRUD。</summary>
public sealed class IdentityUserDisplayNameDirectory(IIdentityAccountDirectory directory) : IUserDisplayNameDirectory
{
    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, string>> FindManyAsync(
        IReadOnlyCollection<string> accountIds,
        CancellationToken cancellationToken = default)
    {
        var accounts = await directory.FindManyAsync(accountIds, cancellationToken);
        if (accounts is null || accounts.Any(pair => pair.Value is null || pair.Key != pair.Value.AccountEid || pair.Value.ENAME is null))
        {
            throw new UserDisplayNameDirectoryContractException();
        }
        return accounts.ToDictionary(pair => pair.Key, pair => pair.Value.ENAME, StringComparer.Ordinal);
    }
}
