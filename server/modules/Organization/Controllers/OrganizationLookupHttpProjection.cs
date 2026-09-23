using ICP.Platform.Crud.Models;
using ICP.Platform.Modules.Organization.Directories;
using ICP.Platform.Modules.Organization.Models;

namespace ICP.Platform.Modules.Organization.Controllers;

/// <summary>
/// 统一 Organization 专用 Lookup 的 HTTP 请求与响应规则。
/// </summary>
internal static class OrganizationLookupHttpProjection
{
    // 在模块 HTTP 边界组装目录请求，并规范逗号分隔的当前值标识。
    internal static OrganizationDirectoryLookupRequest CreateRequest(
        string? keyword,
        int page,
        int pageSize,
        string? currentValueIds,
        bool includeDisabled) => new(
        keyword,
        page,
        pageSize,
        ParseCurrentValueIds(currentValueIds),
        includeDisabled);

    // 公司候选和当前值使用模块统一的停用标记与选择规则。
    internal static StandardCrudLookupResult Map(
        OrganizationDirectoryPage<Company> page,
        bool allowDisabledSelection) => Map(
        page,
        static company => company.Id,
        static company => company.Name,
        static company => company.IsEnabled,
        allowDisabledSelection);

    // 部门候选和当前值使用模块统一的停用标记与选择规则。
    internal static StandardCrudLookupResult Map(
        OrganizationDirectoryPage<Department> page,
        bool allowDisabledSelection) => Map(
        page,
        static department => department.Id,
        static department => department.Name,
        static department => department.IsEnabled,
        allowDisabledSelection);

    // 候选项遵循 includeDisabled，当前停用值始终保留标记且禁止重新选择。
    private static StandardCrudLookupResult Map<T>(
        OrganizationDirectoryPage<T> page,
        Func<T, string> valueSelector,
        Func<T, string> labelSelector,
        Func<T, bool> enabledSelector,
        bool allowDisabledSelection)
    {
        var masked = new HashSet<string>(page.MaskedCurrentIds ?? [], StringComparer.Ordinal);
        return new(
            page.Items.Select(item => ToOption(item, valueSelector, labelSelector, enabledSelector,
                allowDisabledSelection, maskedCurrent: false)).ToList(),
            page.CurrentItems.Select(item => ToOption(item, valueSelector, labelSelector, enabledSelector,
                allowDisabledSelection: false, maskedCurrent: masked.Contains(valueSelector(item)))).ToList(),
            page.Page, page.PageSize, page.HasMore);
    }

    // 当前值标识去空白、去重；目录按批查询，不把请求的已有关系静默截断。
    private static IReadOnlyCollection<string> ParseCurrentValueIds(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(static id => id.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .ToArray();

    // 将领域目录项映射为共享 Lookup option，不把选择规则下沉到领域模型。
    private static StandardCrudOptionItem ToOption<T>(
        T item,
        Func<T, string> valueSelector,
        Func<T, string> labelSelector,
        Func<T, bool> enabledSelector,
        bool allowDisabledSelection,
        bool maskedCurrent)
    {
        if (maskedCurrent)
            return new StandardCrudOptionItem { Value = valueSelector(item), Label = "***", Disabled = true };
        var isEnabled = enabledSelector(item);
        var label = labelSelector(item);
        return new StandardCrudOptionItem
        {
            Value = valueSelector(item),
            Label = isEnabled ? label : $"{label}（已停用）",
            Disabled = !isEnabled && !allowDisabledSelection
        };
    }
}
