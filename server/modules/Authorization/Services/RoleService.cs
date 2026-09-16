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
    private readonly IUserRoleAssignments _userAssignments;

    // 组合角色维护与成员关系写入；初始化使用独立入口。
    public RoleService(
        IRoleRepository roleRepository,
        IRolePermissionRepository rolePermissionRepository,
        IUserRoleRepository userRoleRepository,
        IPlatformObjectIdGenerator objectIdGenerator,
        IUserRoleAssignments userAssignments)
    {
        _roleRepository = roleRepository;
        _rolePermissionRepository = rolePermissionRepository;
        _userRoleRepository = userRoleRepository;
        _objectIdGenerator = objectIdGenerator;
        _userAssignments = userAssignments;
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

    // 用户关系只通过完整校验及事务性写入边界访问。
    public Task<UserRoleAssignmentsDto> GetUserRolesAsync(string userId, CancellationToken cancellationToken = default) =>
        _userAssignments.GetAsync(userId, cancellationToken);

    // 操作者由可信请求上下文产生，调用者字符串不进入审计。
    public Task AssignUserRolesAsync(string userId, AssignUserRolesRequest request, string? assignedBy = null, CancellationToken cancellationToken = default) =>
        _userAssignments.ReplaceAsync(userId, request.RoleCodes, cancellationToken);
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
