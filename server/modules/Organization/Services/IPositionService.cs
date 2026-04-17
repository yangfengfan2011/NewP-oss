using ICP.Platform.Modules.Organization.DTOs;
namespace ICP.Platform.Modules.Organization.Services;

public interface IPositionService
{
    Task<List<PositionDto>> GetByDepartmentIdAsync(Guid departmentId, CancellationToken cancellationToken = default);
    Task<PositionDto?> GetByIdAsync(Guid positionId, CancellationToken cancellationToken = default);
    Task<PositionDto> CreateAsync(PositionDto dto, CancellationToken cancellationToken = default);
    Task<PositionDto> UpdateAsync(Guid positionId, PositionDto dto, CancellationToken cancellationToken = default);
}
