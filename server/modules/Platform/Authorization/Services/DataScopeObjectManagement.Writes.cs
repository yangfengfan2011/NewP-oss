using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Modules.Authorization.DTOs;

namespace ICP.Platform.Modules.Authorization.Services;

public sealed partial class DataScopeObjectManagement
{
    // 路由/owner目标先取得管理资格，再校验payload；两个单对象入口共同进入完整手工写入流程。
    private Task<DataScopeObjectResult<DataScopeBatchAuthorizationReceipt>> SaveConfigurationAsync(
        string resourceKey, string objectId, DataScopeObjectConfiguration input, CancellationToken token,
        DataExecutionBindingReference? execution,
        Func<IReadOnlyList<DataScopeAuthorizationTarget>, Func<CancellationToken, Task<DataScopeObjectResult<DataScopeBatchAuthorizationReceipt>>>,
            CancellationToken, Task<DataScopeObjectResult<DataScopeBatchAuthorizationReceipt>>>? execute = null)
    {
        // 单对象路由中不可能存在的ID仍按原对象不可读处理；它不是批量目标清单的格式错误。
        if (string.IsNullOrWhiteSpace(objectId) || objectId.Length > 128)
            return Task.FromResult(new DataScopeObjectResult<DataScopeBatchAuthorizationReceipt>(DataScopeObjectStatus.NotFound));
        return WriteAsync([new(resourceKey, objectId)], async (_, cancellation) =>
        {
            if (input.ResourceKey != resourceKey || input.ObjectId != objectId)
                throw new RolePermissionValidationException("对象授权目标与请求不一致。", "DATA_SCOPE_OBJECT_TARGET_MISMATCH", field: "objectId");
            return new[] { await grants.PrepareAsync(new(input.ResourceKey, input.ObjectId, input.Subjects), cancellation) };
        }, token, execution, execute);
    }

    // 唯一手工写入时序：先锁齐并重验全部资格，再准备全部配置，最后同stamp登记并保存；父克隆不进入此流程。
    private async Task<DataScopeObjectResult<DataScopeBatchAuthorizationReceipt>> WriteAsync(
        IReadOnlyList<DataScopeAuthorizationTarget> targets,
        Func<IReadOnlyList<DataScopeAuthorizationTarget>, CancellationToken, Task<IReadOnlyList<DataScopeObjectDto>?>> prepare,
        CancellationToken token, DataExecutionBindingReference? execution,
        Func<IReadOnlyList<DataScopeAuthorizationTarget>, Func<CancellationToken, Task<DataScopeObjectResult<DataScopeBatchAuthorizationReceipt>>>,
            CancellationToken, Task<DataScopeObjectResult<DataScopeBatchAuthorizationReceipt>>>? execute = null)
    {
        using var observation = evaluation?.BeginOperation();
        using var operation = audit.BeginOperation();
        RolePermissionValidationException? rejectedWrite = null;
        try
        {
            // stamp保持到Execute返回，覆盖根事务及嵌套事务的实际SaveChanges，而不只覆盖Apply。
            return await transactions.ExecuteAsync<DataScopeObjectResult<DataScopeBatchAuthorizationReceipt>>(async cancellation =>
            {
                var ordered = OrderTargets(targets);
                var status = await LockAndCheckTargetsAsync(ordered, cancellation, execution);
                if (status != DataScopeObjectStatus.Succeeded) return new(status);
                // 新按钮只在原锁内交接目标，续体仍复用同一准备、登记与回滚流程。
                return execute is null ? await SavePreparedAsync(cancellation) : await execute(ordered, SavePreparedAsync, cancellation);

                // 续体只准备并登记已锁定集合，错误仍由外层唯一事务翻译。
                async Task<DataScopeObjectResult<DataScopeBatchAuthorizationReceipt>> SavePreparedAsync(CancellationToken writeToken)
                {
                    IReadOnlyList<DataScopeObjectDto>? configurations;
                    try { configurations = await prepare(ordered, writeToken); }
                    catch (RolePermissionValidationException error)
                    { return new(DataScopeObjectStatus.InvalidInput, ValidationMessage: error.Message, ValidationError: error); }
                    if (configurations is null) return new(DataScopeObjectStatus.InvalidInput);
                    var changed = 0;
                    try
                    {
                        foreach (var configuration in configurations)
                            if (await grants.ApplyAsync(configuration, writeToken)) changed++;
                    }
                    catch (RolePermissionValidationException error)
                    {
                        // 已进入登记阶段的拒绝先触发事务回滚；不在callback内吞掉可能发生在中途的失败。
                        rejectedWrite = error;
                        throw;
                    }
                    return new(DataScopeObjectStatus.Succeeded, new(true, ordered.Length, changed));
                }
            }, token);
        }
        catch (RolePermissionValidationException error) when (ReferenceEquals(error, rejectedWrite))
        { return new(DataScopeObjectStatus.InvalidInput, ValidationMessage: error.Message, ValidationError: error); }
    }

    // 规范身份决定去重与稳定锁序；来源验证属于owner，不能用payload对象身份替代它。
    private static DataScopeAuthorizationTarget[] OrderTargets(IReadOnlyList<DataScopeAuthorizationTarget> targets) =>
        targets.Distinct().OrderBy(target => target.ResourceKey, StringComparer.Ordinal)
            .ThenBy(target => target.ObjectId, StringComparer.Ordinal).ToArray();

    // 写入与未知结果核对共用锁序和全组资格，资格故障不进入配置错误翻译。
    private async Task<DataScopeObjectStatus> LockAndCheckTargetsAsync(DataScopeAuthorizationTarget[] targets, CancellationToken token,
        DataExecutionBindingReference? execution)
    {
        if (targets.Length == 0 || targets.Any(target => string.IsNullOrWhiteSpace(target.ResourceKey)
            || string.IsNullOrWhiteSpace(target.ObjectId) || target.ObjectId.Length > 128)) return DataScopeObjectStatus.InvalidInput;
        foreach (var target in targets) await grants.LockAsync(target.ResourceKey, target.ObjectId, token);
        foreach (var target in targets)
        {
            var eligibility = execution is null
                ? await CheckManagementActionAsync(target.ResourceKey, target.ObjectId, token)
                : await CheckDataAsync(target.ResourceKey, target.ObjectId, token, execution);
            if (eligibility != DataScopeObjectEligibility.Allowed) return DeniedStatus(eligibility);
        }
        return DataScopeObjectStatus.Succeeded;
    }
}
