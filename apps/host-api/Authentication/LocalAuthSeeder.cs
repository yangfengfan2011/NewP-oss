using ICP.Platform.Kernel.ModuleLoader;
using ICP.Platform.Modules.Authorization.DTOs;
using ICP.Platform.Modules.Authorization.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ICP.Platform.HostApi.Authentication;

public sealed class LocalAuthSeeder
{
    private readonly ModuleRegistry _moduleRegistry;
    private readonly IPermissionService _permissionService;
    private readonly IRoleService _roleService;
    private readonly LocalAuthOptions _options;
    private readonly ILogger<LocalAuthSeeder> _logger;

    public LocalAuthSeeder(
        ModuleRegistry moduleRegistry,
        IPermissionService permissionService,
        IRoleService roleService,
        IOptions<LocalAuthOptions> options,
        ILogger<LocalAuthSeeder> logger)
    {
        _moduleRegistry = moduleRegistry;
        _permissionService = permissionService;
        _roleService = roleService;
        _options = options.Value;
        _logger = logger;
    }

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        var permissions = _moduleRegistry.GetAllPermissions();
        await _permissionService.SyncPermissionsAsync(permissions, cancellationToken);

        var role = await _roleService.GetRoleByCodeAsync(_options.AdminRoleCode, cancellationToken);
        if (role is null)
        {
            role = await _roleService.CreateRoleAsync(new CreateRoleRequest
            {
                Code = _options.AdminRoleCode,
                Name = _options.AdminRoleName,
                Description = "Local development administrator"
            }, "system", cancellationToken);
        }

        var permissionCodes = permissions.Select(permission => permission.Code).Distinct().ToList();
        await _roleService.AssignRolePermissionsAsync(role.Id, new AssignRolePermissionsRequest
        {
            PermissionCodes = permissionCodes
        }, "system", cancellationToken);

        await _roleService.AssignUserRolesAsync(_options.AdminUserId, new AssignUserRolesRequest
        {
            RoleCodes = new List<string> { _options.AdminRoleCode }
        }, "system", cancellationToken);

        _logger.LogInformation("Seeded local auth admin {UserName} with {PermissionCount} permissions.", _options.Username, permissionCodes.Count);
    }
}
