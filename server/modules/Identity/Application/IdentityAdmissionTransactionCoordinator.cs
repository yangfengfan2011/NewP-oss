using ICP.Platform.Identity.Contracts;
using ICP.Platform.Persistence.Abstractions;

namespace ICP.Platform.Modules.Identity.Application;

/// <summary>统一持有 Identity 安全用例与普通账号准入变更之间的事务排序。</summary>
internal sealed class IdentityAdmissionTransactionCoordinator
{
    private readonly IPlatformTransactionExecutor _transactionExecutor;
    private readonly IIdentityAdmissionPolicy[] _admissionPolicies;

    // 准入拥有者只声明协调模型；业务步骤仍由各自的具名用例持有。
    internal IdentityAdmissionTransactionCoordinator(
        IPlatformTransactionExecutor transactionExecutor,
        IEnumerable<IIdentityAdmissionPolicy>? admissionPolicies = null)
    {
        _transactionExecutor = transactionExecutor;
        _admissionPolicies = admissionPolicies?.ToArray() ?? [];
    }

    // 在业务步骤开始前一次性取得所有准入参与者的协调权，并由平台统一提交或回滚。
    internal ValueTask<TResult> ExecuteAsync<TResult>(
        IdentityAccountSnapshot account,
        Func<CancellationToken, ValueTask<TResult>> executeUseCase,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(executeUseCase);
        var modelKeys = _admissionPolicies
            .SelectMany(policy => policy.GetCoordinationModels(account))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return _transactionExecutor.ExecuteAsync(modelKeys, executeUseCase, cancellationToken);
    }
}
