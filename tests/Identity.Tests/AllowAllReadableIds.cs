using ICP.Platform.Authorization.Contracts;

namespace ICP.Platform.Identity.Tests;

/// <summary>旧 Identity 单元测试显式选择全部账号可读，正式 Host 总是注入授权实现。</summary>
internal sealed class AllowAllReadableIds : IDataScopeReadableIds
{
    /// <inheritdoc/>
    public Task<IReadOnlySet<string>> GetReadableIdsAsync(string resourceKey, IReadOnlyCollection<string> objectIds,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlySet<string>>(objectIds.ToHashSet(StringComparer.Ordinal));
}
