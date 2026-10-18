using ICP.Platform.Modules.Audit.Contracts;

namespace ICP.Platform.Modules.Audit.Services;

/// <summary>迁移中的已声明入口适配同一查询实现；独立owner迁移完后删除旧构造分支。</summary>
internal sealed class ReadableObjectHistoryQuery(ObjectHistoryQuery query) : IReadableObjectHistoryQuery
{
    /// <inheritdoc/>
    public Task<ObjectHistoryPage?> ListAsync(string resourceKey, string objectId, ObjectHistoryFilter filter, CancellationToken cancellationToken = default) =>
        query.ListAsync(resourceKey, objectId, filter, cancellationToken);
    /// <inheritdoc/>
    public Task<ObjectHistoryItem?> GetAsync(string resourceKey, string objectId, string recordId, ObjectHistoryFilter filter, CancellationToken cancellationToken = default) =>
        query.GetAsync(resourceKey, objectId, recordId, filter, cancellationToken);
    /// <inheritdoc/>
    public Task<IReadOnlyList<ObjectHistoryItem>?> OutputAsync(string resourceKey, string objectId, ObjectHistoryOutputRequest request, CancellationToken cancellationToken = default) =>
        query.OutputAsync(resourceKey, objectId, request, cancellationToken);
}

/// <summary>输出编码仍使用唯一实现，只将查询依赖切换到完整读取入口。</summary>
internal sealed class ReadableObjectHistoryExport(ObjectHistoryExport export) : IReadableObjectHistoryExport
{
    /// <inheritdoc/>
    public Task<ObjectHistoryFile?> ExportAsync(string resourceKey, string objectId, ObjectHistoryOutputRequest request, CancellationToken cancellationToken = default) =>
        export.ExportAsync(resourceKey, objectId, request, cancellationToken);
}
