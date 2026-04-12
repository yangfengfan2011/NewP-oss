using ICP.Platform.Modules.Organization.DTOs;
using ICP.Platform.Modules.Organization.DTOs.Requests;

namespace ICP.Platform.Modules.Organization.Services;

public interface ICompanyService
{
    Task<List<CompanyDto>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<CompanyDto?> GetByIdAsync(Guid companyId, CancellationToken cancellationToken = default);
    Task<CompanyDto> CreateAsync(CreateCompanyRequest request, CancellationToken cancellationToken = default);
    Task<CompanyDto> UpdateAsync(Guid companyId, UpdateCompanyRequest request, CancellationToken cancellationToken = default);
}
