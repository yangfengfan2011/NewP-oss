namespace ICP.Platform.HostApi.Readiness;

/// <summary>
/// 表示单个 Host 就绪阶段的稳定状态。
/// </summary>
public enum ReadinessState
{
    Starting,
    Ready,
    Failed,
    Indeterminate
}

/// <summary>
/// 定义 Host readiness 使用的稳定 failure code。
/// </summary>
public static class ReadinessFailureCodes
{
    public const string RequiredContributorsMissing = "HOST_READINESS_REQUIRED_CONTRIBUTORS_MISSING";
    public const string ContributorStarting = "HOST_READINESS_CONTRIBUTOR_STARTING";
    public const string ContributorFailed = "HOST_READINESS_CONTRIBUTOR_FAILED";
    public const string ContributorIndeterminate = "HOST_READINESS_CONTRIBUTOR_INDETERMINATE";
    public const string ContributorException = "HOST_READINESS_CONTRIBUTOR_EXCEPTION";
}

/// <summary>
/// 表示 contributor 的一次无副作用就绪观察。
/// </summary>
/// <param name="State">稳定状态。</param>
/// <param name="FailureCode">非 ready 时的稳定 failure code。</param>
/// <param name="Diagnostic">已由 contributor 脱敏的简短诊断。</param>
public readonly record struct ReadinessContribution(
    ReadinessState State,
    string? FailureCode,
    string? Diagnostic)
{
    public static ReadinessContribution Ready(string? diagnostic = null) =>
        new(ReadinessState.Ready, null, diagnostic);
}

/// <summary>
/// readiness 响应中的单阶段稳定 DTO。
/// </summary>
public sealed record ReadinessStageDto(
    string Name,
    string Status,
    bool Required,
    string? FailureCode,
    string? Diagnostic);

/// <summary>
/// 表示本地开发监督器用于验证端点所有权的稳定、非凭据身份。
/// </summary>
public sealed record DevSupervisorIdentityDto(
    string RepositoryId,
    string WorktreeId,
    string RunId,
    string EndpointTokenFingerprint);

/// <summary>
/// `/api/health/ready` 的稳定响应 DTO。
/// </summary>
public sealed record ReadinessResponseDto(
    string Status,
    string? FailureCode,
    IReadOnlyList<ReadinessStageDto> Stages,
    DevSupervisorIdentityDto? Supervisor = null);

/// <summary>
/// 表示一次 readiness 聚合结果。
/// </summary>
public sealed record ReadinessEvaluation(bool IsReady, ReadinessResponseDto Response);
