using ICP.Platform.Authorization.Http;
using ICP.Platform.Crud.Models;
using ICP.Platform.Modules.Organization.Directories;
using ICP.Platform.Modules.Organization.Models;
using Microsoft.AspNetCore.Mvc;

namespace ICP.Platform.Modules.Organization.Controllers;

/// <summary>
/// 部门管理拥有的依赖上级 Lookup；显式消费公司和当前部门上下文。
/// </summary>
[ApiController]
[Route("api/organization/department-lookups")]
[EndpointModule("Organization")]
public sealed class DepartmentHierarchyLookupsController(
    IDepartmentDirectory departmentDirectory,
    IDepartmentHierarchyDirectory hierarchyDirectory) : ControllerBase
{
    /// <summary>
    /// 搜索一页同公司上级候选；当前停用值仍可解析，但不会成为新候选。
    /// </summary>
    [HttpGet("parents")]
    [RequirePermission(OrganizationPermissions.DepartmentView)]
    public async Task<ActionResult<StandardCrudLookupResult>> Parents(
        [FromQuery] string companyId,
        [FromQuery] string? currentDepartmentCode = null,
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

        var current = string.IsNullOrWhiteSpace(currentDepartmentCode)
            ? null
            : await departmentDirectory.GetByCompanyAndCodeAsync(
                companyId,
                currentDepartmentCode,
                cancellationToken);
        try
        {
            var result = await hierarchyDirectory.LookupParentCandidatesAsync(
                companyId,
                current?.Id,
                new OrganizationDirectoryLookupRequest(
                    keyword,
                    page,
                    pageSize,
                    ParseCurrentValueIds(currentValueIds),
                    includeDisabled),
                cancellationToken);
            return Ok(Map(result, includeDisabled));
        }
        catch (DepartmentHierarchyCorruptedException exception)
        {
            var problem = new ProblemDetails
            {
                Status = 409,
                Title = "部门层级数据损坏。",
                Detail = exception.Message
            };
            problem.Extensions["code"] = exception.Code;
            return Conflict(problem);
        }
    }

    // Lookup HTTP 外形保持与模块现有分页 provider 一致，但筛选上下文不进入共享协议。
    private static StandardCrudLookupResult Map(
        OrganizationDirectoryPage<Department> page,
        bool allowDisabledSelection) => new(
        page.Items.Select(item => ToOption(item, allowDisabledSelection)).ToList(),
        page.CurrentItems.Select(item => ToOption(item, allowDisabledSelection: false)).ToList(),
        page.Page,
        page.PageSize,
        page.HasMore);

    // 停用当前值带稳定标记并禁止重新选择；列表筛选可以显式包含停用项。
    private static StandardCrudOptionItem ToOption(Department department, bool allowDisabledSelection) => new()
    {
        Value = department.Id,
        Label = department.IsEnabled ? department.Name : $"{department.Name}（已停用）",
        Disabled = !department.IsEnabled && !allowDisabledSelection
    };

    // 当前值标识在模块 HTTP 边界去空白、去重并限制到一百个。
    private static IReadOnlyCollection<string> ParseCurrentValueIds(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(static id => id.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .Take(100)
                .ToArray();
}
