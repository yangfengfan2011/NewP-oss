namespace ICP.Platform.Persistence.Auditing;

/// <summary>
/// 一次实际 Entity 写入最终使用的四个审计字段值。
/// </summary>
/// <param name="CreatedBy">创建主体。</param>
/// <param name="CreatedAt">UTC 创建时间。</param>
/// <param name="UpdatedBy">最后修改主体。</param>
/// <param name="UpdatedAt">不早于创建时间的 UTC 修改时间。</param>
public sealed record EntityAuditValues(
    string CreatedBy,
    DateTime CreatedAt,
    string UpdatedBy,
    DateTime UpdatedAt)
{
    /// <summary>
    /// 将最终可信值写入动态 Entity 字段集合。
    /// </summary>
    public void ApplyTo(IDictionary<string, object?> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        values["CreatedBy"] = CreatedBy;
        values["CreatedAt"] = CreatedAt;
        values["UpdatedBy"] = UpdatedBy;
        values["UpdatedAt"] = UpdatedAt;
    }

    /// <summary>
    /// 仅写入对象元数据声明由服务端拥有的审计字段。
    /// </summary>
    public void ApplyTo(IDictionary<string, object?> values, IEnumerable<string> fields)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(fields);
        foreach (var field in fields)
        {
            values[field] = field switch
            {
                "CreatedBy" => CreatedBy,
                "CreatedAt" => CreatedAt,
                "UpdatedBy" => UpdatedBy,
                "UpdatedAt" => UpdatedAt,
                _ => throw new InvalidOperationException($"Unsupported Entity audit field '{field}'.")
            };
        }
    }
}
