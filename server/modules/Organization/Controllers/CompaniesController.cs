using ICP.Platform.Modules.Organization.DTOs;
using ICP.Platform.Modules.Organization.DTOs.Requests;
using ICP.Platform.Modules.Organization.Services;
using Microsoft.AspNetCore.Mvc;

namespace ICP.Platform.Modules.Organization.Controllers;

[ApiController]
[Route("api/organization/companies")]
public sealed class CompaniesController : ControllerBase
{
    private readonly ICompanyService _companyService;

    public CompaniesController(ICompanyService companyService)
    {
        _companyService = companyService;
    }

    [HttpGet]
    public async Task<ActionResult<List<CompanyDto>>> GetAll(CancellationToken cancellationToken = default)
    {
        var companies = await _companyService.GetAllAsync(cancellationToken);
        return Ok(companies);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<CompanyDto>> Get(Guid id, CancellationToken cancellationToken = default)
    {
        var company = await _companyService.GetByIdAsync(id, cancellationToken);
        return company is null ? NotFound() : Ok(company);
    }

    [HttpPost]
    public async Task<ActionResult<CompanyDto>> Create(
        [FromBody] CreateCompanyRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var company = await _companyService.CreateAsync(request, cancellationToken);
            return CreatedAtAction(nameof(Get), new { id = company.Id }, company);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<CompanyDto>> Update(
        Guid id,
        [FromBody] UpdateCompanyRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var company = await _companyService.UpdateAsync(id, request, cancellationToken);
            return Ok(company);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
