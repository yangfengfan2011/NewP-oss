using ICP.Platform.Authorization.Http;
using ICP.Platform.Crud.Models;
using ICP.Platform.Modules.Organization.Directories;
using ICP.Platform.Modules.Organization.Models;
using Microsoft.AspNetCore.Mvc;

namespace ICP.Platform.Modules.Organization.Controllers;

/// <summary>
/// 部门管理拥有的公司候选 Lookup；不依赖人员管理权限。
/// </summary>
[ApiController]
[Route("api/organization/department-lookups/companies")]
[EndpointModule("Organization")]
public sealed class DepartmentCompanyLookupsController(ICompanyDirectory companyDirectory) : ControllerBase
{
    /// <summary>
    /// 搜索一页部门归属公司候选；当前停用值仍可解析，但不会成为新候选。
    /// </summary>
    [HttpGet]
    [RequirePermission(OrganizationPermissions.DepartmentView)]
    public async Task<ActionResult<StandardCrudLookupResult>> Companies(
        [FromQuery] string? keyword = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? currentValueIds = null,
        [FromQuery] bool includeDisabled = false,
        CancellationToken cancellationToken = default)
    {
        var result = await companyDirectory.LookupAsync(
            new OrganizationDirectoryLookupRequest(
                keyword,
                page,
                pageSize,
                ParseCurrentValueIds(currentValueIds),
                includeDisabled),
            cancellationToken);
        return Ok(Map(result, includeDisabled));
    }

    private static StandardCrudLookupResult Map(
        OrganizationDirectoryPage<Company> page,
        bool allowDisabledSelection) => new(
        page.Items.Select(item => ToOption(item, allowDisabledSelection)).ToList(),
        page.CurrentItems.Select(item => ToOption(item, allowDisabledSelection: false)).ToList(),
        page.Page,
        page.PageSize,
        page.HasMore);

    private static StandardCrudOptionItem ToOption(Company company, bool allowDisabledSelection) => new()
    {
        Value = company.Id,
        Label = company.IsEnabled ? company.Name : $"{company.Name}（已停用）",
        Disabled = !company.IsEnabled && !allowDisabledSelection
    };

    private static IReadOnlyCollection<string> ParseCurrentValueIds(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(static id => id.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .Take(100)
                .ToArray();
}
