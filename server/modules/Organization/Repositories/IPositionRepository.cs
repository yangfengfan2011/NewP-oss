using ICP.Platform.Modules.Organization.Models;

namespace ICP.Platform.Modules.Organization.Repositories;

public interface IPositionRepository
{
    Task<List<Position>> GetByDepartmentIdAsync(string departmentId, CancellationToken cancellationToken = default);
    Task<Position?> GetByIdAsync(string positionId, CancellationToken cancellationToken = default);
    Task CreateAsync(Position position, CancellationToken cancellationToken = default);
    Task UpdateAsync(Position position, CancellationToken cancellationToken = default);
}
