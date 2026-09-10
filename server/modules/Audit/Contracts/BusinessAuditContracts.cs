using System.Globalization;
namespace ICP.Platform.Modules.Audit.Contracts;

/// <summary>明确区分空值、日历日期、UTC 时间点及标识的业务原始值。</summary>
public sealed record BusinessAuditValue(string Kind, string? Value)
{
    /// <summary>保留原始文本及显式空值。</summary>
    public static BusinessAuditValue Text(string? value) => new("text", value);
    /// <summary>保留稳定对象标识。</summary>
    public static BusinessAuditValue Reference(string kind, string? value) => new(kind, value);
    /// <summary>保留规范布尔值。</summary>
    public static BusinessAuditValue Boolean(bool value) => new("boolean", value ? "true" : "false");
    /// <summary>不应用时区转换，保留日历日期。</summary>
    public static BusinessAuditValue CalendarDate(DateOnly? value) => new("calendarDate", value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
    /// <summary>只接受 UTC 时间点，空值仍显式存在。</summary>
    public static BusinessAuditValue Instant(DateTime? value)
    {
        if (value is { Kind: not DateTimeKind.Utc }) throw new ArgumentException("Business audit instants must be UTC.", nameof(value));
        return new("instant", value?.ToString("O", CultureInfo.InvariantCulture));
    }
}

/// <summary>一次显式声明的字段事实；不存在旧值表示创建，值对象中的 null 表示业务空值。</summary>
public sealed record BusinessAuditField(string Field, BusinessAuditValue? Before, BusinessAuditValue After);
/// <summary>模块登记的业务事实，不包含客户端主体、时间或任意请求载荷。</summary>
public sealed record BusinessAuditChange(string EventType, string Resource, string TargetId, IReadOnlyList<BusinessAuditField> Fields);
/// <summary>可信模块在平台事务中登记业务变化；此接口不保存或提交。</summary>
public interface IBusinessAuditRecorder
{
    /// <summary>登记已规范化事实；调用方必须持有共享平台事务。</summary>
    ValueTask RegisterAsync(BusinessAuditChange change, CancellationToken cancellationToken = default);
}
/// <summary>关联对象拥有者批量解析当前名称；结果不改变稳定标识事实。</summary>
public interface IBusinessAuditNameDirectory
{
    string Kind { get; }
    /// <summary>只返回本批次可解析的当前名称，缺失项由展示方保留 ID。</summary>
    Task<IReadOnlyDictionary<string, string>> FindManyAsync(IReadOnlyCollection<string> ids, CancellationToken cancellationToken = default);
}
