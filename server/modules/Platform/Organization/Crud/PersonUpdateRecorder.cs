using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Modules.Audit.Contracts;

namespace ICP.Platform.Modules.Organization.Crud;

/// <summary>人员普通编辑仅记录白名单实际变化；姓名为历史文本，关联对象保留稳定标识。</summary>
public sealed class PersonUpdateRecorder(IBusinessAuditRecorder recorder) : IStandardCrudUpdateRecorder
{
    public string ResourceKey => "organization.person";

    /// <inheritdoc/>
    public ValueTask RegisterAsync(IReadOnlyDictionary<string, object?> before,
        IReadOnlyDictionary<string, object?> after, CancellationToken cancellationToken = default)
    {
        var fields = new List<BusinessAuditField>();
        foreach (var field in new[] { "ENAME", "BirthDate", "CompanyId", "DepartmentId", "PositionId",
            "EmploymentStatus", "EmploymentStartedAt", "EmploymentEndedAt" })
        {
            var oldValue = Value(before, field);
            var newValue = Value(after, field);
            if (oldValue != newValue) fields.Add(new(field, oldValue, newValue));
        }
        return fields.Count == 0 ? ValueTask.CompletedTask
            : recorder.RegisterAsync(new("person.updated", ResourceKey, (string)after["EID"]!, fields), cancellationToken);
    }

    // 比较使用与持久化一致的规范类型，null 是值而不是缺少字段。
    private static BusinessAuditValue Value(IReadOnlyDictionary<string, object?> row, string field) => field switch
    {
        "CompanyId" or "DepartmentId" or "PositionId" => BusinessAuditValue.Reference(field[..^2].ToLowerInvariant(), row.GetValueOrDefault(field) as string),
        "BirthDate" => BusinessAuditValue.CalendarDate(row.GetValueOrDefault(field) as DateOnly?),
        "EmploymentStartedAt" or "EmploymentEndedAt" => BusinessAuditValue.Instant(row.GetValueOrDefault(field) as DateTime?),
        _ => BusinessAuditValue.Text(row.GetValueOrDefault(field) as string)
    };
}
