namespace ICP.Platform.Persistence.Auditing;

/// <summary>
/// 在没有请求主体的后台和设计期流程中使用稳定系统身份生成审计值。
/// </summary>
public sealed class SystemEntityAuditStampProvider : IEntityAuditStampProvider
{
    /// <summary>
    /// 非请求保存操作统一使用的稳定主体标识。
    /// </summary>
    public const string SystemActorId = "system";

    private readonly TimeProvider _timeProvider;

    public SystemEntityAuditStampProvider(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public EntityAuditStamp GetCurrent() =>
        new(SystemActorId, _timeProvider.GetUtcNow().UtcDateTime);
}
