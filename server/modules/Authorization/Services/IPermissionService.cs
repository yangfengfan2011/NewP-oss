using ICP.Platform.Modules.Authorization.DTOs;

namespace ICP.Platform.Modules.Authorization.Services;

public interface IPermissionService
{
    Task<List<PermissionDto>> GetAllPermissionsAsync(CancellationToken cancellationToken = default);
    Task<List<PermissionGroupDto>> GetPermissionsByModuleAsync(CancellationToken cancellationToken = default);
    Task SyncPermissionsAsync(IEnumerable<Kernel.Abstractions.PermissionDescriptor> descriptors, CancellationToken cancellationToken = default);
}
