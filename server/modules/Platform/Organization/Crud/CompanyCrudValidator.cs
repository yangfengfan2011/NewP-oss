using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Models;

namespace ICP.Platform.Modules.Organization.Crud;

public sealed class CompanyCrudValidator : IStandardCrudValidator
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

    /// <summary>公司可直接停用；下属资格中断由统一提交参与者原子撤销会话，不改写下级状态。</summary>
    public Task<StandardCrudError?> ValidateUpdateAsync(
        StandardCrudDescriptor descriptor,
        string id,
        IReadOnlyDictionary<string, object?> values,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(ValidateSortOrder(values));

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
}
