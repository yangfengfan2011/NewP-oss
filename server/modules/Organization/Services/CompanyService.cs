using ICP.Platform.Modules.Organization.DTOs;
using ICP.Platform.Modules.Organization.DTOs.Requests;
using ICP.Platform.Modules.Organization.Models;
using ICP.Platform.Modules.Organization.Repositories;

namespace ICP.Platform.Modules.Organization.Services;

public sealed class CompanyService : ICompanyService
{
    private readonly ICompanyRepository _companyRepository;

    public CompanyService(ICompanyRepository companyRepository)
    {
        _companyRepository = companyRepository;
    }

    public async Task<List<CompanyDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var companies = await _companyRepository.GetAllAsync(cancellationToken);
        return companies.Select(MapToDto).ToList();
    }

    public async Task<CompanyDto?> GetByIdAsync(Guid companyId, CancellationToken cancellationToken = default)
    {
        var company = await _companyRepository.GetByIdAsync(companyId, cancellationToken);
        return company is null ? null : MapToDto(company);
    }

    public async Task<CompanyDto> CreateAsync(CreateCompanyRequest request, CancellationToken cancellationToken = default)
    {
        var existing = await _companyRepository.GetByCodeAsync(request.Code, cancellationToken);
        if (existing is not null)
        {
            throw new InvalidOperationException($"公司编码 '{request.Code}' 已存在。");
        }

        var company = new Company
        {
            Code = request.Code,
            Name = request.Name,
            SortOrder = request.SortOrder
        };

        await _companyRepository.CreateAsync(company, cancellationToken);
        return MapToDto(company);
    }

    public async Task<CompanyDto> UpdateAsync(Guid companyId, UpdateCompanyRequest request, CancellationToken cancellationToken = default)
    {
        var company = await _companyRepository.GetByIdAsync(companyId, cancellationToken);
        if (company is null)
        {
            throw new InvalidOperationException($"公司 '{companyId}' 不存在。");
        }

        company.Name = request.Name;
        company.IsEnabled = request.IsEnabled;
        company.SortOrder = request.SortOrder;
        company.UpdatedAt = DateTime.UtcNow;

        await _companyRepository.UpdateAsync(company, cancellationToken);
        return MapToDto(company);
    }

    private static CompanyDto MapToDto(Company company) => new()
    {
        Id = company.Id,
        Code = company.Code,
        Name = company.Name,
        IsEnabled = company.IsEnabled,
        SortOrder = company.SortOrder,
        CreatedAt = company.CreatedAt
    };
}
