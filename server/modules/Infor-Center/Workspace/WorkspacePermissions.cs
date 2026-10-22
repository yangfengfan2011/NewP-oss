using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Crud.Models;

namespace ICP.Platform.Modules.Workspace;

/// <summary>属性仍是实体的标准详情；来源LID只用于本次访问校验，不成为对象身份。</summary>
public sealed record WorkspaceSourceProperties(string ResourceKey, string ObjectId, StandardCrudDetail Detail);

public sealed partial class WorkspaceContents
{
    /// <summary>属性先核真实PageRef外部来源，再按原位置解析与两读取得对象；详情仍在原计划点读取。</summary>
    public async Task<WorkspaceSourceProperties?> ReadPropertiesAsync(string actor, WorkspacePosition position, string linkId, CancellationToken token)
    {
        DataExecutionBindingReference[] sources = [new("workspace.properties.read", "workspace.backend", "read")];
        ValidatePermissionSources(sources);
        var target = await ResolvePermissionTargetAsync(actor, position, linkId, token);
        var descriptor = target.ResourceKey == folderDescriptor.Value.ResourceKey ? folderDescriptor.Value
            : resources.FindByResourceKey(target.ResourceKey) ?? throw new InvalidOperationException("Workspace object descriptor unavailable.");
        var detail = await crud.GetDetailFromEntryAsync(descriptor, target.ObjectId, sources, token);
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

    // 原图事务贯穿目标解析、全部ACL锁和保存；按钮检查与真正提交共用同一规范对象。
    internal Task<DataScopeAuthorizationResult> WithinPermissionWriteAsync(string actor, WorkspacePosition position, string linkId,
        Func<(string ResourceKey, string ObjectId), CancellationToken, Task<DataScopeAuthorizationResult>> execute, CancellationToken token) =>
        transactions.ExecuteAsync<DataScopeAuthorizationResult>(async cancellation =>
        {
            await LockGraphAsync(cancellation);
            var target = await ResolvePermissionTargetAsync(actor, position, linkId, cancellation);
            return await execute(target, cancellation);
        }, token).AsTask();

    // 所有权限操作每次重解完整位置和当前LID，不把首次加载后的裸EID当来源证明。
    internal async Task<(string ResourceKey, string ObjectId)> ResolvePermissionTargetAsync(string actor, WorkspacePosition position, string linkId, CancellationToken token)
    {
        ValidateSelection([linkId]);
        var path = await positions.ResolveAsync(actor, position, token);
        var target = (await ResolveTargetsAsync(path.FolderId, [linkId], token)).Single();
        return (Resource(target.ModelKey), target.Id);
    }
}
