using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Models;
using ICP.Platform.Persistence.EfCore;
using Microsoft.EntityFrameworkCore;

namespace ICP.Platform.Modules.Organization.Crud;

public sealed class DepartmentCrudValidator(EfPlatformDbContext dbContext) : IStandardCrudValidator
{
    public string ResourceKey => "organization.department";

    /// <summary>
    /// 校验根部门创建时的字符串排序码，并要求所属公司存在且启用。
    /// </summary>
    public async Task<StandardCrudError?> ValidateCreateAsync(
        StandardCrudDescriptor descriptor,
        IReadOnlyDictionary<string, object?> values,
        CancellationToken cancellationToken = default)
    {
        _ = descriptor;
        var sortOrderError = ValidateSortOrder(values);
        if (sortOrderError is not null)
        {
            return sortOrderError;
        }

        var companyId = values.GetValueOrDefault("CompanyId")?.ToString();
        if (string.IsNullOrWhiteSpace(companyId))
        {
            return null;
        }

        var companyState = await Companies()
            .AsNoTracking()
            .Where(company => EF.Property<string>(company, "EID") == companyId)
            .Select(company => new { IsEnabled = EF.Property<bool>(company, "IsEnabled") })
            .SingleOrDefaultAsync(cancellationToken);
        return companyState is null
            ? CompanyError(404, "CompanyNotFound", "所属公司不存在。")
            : companyState.IsEnabled
                ? null
                : CompanyError(409, "CompanyDisabled", "所属公司已停用，不能新建部门。");
    }

    /// <summary>
    /// 校验根部门普通编辑中的字符串排序码；本阶段没有子部门停用守卫。
    /// </summary>
    public Task<StandardCrudError?> ValidateUpdateAsync(
        StandardCrudDescriptor descriptor,
        string id,
        IReadOnlyDictionary<string, object?> values,
        CancellationToken cancellationToken = default)
    {
        _ = descriptor;
        _ = id;
        _ = cancellationToken;
        return Task.FromResult(ValidateSortOrder(values));
    }

    // 排序码必须原样由一至三十二个 ASCII 数字组成，不能接受 Unicode 数字或隐式 trim。
    private static StandardCrudError? ValidateSortOrder(IReadOnlyDictionary<string, object?> values)
    {
        if (!values.TryGetValue("SortOrder", out var value))
        {
            return null;
        }

        var sortOrder = value as string;
        return sortOrder is { Length: >= 1 and <= 32 }
            && sortOrder.All(static character => character is >= '0' and <= '9')
                ? null
                : SortOrderError();
    }

    // 公司状态从 Organization 持久化边界直接读取，不恢复旧 Department service。
    private DbSet<Dictionary<string, object?>> Companies() =>
        dbContext.Set<Dictionary<string, object?>>(dbContext.ResolveModelKey("Company"));

    // 排序码格式错误绑定到 SortOrder，供标准 Drawer 精确展示。
    private static StandardCrudError SortOrderError() => new()
    {
        StatusCode = 400,
        Code = "Organization.Department.SortOrderInvalid",
        Message = "排序码必须由 1 至 32 位 ASCII 数字组成。",
        ReasonKey = "organization.department.sortOrderInvalid",
        FieldErrors =
        [
            new StandardCrudFieldError(
                "SortOrder",
                "Organization.Department.SortOrderInvalid",
                "排序码必须由 1 至 32 位 ASCII 数字组成。")
        ]
    };

    // 公司引用失败保持资源稳定码，并把错误精确绑定到 CompanyId。
    private static StandardCrudError CompanyError(int statusCode, string codeStem, string message) => new()
    {
        StatusCode = statusCode,
        Code = $"Organization.Department.{codeStem}",
        Message = message,
        ReasonKey = $"organization.department.{char.ToLowerInvariant(codeStem[0])}{codeStem[1..]}",
        FieldErrors = [new StandardCrudFieldError("CompanyId", $"Organization.Department.{codeStem}", message)]
    };
}
