using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Crud.ObjectIds;
using ICP.Platform.Modules.Authorization.DTOs;
using ICP.Platform.Persistence.Abstractions;

namespace ICP.Platform.Modules.Authorization.Services;

/// <summary>仅在既有业务事务及stamp中克隆父直接项，不取得新的提交或操作者所有权。</summary>
public sealed class DataScopeObjectInitializer(DataScopeObjectGrants grants, DataScopeCatalog catalog,
    IPlatformTransactionExecutor transactions, IPlatformObjectIdGenerator ids) : IDataScopeObjectInitializer
{
    /// <inheritdoc/>
    public async Task<bool> InitializeFromParentAsync(string resourceKey, string parentObjectId, string newObjectId, CancellationToken token = default) =>
        (await InitializeFromParentsAsync(resourceKey, [parentObjectId], newObjectId, token)).Initialized;

    /// <inheritdoc/>
    public async Task<DataScopeObjectInitialization> InitializeFromParentsAsync(string resourceKey, IReadOnlyList<string> parentObjectIds, string newObjectId,
        CancellationToken token = default)
    {
        if (!transactions.IsActive) throw new InvalidOperationException("Direct authorization initialization requires an active business transaction.");
        ArgumentNullException.ThrowIfNull(parentObjectIds);
        ArgumentException.ThrowIfNullOrWhiteSpace(newObjectId);
        if (parentObjectIds.Count == 0 || parentObjectIds.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Direct authorization initialization requires parent objects.", nameof(parentObjectIds));
        if (parentObjectIds.Contains(newObjectId, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException("Direct authorization initialization requires a new target object.");
        catalog.Resolve(resourceKey);
        // 与手工替换共用父配置锁；读取后保留绝对期限、动态选择器和完整意见，不计算有效授权。
        var parents = parentObjectIds.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal).ToArray();
        foreach (var id in parents) await grants.LockAsync(resourceKey, id, token);
        var parent = await grants.GetAsync(resourceKey, parents[0], token);
        var configuration = DataScopeObjectGrants.SemanticConfiguration(parent);
        foreach (var id in parents.Skip(1))
            if (configuration != DataScopeObjectGrants.SemanticConfiguration(await grants.GetAsync(resourceKey, id, token)))
                throw new InvalidOperationException("Direct authorization initialization requires equivalent parent configurations.");
        await grants.LockAsync(resourceKey, newObjectId, token);
        var target = await grants.GetAsync(resourceKey, newObjectId, token);
        if (target.Subjects.Count != 0)
            throw new InvalidOperationException("Direct authorization initialization cannot replace an existing configuration.");
        if (parent.Subjects.Count == 0) return new(false, parents[0]);
        var copied = parent.Subjects.Select(subject => subject with { Id = ids.NewId("DataScopeObjectSubject") }).ToArray();
        return new(await grants.SaveInCurrentOperationAsync(new DataScopeObjectDto(resourceKey, newObjectId, copied), token), parents[0]);
    }
}
