using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Models;
using ICP.Platform.Modules.Organization.Repositories;

namespace ICP.Platform.Modules.Organization.Crud;

public sealed class PersonCrudValidator : IStandardCrudValidator
{
    private readonly IPersonRepository _repository;

    public PersonCrudValidator(IPersonRepository repository)
    {
        _repository = repository;
    }

    public string ResourceKey => "organization.person";

    /// <summary>
    /// 创建校验只承接人员资源的业务约束；字段裁剪与执行顺序由标准 CRUD 内核负责。
    /// </summary>
    public async Task<StandardCrudError?> ValidateCreateAsync(
        StandardCrudDescriptor descriptor,
        IReadOnlyDictionary<string, object?> values,
        CancellationToken cancellationToken = default)
    {
        var companyId = ReadGuid(values, "CompanyId");
        var personNo = values.TryGetValue("PersonNo", out var value)
            ? value?.ToString() ?? string.Empty
            : string.Empty;

        if (await _repository.ExistsPersonNoAsync(companyId, personNo, null, cancellationToken))
        {
            return Conflict("Organization.Person.PersonNoDuplicated", "同一公司下人员工号必须唯一。");
        }

        return null;
    }

    /// <summary>
    /// 更新校验保留人员资源自身规则；PersonNo 默认由 descriptor 更新白名单阻断。
    /// </summary>
    public async Task<StandardCrudError?> ValidateUpdateAsync(
        StandardCrudDescriptor descriptor,
        string id,
        IReadOnlyDictionary<string, object?> values,
        CancellationToken cancellationToken = default)
    {
        if (values.ContainsKey("PersonNo"))
        {
            return Conflict("Organization.Person.PersonNoReadonly", "人员工号不允许通过标准更新接口修改。");
        }

        await Task.CompletedTask;
        return null;
    }

    private static Guid? ReadGuid(IReadOnlyDictionary<string, object?> values, string field) =>
        values.TryGetValue(field, out var value) && Guid.TryParse(value?.ToString(), out var guid) ? guid : null;

    private static StandardCrudError Conflict(string code, string message) => new()
    {
        StatusCode = 409,
        Code = code,
        Message = message,
        ReasonKey = code
    };
}
