using ICP.Platform.Modules.Organization.DTOs;
using ICP.Platform.Modules.Organization.DTOs.Requests;
using ICP.Platform.Modules.Organization.Models;
namespace ICP.Platform.Modules.Organization.Services;

public interface IPersonService
{
    Task<PersonDto?> GetByIdAsync(Guid personId, CancellationToken cancellationToken = default);
    Task<PersonDetailDto?> GetDetailAsync(Guid personId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PersonListItemDto>> GetListAsync(
        string? keyword,
        Guid? companyId,
        Guid? departmentId,
        EmploymentStatus? employmentStatus,
        CancellationToken cancellationToken = default);
    Task<PersonDto> CreateAsync(PersonDto dto, CancellationToken cancellationToken = default);
    Task<PersonDto> UpdateAsync(Guid personId, PersonDto dto, CancellationToken cancellationToken = default);
    Task<PersonDetailDto> DisableAsync(Guid personId, CancellationToken cancellationToken = default);
    Task<PersonUserLinkDto> LinkUserAsync(Guid personId, LinkPersonUserRequest request, CancellationToken cancellationToken = default);
}
