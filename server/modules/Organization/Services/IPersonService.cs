using ICP.Platform.Modules.Organization.DTOs;
using ICP.Platform.Modules.Organization.DTOs.Requests;
using ICP.Platform.Modules.Organization.Models;
namespace ICP.Platform.Modules.Organization.Services;

public interface IPersonService
{
    Task<PersonDto?> GetByIdAsync(string personId, CancellationToken cancellationToken = default);
    Task<PersonDetailDto?> GetDetailAsync(string personId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PersonListItemDto>> GetListAsync(
        string? keyword,
        string? companyId,
        string? departmentId,
        EmploymentStatus? employmentStatus,
        CancellationToken cancellationToken = default);
    Task<PersonDto> CreateAsync(PersonDto dto, CancellationToken cancellationToken = default);
    Task<PersonDto> UpdateAsync(string personId, PersonDto dto, CancellationToken cancellationToken = default);
    Task<PersonDetailDto> DisableAsync(string personId, CancellationToken cancellationToken = default);
    Task<PersonUserLinkDto> LinkUserAsync(string personId, LinkPersonUserRequest request, CancellationToken cancellationToken = default);
}
