namespace ICP.Platform.Identity.Contracts;

/// <summary>
/// 登记人员账号永久绑定所需的完整命令事实。
/// </summary>
public sealed record PersonnelBindingRegistration
{
    /// <summary>发起绑定的 Person EID。</summary>
    public required string PersonEid { get; init; }

    /// <summary>目标账号 EID。</summary>
    public required string AccountEid { get; init; }

    /// <summary>调用方观察到的账号 Revision。</summary>
    public long ExpectedRevision { get; init; }

    /// <summary>经校验的操作原因。</summary>
    public required string Reason { get; init; }

    /// <summary>服务端解析的非空操作者标识。</summary>
    public required string ActorId { get; init; }

    /// <summary>用于关联诊断的 TraceId。</summary>
    public required string TraceId { get; init; }
}

/// <summary>
/// 登记人员停用引发的账号变化所需事实。
/// </summary>
public sealed record PersonnelAccountDisableRegistration
{
    /// <summary>目标账号 EID。</summary>
    public required string AccountEid { get; init; }

    /// <summary>调用方观察到的账号 Revision。</summary>
    public long ExpectedRevision { get; init; }

    /// <summary>经校验的操作原因。</summary>
    public required string Reason { get; init; }

    /// <summary>服务端解析的非空操作者标识。</summary>
    public required string ActorId { get; init; }

    /// <summary>用于关联诊断的 TraceId。</summary>
    public required string TraceId { get; init; }
}

/// <summary>
/// 登记人员恢复引发的阻断解除所需事实。
/// </summary>
public sealed record PersonnelDisableHoldReleaseRegistration
{
    /// <summary>目标账号 EID。</summary>
    public required string AccountEid { get; init; }

    /// <summary>调用方观察到的账号 Revision。</summary>
    public long ExpectedRevision { get; init; }

    /// <summary>服务端解析的非空操作者标识。</summary>
    public required string ActorId { get; init; }

    /// <summary>用于关联诊断的 TraceId。</summary>
    public required string TraceId { get; init; }
}

/// <summary>
/// Identity 生命周期参与者登记变更后的安全结果。
/// </summary>
public sealed record IdentityLifecycleRegistrationResult
{
    /// <summary>登记完成后的账号快照。</summary>
    public required IdentityAccountSnapshot Account { get; init; }

    /// <summary>本次登记是否产生了状态变化。</summary>
    public bool Changed { get; init; }
}

/// <summary>
/// 跨模块生命周期登记可观察的稳定失败码。
/// </summary>
public static class IdentityLifecycleFailureCodes
{
    /// <summary>账号不存在。</summary>
    public const string AccountNotFound = "IDENTITY_ACCOUNT_NOT_FOUND";

    /// <summary>账号已删除。</summary>
    public const string DeletedAccount = "IDENTITY_ACCOUNT_DELETED";

    /// <summary>本地管理员账号不可参与人员生命周期。</summary>
    public const string LocalAdministratorProtected = "IDENTITY_LOCAL_ADMIN_PROTECTED";

    /// <summary>账号 Revision 已变化。</summary>
    public const string RevisionConflict = "IDENTITY_REVISION_CONFLICT";
}

/// <summary>
/// Identity 生命周期参与者向跨模块调用方暴露的稳定失败，不泄露 Identity 应用层实现类型。
/// </summary>
public sealed class IdentityLifecycleException : InvalidOperationException
{
    /// <summary>创建生命周期登记失败。</summary>
    public IdentityLifecycleException(
        string failureCode,
        IdentityAccountSnapshot? latestAccount = null,
        Exception? innerException = null)
        : base(failureCode, innerException)
    {
        FailureCode = failureCode;
        LatestAccount = latestAccount;
    }

    /// <summary>稳定失败码。</summary>
    public string FailureCode { get; }

    /// <summary>发生并发冲突时可返回的最新账号快照。</summary>
    public IdentityAccountSnapshot? LatestAccount { get; }
}

/// <summary>
/// 供 Organization 在共享事务内登记 Identity 生命周期变化的深命令 seam。
/// 该接口不保存事务，也不暴露仓储或 EF 实体。
/// </summary>
public interface IIdentityLifecycleParticipant
{
    /// <summary>
    /// 校验并登记人员账号永久绑定变化，实际提交由平台事务执行器统一完成。
    /// </summary>
    ValueTask<IdentityLifecycleRegistrationResult> RegisterPersonnelBindingAsync(
        PersonnelBindingRegistration registration,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 校验并登记人员停用引发的账号停用与阻断变化。
    /// </summary>
    ValueTask<IdentityLifecycleRegistrationResult> RegisterPersonnelDisableAsync(
        PersonnelAccountDisableRegistration registration,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 校验并登记人员恢复引发的停用阻断解除，不自动恢复账号。
    /// </summary>
    ValueTask<IdentityLifecycleRegistrationResult> RegisterPersonnelDisableHoldReleaseAsync(
        PersonnelDisableHoldReleaseRegistration registration,
        CancellationToken cancellationToken = default);
}
