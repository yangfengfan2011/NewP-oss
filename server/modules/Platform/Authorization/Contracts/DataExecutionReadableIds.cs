namespace ICP.Platform.Authorization.Contracts;

/// <summary>真实展示owner装配的批量读取适配器；实际读取才取得scoped执行器，避免其目标解析器与display目录构造闭环。</summary>
public sealed class DataExecutionReadableIds(Func<IDataExecutionAccess> access, DataExecutionInternalReadBinding binding) : IDataScopeReadableIds
{
    /// <inheritdoc/>
    public async Task<IReadOnlySet<string>> GetReadableIdsAsync(string resourceKey, IReadOnlyCollection<string> objectIds,
        CancellationToken cancellationToken = default)
    {
        try { return await access().GetInternalReadableIdsAsync(binding, resourceKey, objectIds, cancellationToken); }
        catch (ActionDataAccessException exception) { throw new DataScopeReadExecutionException(resourceKey, exception); }
    }
}
