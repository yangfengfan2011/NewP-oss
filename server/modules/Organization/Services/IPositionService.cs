using ICP.Platform.Modules.Organization.DTOs;
namespace ICP.Platform.Modules.Organization.Services;

public interface IPositionService
{
    Task<List<PositionDto>> GetByDepartmentIdAsync(string departmentId, CancellationToken cancellationToken = default);
    Task<PositionDto?> GetByIdAsync(string positionId, CancellationToken cancellationToken = default);
    Task<PositionDto> CreateAsync(PositionDto dto, CancellationToken cancellationToken = default);
    Task<PositionDto> UpdateAsync(string positionId, PositionDto dto, CancellationToken cancellationToken = default);
}
