using ICP.Platform.Authorization.Contracts;

namespace ICP.Platform.Modules.Organization.Tests;

/// <summary>不关心数据范围的旧资源单元测试显式声明所有引用可读。</summary>
internal sealed class AllowAllReadableIds : IDataScopeReadableIds
{
    /// <inheritdoc/>
    public Task<IReadOnlySet<string>> GetReadableIdsAsync(string resourceKey, IReadOnlyCollection<string> objectIds,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlySet<string>>(objectIds.ToHashSet(StringComparer.Ordinal));
}
