using ICP.Platform.Authorization;
using ICP.Platform.Persistence.Auditing;

namespace ICP.Platform.HostApi.Persistence;

/// <summary>
/// 将 Host 已认证主体适配为持久化审计主体；非请求流程使用稳定系统身份。
/// </summary>
public sealed class HttpContextEntityAuditStampProvider : IEntityAuditStampProvider
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly TimeProvider _timeProvider;

    public HttpContextEntityAuditStampProvider(
        IHttpContextAccessor httpContextAccessor,
        TimeProvider timeProvider)
    {
        _httpContextAccessor = httpContextAccessor;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public EntityAuditStamp GetCurrent()
    {
        var principal = _httpContextAccessor.HttpContext?.User;
        var actorId = principal?.Identity?.IsAuthenticated == true
            ? AuthenticatedSubject.FromPrincipal(principal).SubjectId
            : SystemEntityAuditStampProvider.SystemActorId;

        return new EntityAuditStamp(actorId, _timeProvider.GetUtcNow().UtcDateTime);
    }
}
