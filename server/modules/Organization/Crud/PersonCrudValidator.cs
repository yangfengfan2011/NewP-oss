using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Models;
using ICP.Platform.Modules.Organization.Repositories;

namespace ICP.Platform.Modules.Organization.Crud;

/// <summary>
/// 人员标准 CRUD 专属校验入口；PersonNo 全局唯一由 descriptor 的 StandardCrudUniqueConstraint 承接。
/// </summary>
public sealed class PersonCrudValidator(
    ICompanyRepository companyRepository,
    IDepartmentRepository departmentRepository,
    IPositionRepository positionRepository,
    IStandardCrudRepository standardCrudRepository) : IStandardCrudValidator
{
    public string ResourceKey => "organization.person";

    /// <summary>
    /// 人员可以暂不建立组织归属；一旦填写，则要求公司、部门成对出现、存在且启用，并校验部门归属公司。
    /// </summary>
    public Task<StandardCrudError?> ValidateCreateAsync(
        StandardCrudDescriptor descriptor,
        IReadOnlyDictionary<string, object?> values,
        CancellationToken cancellationToken = default) =>
        ValidateAffiliationAsync(values, cancellationToken);

    /// <summary>
    /// 更新阶段先与持久化现状合并，再校验可选的完整组织归属，避免局部更新绕过成对约束。
    /// </summary>
    public Task<StandardCrudError?> ValidateUpdateAsync(
        StandardCrudDescriptor descriptor,
        string id,
        IReadOnlyDictionary<string, object?> values,
        CancellationToken cancellationToken = default) =>
        ValidateUpdateCandidateAsync(descriptor, id, values, cancellationToken);

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

        return await ValidateAffiliationAsync(candidate, cancellationToken);
    }

    // 组织归属允许三者全空；一旦选择岗位，仍必须提供完整公司与部门并验证岗位归属。
    private async Task<StandardCrudError?> ValidateAffiliationAsync(
        IReadOnlyDictionary<string, object?> values,
        CancellationToken cancellationToken)
    {
        var companyId = ReadOptionalId(values, "CompanyId");
        var departmentId = ReadOptionalId(values, "DepartmentId");
        var positionId = ReadOptionalId(values, "PositionId");

        if (companyId is null && departmentId is null && positionId is null)
        {
            return null;
        }

        if (companyId is null)
        {
            return FieldError("CompanyId", "Organization.Person.CompanyRequired", "所属公司不能为空。", "organization.person.companyRequired");
        }

        if (departmentId is null)
        {
            return FieldError("DepartmentId", "Organization.Person.DepartmentRequired", "所属部门不能为空。", "organization.person.departmentRequired");
        }

        var company = await companyRepository.GetByIdAsync(companyId, cancellationToken);
        if (company is null)
        {
            return FieldError("CompanyId", "Organization.Person.CompanyNotFound", "所属公司不存在。", "organization.person.companyNotFound");
        }

        if (!company.IsEnabled)
        {
            return FieldError("CompanyId", "Organization.Person.CompanyDisabled", "所属公司已停用。", "organization.person.companyDisabled");
        }

        var department = await departmentRepository.GetByIdAsync(departmentId, cancellationToken);
        if (department is null)
        {
            return FieldError("DepartmentId", "Organization.Person.DepartmentNotFound", "所属部门不存在。", "organization.person.departmentNotFound");
        }

        if (!department.IsEnabled)
        {
            return FieldError("DepartmentId", "Organization.Person.DepartmentDisabled", "所属部门已停用。", "organization.person.departmentDisabled");
        }

        if (!string.Equals(department.CompanyId, company.Id, StringComparison.Ordinal))
        {
            return FieldError("DepartmentId", "Organization.Person.DepartmentCompanyMismatch", "所属部门不属于所选公司。", "organization.person.departmentCompanyMismatch");
        }

        if (positionId is null)
        {
            return null;
        }

        var position = await positionRepository.GetByIdAsync(positionId, cancellationToken);
        if (position is null)
        {
            return FieldError("PositionId", "Organization.Person.PositionNotFound", "岗位不存在。", "organization.person.positionNotFound");
        }

        if (!position.IsEnabled)
        {
            return FieldError("PositionId", "Organization.Person.PositionDisabled", "岗位已停用。", "organization.person.positionDisabled");
        }

        return !string.Equals(position.DepartmentId, department.Id, StringComparison.Ordinal)
            ? FieldError("PositionId", "Organization.Person.PositionDepartmentMismatch", "岗位不属于所选部门。", "organization.person.positionDepartmentMismatch")
            : null;
    }

    private static string? ReadOptionalId(IReadOnlyDictionary<string, object?> values, string field) =>
        values.TryGetValue(field, out var value) && value is string text && !string.IsNullOrWhiteSpace(text)
            ? text
            : null;

    private static StandardCrudError FieldError(string field, string code, string message, string reasonKey) => new()
    {
        StatusCode = 400,
        Code = code,
        Message = message,
        ReasonKey = reasonKey,
        FieldErrors = [new StandardCrudFieldError(field, code, message)]
    };
}
