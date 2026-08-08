using ICP.Platform.Authorization.Http;
using ICP.Platform.Modules.Organization.DTOs;
using ICP.Platform.Modules.Organization.Services;
using Microsoft.AspNetCore.Mvc;

namespace ICP.Platform.Modules.Organization.Controllers;

[ApiController]
[Route("api/organization/departments")]
[EndpointModule("Organization")]
public sealed class DepartmentsController : ControllerBase
{
    private readonly IDepartmentService _departmentService;

    public DepartmentsController(IDepartmentService departmentService)
    {
        _departmentService = departmentService;
    }

    [HttpGet]
    [RequirePermission(OrganizationPermissions.DepartmentView)]
    public async Task<ActionResult<List<DepartmentDto>>> GetByCompany(
        [FromQuery] string companyId,
        CancellationToken cancellationToken = default)
    {
        var departments = await _departmentService.GetByCompanyIdAsync(companyId, cancellationToken);
        return Ok(departments);
    }

    [HttpGet("{id}")]
    [RequirePermission(OrganizationPermissions.DepartmentView)]
    public async Task<ActionResult<DepartmentDto>> Get(string id, CancellationToken cancellationToken = default)
    {
        var department = await _departmentService.GetByIdAsync(id, cancellationToken);
        return department is null ? NotFound() : Ok(department);
    }

    [HttpPost]
    [RequirePermission(OrganizationPermissions.DepartmentCreate)]
    public async Task<ActionResult<DepartmentDto>> Create(
        [FromBody] DepartmentDto dto,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var department = await _departmentService.CreateAsync(dto, cancellationToken);
            return CreatedAtAction(nameof(Get), new { id = department.Id }, department);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPut("{id}")]
    [RequirePermission(OrganizationPermissions.DepartmentUpdate)]
    public async Task<ActionResult<DepartmentDto>> Update(
        string id,
        [FromBody] DepartmentDto dto,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var department = await _departmentService.UpdateAsync(id, dto, cancellationToken);
            return Ok(department);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
