using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Modules.Audit.Contracts;
namespace ICP.Platform.Modules.Organization.Crud;
/// <summary>人员模块显式声明创建事件白名单，不随请求或 Model 新字段自动扩张。</summary>
public sealed class PersonCreationRecorder(IBusinessAuditRecorder recorder) : IStandardCrudCreationRecorder
{
    public string ResourceKey => "organization.person";
    /// <inheritdoc/>
    public ValueTask RegisterAsync(IReadOnlyDictionary<string, object?> created, CancellationToken cancellationToken = default)
    {
        var fields = new List<BusinessAuditField>();
        foreach (var field in new[] { "PersonNo", "ENAME", "EmploymentStatus" })
            fields.Add(new(field, null, BusinessAuditValue.Text(Convert.ToString(created.GetValueOrDefault(field)))));
        foreach (var field in new[] { "CompanyId", "DepartmentId", "PositionId" })
            fields.Add(new(field, null, BusinessAuditValue.Reference(field[..^2].ToLowerInvariant(), created.GetValueOrDefault(field) as string)));
        fields.Add(new("BirthDate", null, BusinessAuditValue.CalendarDate(created.GetValueOrDefault("BirthDate") as DateOnly?)));
        foreach (var field in new[] { "EmploymentStartedAt", "EmploymentEndedAt" })
            fields.Add(new(field, null, BusinessAuditValue.Instant(created.GetValueOrDefault(field) as DateTime?)));
        fields.Add(new("IsEnabled", null, BusinessAuditValue.Boolean((bool)created["IsEnabled"]!)));
        return recorder.RegisterAsync(new("person.created", ResourceKey, (string)created["EID"]!, fields), cancellationToken);
    }
}
