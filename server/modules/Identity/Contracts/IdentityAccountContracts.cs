namespace ICP.Platform.Identity.Contracts;

/// <summary>
/// 用户账号的公开生命周期状态。
/// </summary>
public enum IdentityAccountStatus
{
    /// <summary>账号允许认证。</summary>
    Enabled,

    /// <summary>账号已停用。</summary>
    Disabled,

    /// <summary>账号已进入不可恢复终态。</summary>
    Deleted
}

/// <summary>
/// 供其他上下文安全引用的账号快照。
/// </summary>
public sealed record IdentityAccountSnapshot
{
    /// <summary>账号的稳定 EID。</summary>
    public required string UserEid { get; init; }

    /// <summary>不可修改的登录账号。</summary>
    public required string ECODE { get; init; }

    /// <summary>可修改的显示名称。</summary>
    public required string ENAME { get; init; }

    /// <summary>账号当前状态。</summary>
    public IdentityAccountStatus Status { get; init; }

    /// <summary>账号安全聚合的公开并发版本。</summary>
    public long Revision { get; init; }

    /// <summary>是否为受保护的本地管理员。</summary>
    public bool IsLocalAdministrator { get; init; }

    /// <summary>是否已经完成人员账号永久绑定。</summary>
    public bool IsPersonnelBound { get; init; }

    /// <summary>是否存在人员停用阻断。</summary>
    public bool PersonnelDisableHold { get; init; }
}

/// <summary>
/// 账号目录的受限查询条件。
/// </summary>
public sealed record IdentityAccountSearch
{
    /// <summary>按 ECODE 或 ENAME 匹配的可选文本。</summary>
    public string? Query { get; init; }

    /// <summary>可选的账号状态筛选。</summary>
    public IdentityAccountStatus? Status { get; init; }

    /// <summary>可选的人员绑定标记筛选。</summary>
    public bool? IsPersonnelBound { get; init; }

    /// <summary>调用方允许接收的最大结果数。</summary>
    public int Limit { get; init; } = 20;
}

/// <summary>
/// Identity 提供的窄账号目录读取 seam。
/// </summary>
public interface IIdentityAccountDirectory
{
    /// <summary>
    /// 按稳定 User EID 读取一个可供跨上下文使用的账号快照。
    /// </summary>
    ValueTask<IdentityAccountSnapshot?> FindAsync(string userEid, CancellationToken cancellationToken = default);

    /// <summary>
    /// 按受限条件搜索账号目录，不返回已删除账号，除非查询明确指定该状态。
    /// </summary>
    ValueTask<IReadOnlyList<IdentityAccountSnapshot>> SearchAsync(
        IdentityAccountSearch search,
        CancellationToken cancellationToken = default);
}
