using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Crud.LinkQueries;
using System.Text.Json.Serialization;

namespace ICP.Platform.Modules.Workspace;

/// <summary>批量集合保留每次实际出现位置，LID只用于来源校验，不能成为ACL主键。</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record WorkspaceBatchPermissionSource(WorkspacePosition Position, string[] LinkIds);

public sealed partial class WorkspaceContents
{
    /// <summary>一个图事务内重解全部来源、归一真实对象，再交公共批服务全量资格/配置校验与写入。</summary>
    public ValueTask<DataScopeAuthorizationResult<DataScopeBatchAuthorizationReceipt>> ReplaceBatchPermissionsAsync(string actor,
        IReadOnlyList<WorkspaceBatchPermissionSource> sources, DataScopeBatchConfiguration input,
        IDataScopeBatchAuthorization authorization, CancellationToken token) =>
        transactions.ExecuteAsync<DataScopeAuthorizationResult<DataScopeBatchAuthorizationReceipt>>(async cancellation =>
        {
            ValidatePermissionSources([WorkspacePermissionExecutions.Batch("submit")]);
            var targets = await ResolveBatchPermissionTargetsAsync(actor, sources, cancellation);
            return await authorization.ReplaceAsync(targets, input, WorkspacePermissionExecutions.Batch("submit"), cancellation);
        }, token);

    /// <summary>核对使用与保存相同的真实来源，不把失效来源解释为原保存未提交。</summary>
    public ValueTask<DataScopeAuthorizationResult<IReadOnlyList<DataScopeObjectConfiguration>>> ReadCurrentBatchPermissionsAsync(string actor,
        IReadOnlyList<WorkspaceBatchPermissionSource> sources, IDataScopeBatchAuthorization authorization, CancellationToken token) =>
        transactions.ExecuteAsync<DataScopeAuthorizationResult<IReadOnlyList<DataScopeObjectConfiguration>>>(async cancellation =>
        {
            ValidatePermissionSources([WorkspacePermissionExecutions.Batch("prepare")]);
            var targets = await ResolveBatchPermissionTargetsAsync(actor, sources, cancellation);
            return await authorization.ReadCurrentAsync(targets, WorkspacePermissionExecutions.Batch("prepare"), cancellation);
        }, token);

    // 来源只产出真实对象身份；图锁先于公共批服务的全部ACL锁，核对和保存保持同一顺序。
    private async Task<DataScopeAuthorizationTarget[]> ResolveBatchPermissionTargetsAsync(string actor,
        IReadOnlyList<WorkspaceBatchPermissionSource> sources, CancellationToken cancellation)
    {
        if (sources is null || sources.Count == 0) throw new LinkQueryException(400, "Workspace.InvalidSelection");
        await LockGraphAsync(cancellation);
        var targets = new HashSet<DataScopeAuthorizationTarget>();
        foreach (var source in sources)
        {
            if (source is null || source.Position is null || source.LinkIds is null || source.LinkIds.Length == 0
                || source.LinkIds.Any(string.IsNullOrWhiteSpace)) throw new LinkQueryException(400, "Workspace.InvalidSelection");
            var path = await positions.ResolveAsync(actor, source.Position, cancellation);
            // 分段读取是SQL工作单元，不是最终集合上限；全部来源都必须成立，不能略过失效LID。
            foreach (var segment in source.LinkIds.Distinct(StringComparer.Ordinal).Chunk(100))
                foreach (var target in await ResolveTargetsAsync(path.FolderId, segment, cancellation))
                    targets.Add(new(Resource(target.ModelKey), target.Id));
        }
        return targets.ToArray();
    }
}
