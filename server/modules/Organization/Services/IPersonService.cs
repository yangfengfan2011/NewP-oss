using ICP.Platform.Modules.Organization.DTOs;
using ICP.Platform.Modules.Organization.DTOs.Requests;

namespace ICP.Platform.Modules.Organization.Services;

public interface IPersonService
{
    Task<PersonDto?> GetByIdAsync(Guid personId, CancellationToken cancellationToken = default);
    Task<PersonDetailDto?> GetDetailAsync(Guid personId, CancellationToken cancellationToken = default);
    Task<PersonDto> CreateAsync(CreatePersonRequest request, CancellationToken cancellationToken = default);
    Task<PersonDto> UpdateAsync(Guid personId, UpdatePersonRequest request, CancellationToken cancellationToken = default);
    Task<PersonUserLinkDto> LinkUserAsync(Guid personId, LinkPersonUserRequest request, CancellationToken cancellationToken = default);
}
