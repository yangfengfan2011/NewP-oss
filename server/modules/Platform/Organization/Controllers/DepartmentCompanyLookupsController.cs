using ICP.Platform.Authorization.Http;
using ICP.Platform.Crud.Models;
using ICP.Platform.Modules.Organization.Directories;
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
    [RequireFeature("organization.department.company.lookup")]
    [OrganizationLookupExecution("organization.department.company.lookup", "organization.company")]
    public async Task<ActionResult<StandardCrudLookupResult>> Companies(
        [FromQuery] string? keyword = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? currentValueIds = null,
        [FromQuery] bool includeDisabled = false,
        CancellationToken cancellationToken = default)
    {
        var result = await companyDirectory.LookupAsync(
            OrganizationLookupHttpProjection.WithExecution(OrganizationLookupHttpProjection.CreateRequest(
                keyword, page, pageSize, currentValueIds, includeDisabled), "organization.department.company.lookup"),
            cancellationToken);
        return Ok(OrganizationLookupHttpProjection.Map(result, includeDisabled));
    }
}
