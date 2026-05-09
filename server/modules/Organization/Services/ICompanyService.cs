using ICP.Platform.Modules.Organization.DTOs;
namespace ICP.Platform.Modules.Organization.Services;

public interface ICompanyService
{
    Task<List<CompanyDto>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<CompanyDto?> GetByIdAsync(string companyId, CancellationToken cancellationToken = default);
    Task<CompanyDto> CreateAsync(CompanyDto dto, CancellationToken cancellationToken = default);
    Task<CompanyDto> UpdateAsync(string companyId, CompanyDto dto, CancellationToken cancellationToken = default);
}
