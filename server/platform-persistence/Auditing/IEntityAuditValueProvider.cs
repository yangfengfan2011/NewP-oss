namespace ICP.Platform.Persistence.Auditing;

/// <summary>
/// 为一次实际 Entity 写入生成并复用可信审计值。
/// </summary>
public interface IEntityAuditValueProvider
{
    /// <summary>
    /// 开始一次写入作用域；同一作用域内行为层与持久化层读取相同可信值。
    /// </summary>
    IDisposable BeginOperation();

    /// <summary>
    /// 取得当前写入作用域的可信主体和 UTC 时间；没有显式作用域时读取当前服务端来源。
    /// </summary>
    EntityAuditStamp Current();

    /// <summary>
    /// 生成创建所需的四个审计字段值。
    /// </summary>
    EntityAuditValues ForCreate();

    /// <summary>
    /// 保留创建信息并生成更新时间不早于创建时间的更新审计值。
    /// </summary>
    EntityAuditValues ForUpdate(string createdBy, DateTime createdAtUtc);
}
