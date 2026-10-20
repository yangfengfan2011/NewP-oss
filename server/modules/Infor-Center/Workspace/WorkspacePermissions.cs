using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Crud.Models;

namespace ICP.Platform.Modules.Workspace;

/// <summary>属性仍是实体的标准详情；来源LID只用于本次访问校验，不成为对象身份。</summary>
public sealed record WorkspaceSourceProperties(string ResourceKey, string ObjectId, StandardCrudDetail Detail);

public sealed partial class WorkspaceContents
{
    /// <summary>来源属性只在当前完整位置中解析真实对象，再复用其正式descriptor与共享只读详情。</summary>
    public async Task<WorkspaceSourceProperties?> ReadPropertiesAsync(string actor, WorkspacePosition position, string linkId, CancellationToken token)
    {
        var target = await ResolvePermissionTargetAsync(actor, position, linkId, token);
        var descriptor = target.ResourceKey == folderDescriptor.Value.ResourceKey ? folderDescriptor.Value
            : resources.FindByResourceKey(target.ResourceKey) ?? throw new InvalidOperationException("Workspace object descriptor unavailable.");
        var detail = target.ResourceKey == folderDescriptor.Value.ResourceKey
            ? await crud.GetDetailFromEntryAsync(descriptor, target.ObjectId,
                [new("workspace.folder.read", "workspace.backend", "read")], token)
            : await crud.GetDetailFromSourceAsync(descriptor, target.ObjectId,
                new("workspace.properties.detail", "workspace.backend", typeof(WorkspaceContents), nameof(ReadPropertiesAsync)), token);
        return detail is null ? null : new(target.ResourceKey, target.ObjectId, detail);
    }

    /// <summary>从完整出现位置与真实关系重解规范对象，只查询可浏览对象，不要求授权管理资格。</summary>
    public async Task<DataScopeObjectInspection?> ReadPermissionsAsync(string actor, WorkspacePosition position, string linkId,
        string? accountId, IDataScopeObjectPermissionQueries queries, CancellationToken token)
    {
        ValidatePermissionSources(WorkspacePermissionExecutions.ViewSources);
        var target = await ResolvePermissionTargetAsync(actor, position, linkId, token);
        return await queries.InspectAsync(target.ResourceKey, target.ObjectId, accountId, WorkspacePermissionExecutions.ViewSources, token);
    }

    /// <summary>真实来源与授权替换共用图事务；先锁图，再解析当前LID及取得对象ACL锁，避免移除来源交错提交。</summary>
    public ValueTask<DataScopeAuthorizationResult> ReplacePermissionsAsync(string actor, WorkspacePosition position, string linkId,
        DataScopeObjectConfiguration configuration, IDataScopeObjectAuthorization authorization, CancellationToken token) =>
        transactions.ExecuteAsync<DataScopeAuthorizationResult>(async cancellation =>
        {
            ValidatePermissionSources([WorkspacePermissionExecutions.Management("submit")]);
            await LockGraphAsync(cancellation);
            var target = await ResolvePermissionTargetAsync(actor, position, linkId, cancellation);
            return await authorization.ReplaceAsync(target.ResourceKey, target.ObjectId, configuration, WorkspacePermissionExecutions.Management("submit"), cancellation);
        }, token);

    // 所有权限操作每次重解完整位置和当前LID，不把首次加载后的裸EID当来源证明。
    internal async Task<(string ResourceKey, string ObjectId)> ResolvePermissionTargetAsync(string actor, WorkspacePosition position, string linkId, CancellationToken token)
    {
        ValidateSelection([linkId]);
        var path = await positions.ResolveAsync(actor, position, token);
        var target = (await ResolveTargetsAsync(path.FolderId, [linkId], token)).Single();
        return (Resource(target.ModelKey), target.Id);
    }
}
