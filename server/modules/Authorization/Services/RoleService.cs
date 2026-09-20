using ICP.Platform.Crud.ObjectIds;
using ICP.Platform.Modules.Authorization.DTOs;
using ICP.Platform.Modules.Authorization.Models;
using ICP.Platform.Modules.Authorization.Repositories;

namespace ICP.Platform.Modules.Authorization.Services;

public class RoleService : IRoleService
{
    private readonly IRoleRepository _roleRepository;
    private readonly IRolePermissionRepository _rolePermissionRepository;
    private readonly IUserRoleRepository _userRoleRepository;
    private readonly IPlatformObjectIdGenerator _objectIdGenerator;

    // 角色维护保留内建身份和内部历史删除边界；成员关系由独立用例持有。
    public RoleService(
        IRoleRepository roleRepository,
        IRolePermissionRepository rolePermissionRepository,
        IUserRoleRepository userRoleRepository,
        IPlatformObjectIdGenerator objectIdGenerator)
    {
        _roleRepository = roleRepository;
        _rolePermissionRepository = rolePermissionRepository;
        _userRoleRepository = userRoleRepository;
        _objectIdGenerator = objectIdGenerator;
    }

    public async Task<List<RoleDto>> GetAllRolesAsync(CancellationToken cancellationToken = default)
    {
        var roles = await _roleRepository.GetAllAsync(cancellationToken);
        return roles.Select(MapToDto).ToList();
    }

    // 普通角色创建拒绝保留身份和内建标记。
    public async Task<RoleDto> CreateRoleAsync(RoleDto dto, string? createdBy = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(dto.Code) || dto.IsBuiltIn || LocalAdministratorCatalog.MatchesRoleCode(dto.Code.Trim()))
            throw new InvalidOperationException("受保护的角色身份不能通过普通管理创建。");
        var existing = await _roleRepository.GetByCodeAsync(dto.Code, cancellationToken);
        if (existing != null)
        {
            throw new InvalidOperationException($"角色代码 '{dto.Code}' 已存在");
        }

        var role = new Role
        {
            Id = _objectIdGenerator.NewId(nameof(Role)),
            Code = dto.Code,
            Name = dto.DisplayName,
            Description = dto.Description,
            CreatedBy = createdBy
        };

        await _roleRepository.CreateAsync(role, cancellationToken);
        return MapToDto(role);
    }

    // 代码及系统字段保持服务端所有权，管理员角色不可修改。
    public async Task<RoleDto> UpdateRoleAsync(string roleId, RoleDto dto, string? updatedBy = null, CancellationToken cancellationToken = default)
    {
        var role = await _roleRepository.GetByIdAsync(roleId, cancellationToken);
        if (role == null)
        {
            throw new InvalidOperationException($"角色 '{roleId}' 不存在");
        }

        if (role.IsBuiltIn || LocalAdministratorCatalog.MatchesRoleCode(role.Code))
        {
            throw new InvalidOperationException("系统内置角色不允许修改");
        }

        // 单 DTO 不代表所有字段都可编辑；更新仍保持当前“代码不可改、系统字段不可回写”的边界。
        role.Name = dto.DisplayName;
        role.Description = dto.Description;
        role.IsEnabled = dto.IsEnabled;
        role.UpdatedAt = DateTime.UtcNow;
        role.UpdatedBy = updatedBy;

        await _roleRepository.UpdateAsync(role, cancellationToken);
        return MapToDto(role);
    }

    // 保留普通角色的关系清理语义，禁止删除管理员角色。
    public async Task DeleteRoleAsync(string roleId, CancellationToken cancellationToken = default)
    {
        var role = await _roleRepository.GetByIdAsync(roleId, cancellationToken);
        if (role == null)
        {
            throw new InvalidOperationException($"角色 '{roleId}' 不存在");
        }

        if (role.IsBuiltIn || LocalAdministratorCatalog.MatchesRoleCode(role.Code))
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

    private static RoleDto MapToDto(Role role)
    {
        return new RoleDto
        {
            Id = role.Id,
            Code = role.Code,
            DisplayName = role.Name,
            Description = role.Description,
            IsEnabled = role.IsEnabled,
            IsBuiltIn = role.IsBuiltIn,
            CreatedBy = role.CreatedBy ?? string.Empty,
            CreatedAt = role.CreatedAt,
            UpdatedBy = role.UpdatedBy ?? role.CreatedBy ?? string.Empty,
            UpdatedAt = role.UpdatedAt ?? role.CreatedAt
        };
    }
}
