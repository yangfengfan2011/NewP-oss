using ICP.Platform.Authorization.Http;
using ICP.Platform.Crud.Models;
using ICP.Platform.Modules.Organization.Directories;
using ICP.Platform.Modules.Organization.Models;
using Microsoft.AspNetCore.Mvc;

namespace ICP.Platform.Modules.Organization.Controllers;

/// <summary>
/// 人员组织归属的模块专用分页 Lookup；不占用或扩展 Standard CRUD 资源 options 路由。
/// </summary>
/// <param name="companyDirectory">人员公司候选与当前值的窄读取入口。</param>
/// <param name="departmentDirectory">人员部门候选与当前值的公司受限窄读取入口。</param>
[ApiController]
[Route("api/organization/person-lookups")]
[EndpointModule("Organization")]
public sealed class PersonOrganizationLookupsController(
    ICompanyDirectory companyDirectory,
    IDepartmentDirectory departmentDirectory) : ControllerBase
{
    /// <summary>
    /// 搜索一页公司候选，并独立解析可能已停用的当前公司。
    /// </summary>
    [HttpGet("companies")]
    [RequirePermission(OrganizationPermissions.PersonView)]
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
        return Ok(Map(
            result,
            company => ToOption(company.Id, company.Name, company.IsEnabled, includeDisabled),
            static company => ToOption(company.Id, company.Name, company.IsEnabled)));
    }

    /// <summary>
    /// 在显式公司范围内搜索一页部门候选，并拒绝跨公司解析当前部门。
    /// </summary>
    [HttpGet("departments")]
    [RequirePermission(OrganizationPermissions.PersonView)]
    public async Task<ActionResult<StandardCrudLookupResult>> Departments(
        [FromQuery] string companyId,
        [FromQuery] string? keyword = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? currentValueIds = null,
        [FromQuery] bool includeDisabled = false,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(companyId))
        {
            return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                ["companyId"] = ["所属公司不能为空。"]
            }));
        }

        var result = await departmentDirectory.LookupAsync(
            companyId,
            new OrganizationDirectoryLookupRequest(
                keyword,
                page,
                pageSize,
                ParseCurrentValueIds(currentValueIds),
                includeDisabled),
            cancellationToken);
        return Ok(Map(
            result,
            department => ToOption(department.Id, department.Name, department.IsEnabled, includeDisabled),
            static department => ToOption(department.Id, department.Name, department.IsEnabled)));
    }

    // 把模块目录页收敛为共享 Lookup 响应外形，但不改变共享请求协议。
    private static StandardCrudLookupResult Map<T>(
        OrganizationDirectoryPage<T> page,
        Func<T, StandardCrudOptionItem> mapItem,
        Func<T, StandardCrudOptionItem> mapCurrentItem) =>
        new(
            page.Items.Select(mapItem).ToList(),
            page.CurrentItems.Select(mapCurrentItem).ToList(),
            page.Page,
            page.PageSize,
            page.HasMore);

    // 当前停用值不可重新选择；显式列表筛选中的停用项仍可用于查询历史归属。
    private static StandardCrudOptionItem ToOption(
        string id,
        string name,
        bool isEnabled,
        bool allowDisabledSelection = false) => new()
    {
        Value = id,
        Label = isEnabled ? name : $"{name}（已停用）",
        Disabled = !isEnabled && !allowDisabledSelection
    };

    // 前端按共享约定发送逗号分隔标识；模块边界负责去空白、去重与数量上限。
    private static IReadOnlyCollection<string> ParseCurrentValueIds(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(static id => id.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .Take(100)
                .ToArray();
}
