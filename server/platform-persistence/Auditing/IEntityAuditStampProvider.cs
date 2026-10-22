namespace ICP.Platform.Persistence.Auditing;

/// <summary>
/// 为持久化内核提供当前 Entity 保存操作的可信主体和时间。
/// </summary>
public interface IEntityAuditStampProvider
{
    /// <summary>
    /// 获取当前保存操作统一使用的审计值。
    /// </summary>
    EntityAuditStamp GetCurrent();
}
