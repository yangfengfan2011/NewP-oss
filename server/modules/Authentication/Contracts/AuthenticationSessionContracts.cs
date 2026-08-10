namespace ICP.Platform.Authentication.Contracts;

/// <summary>
/// 认证会话的受支持类型。
/// </summary>
public enum AuthenticationSessionType
{
    /// <summary>允许访问普通受保护入口的会话。</summary>
    Regular,

    /// <summary>只允许首次改密白名单入口的会话。</summary>
    Restricted
}

/// <summary>
/// 会话注册表对凭据摘要的验证结论。
/// </summary>
public enum AuthenticationSessionValidationStatus
{
    /// <summary>会话存在且当前有效。</summary>
    Active,

    /// <summary>注册表中不存在该摘要。</summary>
    Unknown,

    /// <summary>会话已经撤销。</summary>
    Revoked,

    /// <summary>会话已经超过固定有效期。</summary>
    Expired
}

/// <summary>
/// 新会话写入注册表所需的非秘密事实。
/// </summary>
public sealed record AuthenticationSessionRegistration
{
    /// <summary>会话所属 User EID。</summary>
    public required string UserEid { get; init; }

    /// <summary>不可逆的会话凭据摘要，不是客户端持有的原始令牌。</summary>
    public required string CredentialDigest { get; init; }

    /// <summary>会话类型。</summary>
    public AuthenticationSessionType Type { get; init; }

    /// <summary>固定过期 UTC 时间。</summary>
    public DateTime ExpiresAtUtc { get; init; }

    /// <summary>服务端解析的非空操作者标识。</summary>
    public required string ActorId { get; init; }
}

/// <summary>
/// 不包含会话凭据的注册表快照。
/// </summary>
public sealed record AuthenticationSessionSnapshot
{
    /// <summary>认证会话 EID。</summary>
    public required string SessionEid { get; init; }

    /// <summary>会话所属 User EID。</summary>
    public required string UserEid { get; init; }

    /// <summary>会话类型。</summary>
    public AuthenticationSessionType Type { get; init; }

    /// <summary>固定过期 UTC 时间。</summary>
    public DateTime ExpiresAtUtc { get; init; }

    /// <summary>可选的撤销 UTC 时间。</summary>
    public DateTime? RevokedAtUtc { get; init; }

    /// <summary>可选的稳定撤销原因。</summary>
    public string? RevocationReason { get; init; }
}

/// <summary>
/// 会话凭据摘要的验证结果。
/// </summary>
public sealed record AuthenticationSessionValidation
{
    /// <summary>验证状态。</summary>
    public AuthenticationSessionValidationStatus Status { get; init; }

    /// <summary>命中注册表时返回的不含凭据快照。</summary>
    public AuthenticationSessionSnapshot? Session { get; init; }
}

/// <summary>
/// 撤销一个会话所需的事实。
/// </summary>
public sealed record AuthenticationSessionRevocation
{
    /// <summary>目标会话 EID。</summary>
    public required string SessionEid { get; init; }

    /// <summary>稳定撤销原因。</summary>
    public required string Reason { get; init; }

    /// <summary>服务端解析的非空操作者标识。</summary>
    public required string ActorId { get; init; }
}

/// <summary>
/// 撤销一个账号全部会话所需的事实。
/// </summary>
public sealed record UserAuthenticationSessionsRevocation
{
    /// <summary>目标 User EID。</summary>
    public required string UserEid { get; init; }

    /// <summary>稳定撤销原因。</summary>
    public required string Reason { get; init; }

    /// <summary>服务端解析的非空操作者标识。</summary>
    public required string ActorId { get; init; }
}

/// <summary>
/// Authentication 提供的深会话注册表 seam。
/// 写方法只登记共享事务中的变化，不自行保存。
/// </summary>
public interface IAuthenticationSessionRegistry
{
    /// <summary>
    /// 登记一个使用不可逆凭据摘要的新认证会话。
    /// </summary>
    ValueTask<AuthenticationSessionSnapshot> RegisterAsync(
        AuthenticationSessionRegistration registration,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 使用不可逆凭据摘要读取当前会话有效性。
    /// </summary>
    ValueTask<AuthenticationSessionValidation> ValidateAsync(
        string credentialDigest,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 登记当前认证会话的撤销变化。
    /// </summary>
    ValueTask<bool> RevokeCurrentAsync(
        AuthenticationSessionRevocation revocation,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 登记目标账号全部认证会话的撤销变化。
    /// </summary>
    ValueTask<int> RevokeAllAsync(
        UserAuthenticationSessionsRevocation revocation,
        CancellationToken cancellationToken = default);
}
