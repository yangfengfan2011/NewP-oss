namespace ICP.Platform.Persistence.Auditing;

/// <summary>
/// 提供 Entity 审计值解析边界的稳定失败码。
/// </summary>
public static class EntityAuditFailureCodes
{
    public const string StampUnavailable = "PERSISTENCE_ENTITY_AUDIT_STAMP_UNAVAILABLE";
}

/// <summary>
/// 表示可信 Entity audit owner 未能为当前保存提供有效审计值。
/// </summary>
public sealed class EntityAuditStampUnavailableException : InvalidOperationException
{
    /// <summary>
    /// 创建稳定的审计值缺失异常。
    /// </summary>
    /// <param name="innerException">可信 owner 返回无效值时的原始异常。</param>
    public EntityAuditStampUnavailableException(Exception? innerException = null)
        : base(
            $"Entity audit stamp is unavailable ({EntityAuditFailureCodes.StampUnavailable}).",
            innerException)
    {
    }

    /// <summary>
    /// 获取供平台边界映射的稳定失败码。
    /// </summary>
    public string FailureCode => EntityAuditFailureCodes.StampUnavailable;
}
