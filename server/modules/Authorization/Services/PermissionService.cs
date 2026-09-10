using ICP.Platform.Crud.ObjectIds;
using ICP.Platform.Modules.Authorization.DTOs;
using ICP.Platform.Modules.Authorization.Models;
using ICP.Platform.Modules.Authorization.Repositories;

namespace ICP.Platform.Modules.Authorization.Services;

public class PermissionService : IPermissionService
{
    private readonly IPermissionRepository _permissionRepository;
    private readonly IPlatformObjectIdGenerator _objectIdGenerator;
    private readonly ActivePermissionCatalog _catalog;

    // 同步服务同时拥有活动目录发布，外部读取只消费活动代码。
    public PermissionService(
        IPermissionRepository permissionRepository,
        IPlatformObjectIdGenerator objectIdGenerator, ActivePermissionCatalog catalog)
    {
        _permissionRepository = permissionRepository;
        _objectIdGenerator = objectIdGenerator;
        _catalog = catalog;
    }

    // 读取活动权限目录，历史行不会扩大可配置集合。
    public async Task<List<PermissionDto>> GetAllPermissionsAsync(CancellationToken cancellationToken = default)
    {
        var permissions = await _permissionRepository.GetByCodesAsync(_catalog.Codes, cancellationToken);
        return permissions.Where(p => _catalog.Codes.Contains(p.Code)).Select(MapToDto).ToList();
    }

    // 仅对活动权限分组。
    public async Task<List<PermissionGroupDto>> GetPermissionsByModuleAsync(CancellationToken cancellationToken = default)
    {
        var permissions = await _permissionRepository.GetByCodesAsync(_catalog.Codes, cancellationToken);

        var grouped = permissions.Where(p => _catalog.Codes.Contains(p.Code))
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

    // 启动期同步后发布完整活动快照。
    public async Task SyncPermissionsAsync(IEnumerable<Kernel.Abstractions.PermissionDescriptor> descriptors, CancellationToken cancellationToken = default)
    {
        var active = descriptors.ToArray();
        var existingPermissions = await _permissionRepository.GetAllAsync(cancellationToken);
        var existingCodes = existingPermissions.ToDictionary(p => p.Code, p => p);

        foreach (var descriptor in active)
        {
            if (existingCodes.TryGetValue(descriptor.Code, out var existing))
            {
                if (MatchesDescriptor(existing, descriptor))
                {
                    // LocalAuth 每次启动都会收敛活动权限；跳过无变化写入，避免远端数据库上产生整表空更新。
                    continue;
                }

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
                    Id = _objectIdGenerator.NewId(nameof(Permission)),
                    Code = descriptor.Code,
                    DisplayName = descriptor.DisplayName,
                    Description = descriptor.Description,
                    ModuleId = descriptor.ModuleId,
                    Group = descriptor.Group
                };
                await _permissionRepository.CreateAsync(permission, cancellationToken);
            }
        }
        _catalog.Publish(active);
    }

    private static bool MatchesDescriptor(
        Permission permission,
        Kernel.Abstractions.PermissionDescriptor descriptor) =>
        string.Equals(permission.DisplayName, descriptor.DisplayName, StringComparison.Ordinal)
        && string.Equals(permission.Description, descriptor.Description, StringComparison.Ordinal)
        && string.Equals(permission.ModuleId, descriptor.ModuleId, StringComparison.Ordinal)
        && string.Equals(permission.Group, descriptor.Group, StringComparison.Ordinal);

    private static PermissionDto MapToDto(Permission permission)
    {
        return new PermissionDto
        {
            Id = permission.Id,
            Code = permission.Code,
            DisplayName = permission.DisplayName,
            Description = permission.Description,
            ModuleId = permission.ModuleId,
            Group = permission.Group,
            CreatedAt = permission.CreatedAt
        };
    }
}
