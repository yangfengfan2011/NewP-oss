using ICP.Platform.Modules.Authorization.DTOs;

namespace ICP.Platform.Modules.Authorization.Services;

public interface IRoleService
{
    Task<List<RoleDto>> GetAllRolesAsync(CancellationToken cancellationToken = default);
    Task<RoleDto> CreateRoleAsync(RoleDto dto, string? createdBy = null, CancellationToken cancellationToken = default);
    Task<RoleDto> UpdateRoleAsync(string roleId, RoleDto dto, string? updatedBy = null, CancellationToken cancellationToken = default);
    Task DeleteRoleAsync(string roleId, CancellationToken cancellationToken = default);
    Task<UserRoleAssignmentsDto> GetUserRolesAsync(string userId, CancellationToken cancellationToken = default);
    Task AssignUserRolesAsync(string userId, AssignUserRolesRequest request, string? assignedBy = null, CancellationToken cancellationToken = default);
}
