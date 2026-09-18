namespace ICP.Platform.Persistence.Auditing;

/// <summary>
/// 把可信主体和 UTC 时间收敛为 Entity 审计字段的唯一赋值规则。
/// </summary>
/// <param name="stampProvider">当前运行环境提供的可信主体与时间来源。</param>
public sealed class EntityAuditValueProvider(IEntityAuditStampProvider stampProvider) : IEntityAuditValueProvider
{
    private readonly AsyncLocal<OperationScope?> _current = new();

    /// <inheritdoc />
    public IDisposable BeginOperation()
    {
        var parent = _current.Value;
        var scope = new OperationScope(this, parent, ResolveStamp());
        _current.Value = scope;
        return scope;
    }

    /// <inheritdoc />
    public EntityAuditStamp Current() => CurrentStamp();

    /// <inheritdoc />
    public EntityAuditValues ForCreate()
    {
        var stamp = CurrentStamp();
        return new EntityAuditValues(
            stamp.ActorId,
            stamp.OccurredAtUtc,
            stamp.ActorId,
            stamp.OccurredAtUtc);
    }

    /// <inheritdoc />
    public EntityAuditValues ForUpdate(string createdBy, DateTime createdAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(createdBy);
        createdAtUtc = createdAtUtc.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(createdAtUtc, DateTimeKind.Utc)
            : createdAtUtc.ToUniversalTime();

        var stamp = CurrentStamp();
        return new EntityAuditValues(
            createdBy,
            createdAtUtc,
            stamp.ActorId,
            stamp.OccurredAtUtc < createdAtUtc ? createdAtUtc : stamp.OccurredAtUtc);
    }

    // operation scope 内复用同一 stamp；未迁移保存仍在每次调用时取得新值。
    private EntityAuditStamp CurrentStamp() => _current.Value?.Stamp ?? ResolveStamp();

    // 统一把无值或无效可信来源转换为稳定平台失败。
    private EntityAuditStamp ResolveStamp()
    {
        try
        {
            return stampProvider.GetCurrent() ?? throw new EntityAuditStampUnavailableException();
        }
        catch (EntityAuditStampUnavailableException)
        {
            throw;
        }
        catch (ArgumentException exception)
        {
            throw new EntityAuditStampUnavailableException(exception);
        }
    }

    private sealed class OperationScope(
        EntityAuditValueProvider owner,
        OperationScope? parent,
        EntityAuditStamp stamp) : IDisposable
    {
        private bool _disposed;

        internal EntityAuditStamp Stamp { get; } = stamp;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            if (!ReferenceEquals(owner._current.Value, this))
            {
                throw new InvalidOperationException("Entity audit operation scopes must be disposed in reverse order.");
            }

            owner._current.Value = parent;
            _disposed = true;
        }
    }
}
