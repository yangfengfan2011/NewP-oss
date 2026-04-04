using ICP.Platform.Modules.Authorization.DTOs;

namespace ICP.Platform.Modules.Authorization.Services;

public interface IRoleService
{
    Task<List<RoleDto>> GetAllRolesAsync(CancellationToken cancellationToken = default);
    Task<RoleDto?> GetRoleByIdAsync(string roleId, CancellationToken cancellationToken = default);
    Task<RoleDto?> GetRoleByCodeAsync(string code, CancellationToken cancellationToken = default);
    Task<RoleDto> CreateRoleAsync(CreateRoleRequest request, string? createdBy = null, CancellationToken cancellationToken = default);
    Task<RoleDto> UpdateRoleAsync(string roleId, UpdateRoleRequest request, string? updatedBy = null, CancellationToken cancellationToken = default);
    Task DeleteRoleAsync(string roleId, CancellationToken cancellationToken = default);
    Task<RolePermissionsDto> GetRolePermissionsAsync(string roleId, CancellationToken cancellationToken = default);
    Task AssignRolePermissionsAsync(string roleId, AssignRolePermissionsRequest request, string? grantedBy = null, CancellationToken cancellationToken = default);
    Task<UserRoleDto> GetUserRolesAsync(string userId, CancellationToken cancellationToken = default);
    Task AssignUserRolesAsync(string userId, AssignUserRolesRequest request, string? assignedBy = null, CancellationToken cancellationToken = default);
}
