using ICP.Platform.Modules.Organization.DTOs;
namespace ICP.Platform.Modules.Organization.Services;

public interface ICompanyService
{
    Task<List<CompanyDto>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<CompanyDto?> GetByIdAsync(Guid companyId, CancellationToken cancellationToken = default);
    Task<CompanyDto> CreateAsync(CompanyDto dto, CancellationToken cancellationToken = default);
    Task<CompanyDto> UpdateAsync(Guid companyId, CompanyDto dto, CancellationToken cancellationToken = default);
}
