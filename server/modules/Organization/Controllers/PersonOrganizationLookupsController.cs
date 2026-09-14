using ICP.Platform.Authorization.Http;
using ICP.Platform.Crud.Models;
using ICP.Platform.Modules.Organization.Directories;
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
    [RequireFeature("organization.person.company.lookup")]
    public async Task<ActionResult<StandardCrudLookupResult>> Companies(
        [FromQuery] string? keyword = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? currentValueIds = null,
        [FromQuery] bool includeDisabled = false,
        CancellationToken cancellationToken = default)
    {
        var result = await companyDirectory.LookupAsync(
            OrganizationLookupHttpProjection.CreateRequest(
                keyword, page, pageSize, currentValueIds, includeDisabled),
            cancellationToken);
        return Ok(OrganizationLookupHttpProjection.Map(result, includeDisabled));
    }

    /// <summary>
    /// 在显式公司范围内搜索一页部门候选，并拒绝跨公司解析当前部门。
    /// </summary>
    [HttpGet("departments")]
    [RequireFeature("organization.person.department.lookup")]
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
            OrganizationLookupHttpProjection.CreateRequest(
                keyword, page, pageSize, currentValueIds, includeDisabled),
            cancellationToken);
        return Ok(OrganizationLookupHttpProjection.Map(result, includeDisabled));
    }
}
