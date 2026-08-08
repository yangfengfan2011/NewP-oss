using ICP.Platform.Authorization.Http;
using ICP.Platform.Modules.Organization.DTOs;
using ICP.Platform.Modules.Organization.Services;
using Microsoft.AspNetCore.Mvc;

namespace ICP.Platform.Modules.Organization.Controllers;

[ApiController]
[Route("api/organization/companies")]
[EndpointModule("Organization")]
public sealed class CompaniesController : ControllerBase
{
    private readonly ICompanyService _companyService;

    public CompaniesController(ICompanyService companyService)
    {
        _companyService = companyService;
    }

    [HttpGet]
    [RequirePermission(OrganizationPermissions.CompanyView)]
    public async Task<ActionResult<List<CompanyDto>>> GetAll(CancellationToken cancellationToken = default)
    {
        var companies = await _companyService.GetAllAsync(cancellationToken);
        return Ok(companies);
    }

    [HttpGet("{id}")]
    [RequirePermission(OrganizationPermissions.CompanyView)]
    public async Task<ActionResult<CompanyDto>> Get(string id, CancellationToken cancellationToken = default)
    {
        var company = await _companyService.GetByIdAsync(id, cancellationToken);
        return company is null ? NotFound() : Ok(company);
    }

    [HttpPost]
    [RequirePermission(OrganizationPermissions.CompanyCreate)]
    public async Task<ActionResult<CompanyDto>> Create(
        [FromBody] CompanyDto dto,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var company = await _companyService.CreateAsync(dto, cancellationToken);
            return CreatedAtAction(nameof(Get), new { id = company.Id }, company);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPut("{id}")]
    [RequirePermission(OrganizationPermissions.CompanyUpdate)]
    public async Task<ActionResult<CompanyDto>> Update(
        string id,
        [FromBody] CompanyDto dto,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var company = await _companyService.UpdateAsync(id, dto, cancellationToken);
            return Ok(company);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
