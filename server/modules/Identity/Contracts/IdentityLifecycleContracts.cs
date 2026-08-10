namespace ICP.Platform.Identity.Contracts;

/// <summary>
/// 登记人员账号永久绑定所需的完整命令事实。
/// </summary>
public sealed record PersonnelBindingRegistration
{
    /// <summary>发起绑定的 Person EID。</summary>
    public required string PersonEid { get; init; }

    /// <summary>目标 User EID。</summary>
    public required string UserEid { get; init; }

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
    /// <summary>目标 User EID。</summary>
    public required string UserEid { get; init; }

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
    /// <summary>目标 User EID。</summary>
    public required string UserEid { get; init; }

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
