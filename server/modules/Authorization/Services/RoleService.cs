using ICP.Platform.Modules.Authorization.DTOs;
using ICP.Platform.Modules.Authorization.Models;
using ICP.Platform.Modules.Authorization.Repositories;

namespace ICP.Platform.Modules.Authorization.Services;

public class RoleService : IRoleService
{
    private readonly IRoleRepository _roleRepository;
    private readonly IPermissionRepository _permissionRepository;
    private readonly IRolePermissionRepository _rolePermissionRepository;
    private readonly IUserRoleRepository _userRoleRepository;

    public RoleService(
        IRoleRepository roleRepository,
        IPermissionRepository permissionRepository,
        IRolePermissionRepository rolePermissionRepository,
        IUserRoleRepository userRoleRepository)
    {
        _roleRepository = roleRepository;
        _permissionRepository = permissionRepository;
        _rolePermissionRepository = rolePermissionRepository;
        _userRoleRepository = userRoleRepository;
    }

    public async Task<List<RoleDto>> GetAllRolesAsync(CancellationToken cancellationToken = default)
    {
        var roles = await _roleRepository.GetAllAsync(cancellationToken);
        return roles.Select(MapToDto).ToList();
    }

    public async Task<RoleDto?> GetRoleByIdAsync(string roleId, CancellationToken cancellationToken = default)
    {
        var role = await _roleRepository.GetByIdAsync(roleId, cancellationToken);
        return role != null ? MapToDto(role) : null;
    }

    public async Task<RoleDto?> GetRoleByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        var role = await _roleRepository.GetByCodeAsync(code, cancellationToken);
        return role != null ? MapToDto(role) : null;
    }

    public async Task<RoleDto> CreateRoleAsync(RoleDto dto, string? createdBy = null, CancellationToken cancellationToken = default)
    {
        var existing = await _roleRepository.GetByCodeAsync(dto.Code, cancellationToken);
        if (existing != null)
        {
            throw new InvalidOperationException($"角色代码 '{dto.Code}' 已存在");
        }

        var role = new Role
        {
            Code = dto.Code,
            Name = dto.Name,
            Description = dto.Description,
            CreatedBy = createdBy
        };

        await _roleRepository.CreateAsync(role, cancellationToken);
        return MapToDto(role);
    }

    public async Task<RoleDto> UpdateRoleAsync(string roleId, RoleDto dto, string? updatedBy = null, CancellationToken cancellationToken = default)
    {
        var role = await _roleRepository.GetByIdAsync(roleId, cancellationToken);
        if (role == null)
        {
            throw new InvalidOperationException($"角色 '{roleId}' 不存在");
        }

        if (role.IsBuiltIn)
        {
            throw new InvalidOperationException("系统内置角色不允许修改");
        }

        // 单 DTO 不代表所有字段都可编辑；更新仍保持当前“代码不可改、系统字段不可回写”的边界。
        role.Name = dto.Name;
        role.Description = dto.Description;
        role.IsEnabled = dto.IsEnabled;
        role.UpdatedAt = DateTime.UtcNow;
        role.UpdatedBy = updatedBy;

        await _roleRepository.UpdateAsync(role, cancellationToken);
        return MapToDto(role);
    }

    public async Task DeleteRoleAsync(string roleId, CancellationToken cancellationToken = default)
    {
        var role = await _roleRepository.GetByIdAsync(roleId, cancellationToken);
        if (role == null)
        {
            throw new InvalidOperationException($"角色 '{roleId}' 不存在");
        }

        if (role.IsBuiltIn)
        {
            throw new InvalidOperationException("系统内置角色不允许删除");
        }

        // 删除角色权限关系
        await _rolePermissionRepository.DeleteByRoleIdAsync(roleId, cancellationToken);

        // 删除用户角色关系
        await _userRoleRepository.DeleteByRoleIdAsync(roleId, cancellationToken);

        // 删除角色
        await _roleRepository.DeleteAsync(roleId, cancellationToken);
    }

    public async Task<RolePermissionsDto> GetRolePermissionsAsync(string roleId, CancellationToken cancellationToken = default)
    {
        var rolePermissions = await _rolePermissionRepository.GetByRoleIdAsync(roleId, cancellationToken);
        var permissionIds = rolePermissions.Select(rp => rp.PermissionId).ToList();

        var permissions = await _permissionRepository.GetByIdsAsync(permissionIds, cancellationToken);
        var permissionCodes = permissions.Select(p => p.Code).ToList();

        return new RolePermissionsDto
        {
            RoleId = roleId,
            PermissionCodes = permissionCodes
        };
    }

    public async Task AssignRolePermissionsAsync(string roleId, AssignRolePermissionsRequest request, string? grantedBy = null, CancellationToken cancellationToken = default)
    {
        var role = await _roleRepository.GetByIdAsync(roleId, cancellationToken);
        if (role == null)
        {
            throw new InvalidOperationException($"角色 '{roleId}' 不存在");
        }

        // 获取权限 ID
        var permissions = await _permissionRepository.GetByCodesAsync(request.PermissionCodes, cancellationToken);
        var permissionIds = permissions.Select(p => p.Id).ToList();

        // 删除现有权限
        await _rolePermissionRepository.DeleteByRoleIdAsync(roleId, cancellationToken);

        // 添加新权限
        var rolePermissions = permissionIds.Select(permissionId => new RolePermission
        {
            RoleId = roleId,
            PermissionId = permissionId,
            GrantedBy = grantedBy
        }).ToList();

        await _rolePermissionRepository.CreateBatchAsync(rolePermissions, cancellationToken);
    }

    public async Task<UserRoleAssignmentsDto> GetUserRolesAsync(string userId, CancellationToken cancellationToken = default)
    {
        var userRoles = await _userRoleRepository.GetByUserIdAsync(userId, cancellationToken);
        var roleIds = userRoles.Select(ur => ur.RoleId).ToList();

        var roles = await _roleRepository.GetByIdsAsync(roleIds, cancellationToken);
        var roleCodes = roles.Select(r => r.Code).ToList();

        return new UserRoleAssignmentsDto
        {
            UserId = userId,
            RoleCodes = roleCodes
        };
    }

    public async Task AssignUserRolesAsync(string userId, AssignUserRolesRequest request, string? assignedBy = null, CancellationToken cancellationToken = default)
    {
        // 获取角色 ID
        var roles = await _roleRepository.GetByCodesAsync(request.RoleCodes, cancellationToken);
        var roleIds = roles.Select(r => r.Id).ToList();

        // 删除现有角色
        await _userRoleRepository.DeleteByUserIdAsync(userId, cancellationToken);

        // 添加新角色
        var userRoles = roleIds.Select(roleId => new UserRole
        {
            UserId = userId,
            RoleId = roleId,
            AssignedBy = assignedBy
        }).ToList();

        await _userRoleRepository.CreateBatchAsync(userRoles, cancellationToken);
    }

    private static RoleDto MapToDto(Role role)
    {
        return new RoleDto
        {
            Id = role.Id,
            Code = role.Code,
            Name = role.Name,
            Description = role.Description,
            IsEnabled = role.IsEnabled,
            IsBuiltIn = role.IsBuiltIn,
            CreatedBy = role.CreatedBy,
            CreatedAt = role.CreatedAt,
            UpdatedBy = role.UpdatedBy,
            UpdatedAt = role.UpdatedAt
        };
    }
}
