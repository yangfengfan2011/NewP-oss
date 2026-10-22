namespace ICP.Platform.Persistence.Auditing;

/// <summary>
/// 表示一次 Entity 保存操作使用的可信审计值。
/// </summary>
public sealed record EntityAuditStamp
{
    /// <summary>
    /// 创建审计值，并要求调用方提供非空主体和 UTC 时间。
    /// </summary>
    public EntityAuditStamp(string actorId, DateTime occurredAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorId);
        if (occurredAtUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Entity audit timestamp must be UTC.", nameof(occurredAtUtc));
        }

        ActorId = actorId;
        OccurredAtUtc = occurredAtUtc;
    }

    /// <summary>
    /// 获取执行当前保存操作的可信主体标识。
    /// </summary>
    public string ActorId { get; }

    /// <summary>
    /// 获取当前保存操作的 UTC 时间。
    /// </summary>
    public DateTime OccurredAtUtc { get; }
}
