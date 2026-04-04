using ICP.Platform.Modules.Authorization.DTOs;
using ICP.Platform.Modules.Authorization.Models;
using ICP.Platform.Modules.Authorization.Repositories;

namespace ICP.Platform.Modules.Authorization.Services;

public class PermissionService : IPermissionService
{
    private readonly IPermissionRepository _permissionRepository;

    public PermissionService(IPermissionRepository permissionRepository)
    {
        _permissionRepository = permissionRepository;
    }

    public async Task<List<PermissionDto>> GetAllPermissionsAsync(CancellationToken cancellationToken = default)
    {
        var permissions = await _permissionRepository.GetAllAsync(cancellationToken);
        return permissions.Select(MapToDto).ToList();
    }

    public async Task<List<PermissionGroupDto>> GetPermissionsByModuleAsync(CancellationToken cancellationToken = default)
    {
        var permissions = await _permissionRepository.GetAllAsync(cancellationToken);

        var grouped = permissions
            .GroupBy(p => new { p.ModuleId, p.Group })
            .Select(g => new PermissionGroupDto
            {
                ModuleId = g.Key.ModuleId,
                Group = g.Key.Group,
                Permissions = g.Select(MapToDto).ToList()
            })
            .ToList();

        return grouped;
    }

    public async Task SyncPermissionsAsync(IEnumerable<Kernel.Abstractions.PermissionDescriptor> descriptors, CancellationToken cancellationToken = default)
    {
        var existingPermissions = await _permissionRepository.GetAllAsync(cancellationToken);
        var existingCodes = existingPermissions.ToDictionary(p => p.Code, p => p);

        foreach (var descriptor in descriptors)
        {
            if (existingCodes.TryGetValue(descriptor.Code, out var existing))
            {
                // 更新现有权限
                existing.DisplayName = descriptor.DisplayName;
                existing.Description = descriptor.Description;
                existing.ModuleId = descriptor.ModuleId;
                existing.Group = descriptor.Group;
                await _permissionRepository.UpdateAsync(existing, cancellationToken);
            }
            else
            {
                // 创建新权限
                var permission = new Permission
                {
                    Code = descriptor.Code,
                    DisplayName = descriptor.DisplayName,
                    Description = descriptor.Description,
                    ModuleId = descriptor.ModuleId,
                    Group = descriptor.Group
                };
                await _permissionRepository.CreateAsync(permission, cancellationToken);
            }
        }
    }

    private static PermissionDto MapToDto(Permission permission)
    {
        return new PermissionDto
        {
            Id = permission.Id,
            Code = permission.Code,
            DisplayName = permission.DisplayName,
            Description = permission.Description,
            ModuleId = permission.ModuleId,
            Group = permission.Group
        };
    }
}
