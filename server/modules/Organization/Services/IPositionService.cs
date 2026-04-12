using ICP.Platform.Modules.Organization.DTOs;
using ICP.Platform.Modules.Organization.DTOs.Requests;

namespace ICP.Platform.Modules.Organization.Services;

public interface IPositionService
{
    Task<List<PositionDto>> GetByDepartmentIdAsync(Guid departmentId, CancellationToken cancellationToken = default);
    Task<PositionDto?> GetByIdAsync(Guid positionId, CancellationToken cancellationToken = default);
    Task<PositionDto> CreateAsync(CreatePositionRequest request, CancellationToken cancellationToken = default);
    Task<PositionDto> UpdateAsync(Guid positionId, UpdatePositionRequest request, CancellationToken cancellationToken = default);
}
