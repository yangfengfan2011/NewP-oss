using ICP.Platform.Authorization.Http;
using ICP.Platform.Crud.Models;
using ICP.Platform.Modules.Organization.Directories;
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
    [RequireFeature("organization.department.parent.lookup")]
    [OrganizationLookupExecution("organization.department.parent.lookup", "organization.department")]
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
                OrganizationLookupHttpProjection.WithExecution(OrganizationLookupHttpProjection.CreateRequest(
                    keyword, page, pageSize, currentValueIds, includeDisabled), "organization.department.parent.lookup"),
                cancellationToken);
            return Ok(OrganizationLookupHttpProjection.Map(result, includeDisabled));
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
}
