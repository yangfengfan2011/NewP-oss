using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Models;
using ICP.Platform.Modules.Organization.Directories;
using ICP.Platform.Presentation;

namespace ICP.Platform.Modules.Organization.Crud;

/// <summary>
/// 人员标准 CRUD 专属校验入口；PersonNo 全局唯一由 descriptor 的 StandardCrudUniqueConstraint 承接。
/// </summary>
/// <param name="companyDirectory">公司归属事实的窄读取入口。</param>
/// <param name="departmentDirectory">部门归属事实的窄读取入口。</param>
/// <param name="positionDirectory">与公司、部门独立的全局岗位读取目录。</param>
/// <param name="standardCrudRepository">更新时读取当前人员候选值的标准仓储。</param>
/// <param name="temporalInputNormalizer">平台 temporal 规范化器，用于按会话本地日期校验生日。</param>
public sealed class PersonCrudValidator(
    ICompanyDirectory companyDirectory,
    IDepartmentDirectory departmentDirectory,
    IPositionDirectory positionDirectory,
    IStandardCrudRepository standardCrudRepository,
    ITemporalInputNormalizer? temporalInputNormalizer = null) : IStandardCrudValidator
{
    public string ResourceKey => "organization.person";

    /// <summary>
    /// 人员可以暂不建立组织归属；创建时一旦填写，则要求公司、部门成对出现、存在且启用，并校验部门归属公司。
    /// </summary>
    public Task<StandardCrudError?> ValidateCreateAsync(
        StandardCrudDescriptor descriptor,
        IReadOnlyDictionary<string, object?> values,
        CancellationToken cancellationToken = default) =>
        ValidateCandidateAsync(
            values,
            requireEnabledAffiliation: true,
            requireEnabledPosition: true,
            cancellationToken);

    /// <summary>
    /// 更新阶段先与持久化现状合并；未改变归属保留停用引用，清空或更换时重新执行完整校验。
    /// </summary>
    public Task<StandardCrudError?> ValidateUpdateAsync(
        StandardCrudDescriptor descriptor,
        string id,
        IReadOnlyDictionary<string, object?> values,
        CancellationToken cancellationToken = default) =>
        ValidateUpdateCandidateAsync(descriptor, id, values, cancellationToken);

    // 先合并局部更新与现状，再以归属对是否变化决定是否重新施加启用约束。
    private async Task<StandardCrudError?> ValidateUpdateCandidateAsync(
        StandardCrudDescriptor descriptor,
        string id,
        IReadOnlyDictionary<string, object?> values,
        CancellationToken cancellationToken)
    {
        var existing = await standardCrudRepository.GetByIdAsync(descriptor, id, cancellationToken);
        if (existing is null)
        {
            // 标准 CRUD 内核在调用校验器前已处理不存在；这里只保留竞态下的无副作用退出。
            return null;
        }

        var candidate = new Dictionary<string, object?>(existing, StringComparer.Ordinal);
        foreach (var pair in values)
        {
            candidate[pair.Key] = pair.Value;
        }

        var existingPositionId = ReadOptionalId(existing, "PositionId");
        var candidatePositionId = ReadOptionalId(candidate, "PositionId");
        var positionChanged = values.ContainsKey("PositionId")
            && !string.Equals(existingPositionId, candidatePositionId, StringComparison.Ordinal);
        var existingCompanyId = ReadOptionalId(existing, "CompanyId");
        var candidateCompanyId = ReadOptionalId(candidate, "CompanyId");
        var existingDepartmentId = ReadOptionalId(existing, "DepartmentId");
        var candidateDepartmentId = ReadOptionalId(candidate, "DepartmentId");
        var affiliationChanged = !string.Equals(existingCompanyId, candidateCompanyId, StringComparison.Ordinal)
            || !string.Equals(existingDepartmentId, candidateDepartmentId, StringComparison.Ordinal);
        return await ValidateCandidateAsync(
            candidate,
            affiliationChanged,
            positionChanged,
            cancellationToken);
    }

    // 组织归属和岗位是两个独立事实；二者分别校验，不能因另一方为空而增加隐式依赖。
    private async Task<StandardCrudError?> ValidateCandidateAsync(
        IReadOnlyDictionary<string, object?> values,
        bool requireEnabledAffiliation,
        bool requireEnabledPosition,
        CancellationToken cancellationToken)
    {
        var temporalError = ValidateTemporalFields(values);
        if (temporalError is not null)
        {
            return temporalError;
        }

        var companyId = ReadOptionalId(values, "CompanyId");
        var departmentId = ReadOptionalId(values, "DepartmentId");
        var positionId = ReadOptionalId(values, "PositionId");

        if (companyId is null != (departmentId is null))
        {
            return companyId is null
                ? FieldError("CompanyId", "Organization.Person.CompanyRequired", "所属公司不能为空。", "organization.person.companyRequired")
                : FieldError("DepartmentId", "Organization.Person.DepartmentRequired", "所属部门不能为空。", "organization.person.departmentRequired");
        }

        if (companyId is not null && departmentId is not null)
        {
            var company = await companyDirectory.GetByIdAsync(companyId, cancellationToken);
            if (company is null)
            {
                return FieldError("CompanyId", "Organization.Person.CompanyNotFound", "所属公司不存在。", "organization.person.companyNotFound");
            }

            if (requireEnabledAffiliation && !company.IsEnabled)
            {
                return FieldError("CompanyId", "Organization.Person.CompanyDisabled", "所属公司已停用。", "organization.person.companyDisabled");
            }

            var department = await departmentDirectory.GetByIdAsync(departmentId, cancellationToken);
            if (department is null)
            {
                return FieldError("DepartmentId", "Organization.Person.DepartmentNotFound", "所属部门不存在。", "organization.person.departmentNotFound");
            }

            if (requireEnabledAffiliation && !department.IsEnabled)
            {
                return FieldError("DepartmentId", "Organization.Person.DepartmentDisabled", "所属部门已停用。", "organization.person.departmentDisabled");
            }

            if (!string.Equals(department.CompanyId, company.Id, StringComparison.Ordinal))
            {
                return FieldError("DepartmentId", "Organization.Person.DepartmentCompanyMismatch", "所属部门不属于所选公司。", "organization.person.departmentCompanyMismatch");
            }
        }

        if (positionId is null)
        {
            return null;
        }

        var position = await positionDirectory.GetByIdAsync(positionId, cancellationToken);
        if (position is null)
        {
            return FieldError("PositionId", "Organization.Person.PositionNotFound", "岗位不存在。", "organization.person.positionNotFound");
        }

        if (requireEnabledPosition && !position.IsEnabled)
        {
            return FieldError("PositionId", "Organization.Person.PositionDisabled", "岗位已停用。", "organization.person.positionDisabled");
        }

        return null;
    }

    private StandardCrudError? ValidateTemporalFields(IReadOnlyDictionary<string, object?> values)
    {
        if (values.TryGetValue("BirthDate", out var birthValue)
            && birthValue is DateOnly birthDate)
        {
            var today = temporalInputNormalizer?.GetCurrentLocalDate()
                ?? DateOnly.FromDateTime(DateTime.UtcNow);
            if (birthDate > today)
            {
                return FieldError(
                    "BirthDate",
                    "Organization.Person.BirthDateInFuture",
                    "出生日期不能晚于当前日期。",
                    "organization.person.birthDateInFuture");
            }
        }

        var startedAt = ReadOptionalInstant(values, "EmploymentStartedAt");
        var endedAt = ReadOptionalInstant(values, "EmploymentEndedAt");
        return startedAt.HasValue && endedAt.HasValue && endedAt.Value < startedAt.Value
            ? FieldError(
                "EmploymentEndedAt",
                "Organization.Person.EmploymentIntervalInvalid",
                "任职结束时间不能早于任职开始时间。",
                "organization.person.employmentIntervalInvalid")
            : null;
    }

    private static string? ReadOptionalId(IReadOnlyDictionary<string, object?> values, string field) =>
        values.TryGetValue(field, out var value) && value is string text && !string.IsNullOrWhiteSpace(text)
            ? text
            : null;

    private static DateTime? ReadOptionalInstant(IReadOnlyDictionary<string, object?> values, string field) =>
        values.TryGetValue(field, out var value) && value is DateTime instant ? instant : null;

    private static StandardCrudError FieldError(string field, string code, string message, string reasonKey) => new()
    {
        StatusCode = 400,
        Code = code,
        Message = message,
        ReasonKey = reasonKey,
        FieldErrors = [new StandardCrudFieldError(field, code, message)]
    };
}
