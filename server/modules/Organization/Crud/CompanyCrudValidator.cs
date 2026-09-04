using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Models;
using ICP.Platform.Persistence.EfCore;
using Microsoft.EntityFrameworkCore;

namespace ICP.Platform.Modules.Organization.Crud;

public sealed class CompanyCrudValidator(EfPlatformDbContext dbContext) : IStandardCrudValidator
{
    public string ResourceKey => "organization.company";

    /// <summary>
    /// 校验创建输入中的字符串排序码，不把前导零转换为数值。
    /// </summary>
    public Task<StandardCrudError?> ValidateCreateAsync(
        StandardCrudDescriptor descriptor,
        IReadOnlyDictionary<string, object?> values,
        CancellationToken cancellationToken = default)
    {
        _ = descriptor;
        _ = cancellationToken;
        return Task.FromResult(ValidateSortOrder(values));
    }

    /// <summary>
    /// 校验编辑排序码，并在启用到停用的状态转换前拒绝仍挂有启用部门的公司。
    /// </summary>
    public async Task<StandardCrudError?> ValidateUpdateAsync(
        StandardCrudDescriptor descriptor,
        string id,
        IReadOnlyDictionary<string, object?> values,
        CancellationToken cancellationToken = default)
    {
        var sortOrderError = ValidateSortOrder(values);
        if (sortOrderError is not null || !IsDisabling(values))
        {
            return sortOrderError;
        }

        var isCurrentlyEnabled = await Companies(descriptor)
            .AsNoTracking()
            .Where(company => EF.Property<string>(company, descriptor.IdField) == id)
            .Select(company => EF.Property<bool>(company, "IsEnabled"))
            .SingleOrDefaultAsync(cancellationToken);
        if (!isCurrentlyEnabled)
        {
            return null;
        }

        var hasEnabledDepartment = await Departments()
            .AsNoTracking()
            .AnyAsync(department =>
                EF.Property<string>(department, "CompanyId") == id
                && EF.Property<bool>(department, "IsEnabled"),
                cancellationToken);
        return hasEnabledDepartment ? DisableBlockedError() : null;
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

    // IsEnabled 只在普通更新显式提交 false 时触发停用守卫。
    private static bool IsDisabling(IReadOnlyDictionary<string, object?> values) =>
        values.GetValueOrDefault("IsEnabled") is false;

    // 动态 Company 集合只用于读取当前启用状态，避免把旧宽 CRUD service 恢复为第二 owner。
    private DbSet<Dictionary<string, object?>> Companies(StandardCrudDescriptor descriptor) =>
        dbContext.Set<Dictionary<string, object?>>(dbContext.ResolveModelKey(descriptor.ModelName));

    // 启用部门守卫直接下推到同一 Organization 持久化边界。
    private DbSet<Dictionary<string, object?>> Departments() =>
        dbContext.Set<Dictionary<string, object?>>(dbContext.ResolveModelKey("Department"));

    // 字符串格式错误绑定到 SortOrder，供标准 Drawer 精确展示。
    private static StandardCrudError SortOrderError() => new()
    {
        StatusCode = 400,
        Code = "Organization.Company.SortOrderInvalid",
        Message = "排序码必须由 1 至 32 位 ASCII 数字组成。",
        ReasonKey = "organization.company.sortOrderInvalid",
        FieldErrors =
        [
            new StandardCrudFieldError(
                "SortOrder",
                "Organization.Company.SortOrderInvalid",
                "排序码必须由 1 至 32 位 ASCII 数字组成。")
        ]
    };

    // 公司下仍有启用部门属于可恢复的状态冲突，不清除或级联修改部门。
    private static StandardCrudError DisableBlockedError() => new()
    {
        StatusCode = 409,
        Code = "Organization.Company.DisableBlockedByEnabledDepartment",
        Message = "公司仍有启用部门，不能停用。",
        ReasonKey = "organization.company.disableBlockedByEnabledDepartment"
    };
}
