using ICP.Platform.Kernel.Abstractions;
using ICP.Platform.Modules.Authorization.DTOs;
using ICP.Platform.Modules.Authorization.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ICP.Platform.HostApi.Authentication;

public sealed class LocalAuthSeeder
{
    private readonly IPermissionService _permissionService;
    private readonly IRoleService _roleService;
    private readonly LocalAuthOptions _options;
    private readonly ILogger<LocalAuthSeeder> _logger;

    public LocalAuthSeeder(
        IPermissionService permissionService,
        IRoleService roleService,
        IOptions<LocalAuthOptions> options,
        ILogger<LocalAuthSeeder> logger)
    {
        _permissionService = permissionService;
        _roleService = roleService;
        _options = options.Value;
        _logger = logger;
    }

    public async Task SeedAsync(
        IReadOnlyCollection<PermissionDescriptor> activePermissions,
        CancellationToken cancellationToken = default)
    {
        await SeedCatalogAsync(activePermissions, cancellationToken);

        await _roleService.AssignUserRolesAsync(_options.AdminUserId, new AssignUserRolesRequest
        {
            RoleCodes = new List<string> { _options.AdminRoleCode }
        }, "system", cancellationToken);
        var assignedRoleCodes = (await _roleService.GetUserRolesAsync(_options.AdminUserId, cancellationToken))
            .RoleCodes
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (!new[] { _options.AdminRoleCode }.SequenceEqual(assignedRoleCodes, StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                "AUTH_LOCAL_ADMIN_CATALOG_MISMATCH: local administrator roles did not converge to LocalAdmin only.");
        }

        _logger.LogInformation("Seeded local auth admin {UserName} with {PermissionCount} permissions.", _options.Username, activePermissions.Count);
    }

    public async Task SeedCatalogAsync(
        IReadOnlyCollection<PermissionDescriptor> activePermissions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(activePermissions);

        var permissionCodes = activePermissions
            .Select(static permission => permission.Code)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();
        await _permissionService.SyncPermissionsAsync(activePermissions, cancellationToken);

        var synchronizedCodes = (await _permissionService.GetAllPermissionsAsync(cancellationToken))
            .Select(static permission => permission.Code)
            .ToHashSet(StringComparer.Ordinal);
        var missingCodes = permissionCodes
            .Where(code => !synchronizedCodes.Contains(code))
            .ToArray();
        if (missingCodes.Length > 0)
        {
            throw new InvalidOperationException(
                $"AUTH_LOCAL_ADMIN_CATALOG_MISMATCH: active permissions were not synchronized: {string.Join(", ", missingCodes)}");
        }

        var role = await _roleService.GetRoleByCodeAsync(_options.AdminRoleCode, cancellationToken);
        if (role is null)
        {
            role = await _roleService.CreateRoleAsync(new RoleDto
            {
                Code = _options.AdminRoleCode,
                Name = _options.AdminRoleName,
                Description = "Local development administrator"
            }, "system", cancellationToken);
        }

        await _roleService.AssignRolePermissionsAsync(role.Id, new AssignRolePermissionsRequest
        {
            PermissionCodes = permissionCodes
        }, "system", cancellationToken);
        var assignedPermissionCodes = (await _roleService.GetRolePermissionsAsync(role.Id, cancellationToken))
            .PermissionCodes
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (!permissionCodes.SequenceEqual(assignedPermissionCodes, StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                "AUTH_LOCAL_ADMIN_CATALOG_MISMATCH: LocalAdmin role permissions did not converge to the active catalog.");
        }

        _logger.LogInformation("Synchronized LocalAdmin role with {PermissionCount} permissions.", permissionCodes.Count);
    }
}
