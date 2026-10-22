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
    public required string AccountEid { get; init; }

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
    /// 按稳定账号 EID 读取一个可供跨上下文使用的账号快照。
    /// </summary>
    ValueTask<IdentityAccountSnapshot?> FindAsync(string accountEid, CancellationToken cancellationToken = default);

    /// <summary>
    /// 按稳定账号 EID 集合批量读取跨上下文公开账号快照；不返回已删除账号。
    /// </summary>
    ValueTask<IReadOnlyDictionary<string, IdentityAccountSnapshot>> FindManyAsync(
        IReadOnlyCollection<string> accountEids,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 按受限条件搜索账号目录，不返回已删除账号，除非查询明确指定该状态。
    /// </summary>
    ValueTask<IReadOnlyList<IdentityAccountSnapshot>> SearchAsync(
        IdentityAccountSearch search,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 本地凭据验证的稳定结果状态。
/// </summary>
public enum LocalCredentialAuthenticationStatus
{
    /// <summary>账号与凭据有效。</summary>
    Succeeded,

    /// <summary>账号或凭据无效；调用方不得区分具体原因。</summary>
    Invalid,

    /// <summary>账号或凭据存储当前不可用。</summary>
    StoreUnavailable
}

/// <summary>
/// 本地凭据验证结果；不包含凭据或密码哈希。
/// </summary>
public sealed record LocalCredentialAuthenticationResult
{
    /// <summary>验证状态。</summary>
    public LocalCredentialAuthenticationStatus Status { get; init; }

    /// <summary>成功时返回的账号快照。</summary>
    public IdentityAccountSnapshot? Account { get; init; }

    /// <summary>成功凭据是否仍为临时凭据。</summary>
    public bool IsTemporary { get; init; }

    /// <summary>存储故障时返回的稳定失败码。</summary>
    public string? FailureCode { get; init; }
}

/// <summary>
/// LocalAuth 向 Host 暴露的窄凭据验证 seam。
/// </summary>
public interface ILocalCredentialAuthenticator
{
    /// <summary>
    /// 使用 ECODE 与原始密码验证一个允许登录的账号。
    /// </summary>
    ValueTask<LocalCredentialAuthenticationResult> AuthenticateAsync(
        string accountCode,
        string password,
        CancellationToken cancellationToken = default);
}

/// <summary>LocalAuth 稳定失败码。</summary>
public static class LocalCredentialAuthenticationFailureCodes
{
    /// <summary>Identity 或凭据存储当前不可用。</summary>
    public const string StoreUnavailable = "AUTH_IDENTITY_STORE_UNAVAILABLE";
}
