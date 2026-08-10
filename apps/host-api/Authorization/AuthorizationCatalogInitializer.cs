using ICP.Platform.Kernel.Abstractions;
using ICP.Platform.Modules.Authorization.DTOs;
using ICP.Platform.Modules.Authorization.Services;

namespace ICP.Platform.HostApi.Authorization;

public sealed class AuthorizationCatalogInitializer
{
    public const string LocalAdministratorRoleCode = "LocalAdmin";
    private readonly IPermissionService _permissionService;
    private readonly IRoleService _roleService;
    private readonly ILogger<AuthorizationCatalogInitializer> _logger;

    public AuthorizationCatalogInitializer(
        IPermissionService permissionService,
        IRoleService roleService,
        ILogger<AuthorizationCatalogInitializer> logger)
    {
        _permissionService = permissionService;
        _roleService = roleService;
        _logger = logger;
    }

    public async Task InitializeAsync(
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
        var missingCodes = permissionCodes.Where(code => !synchronizedCodes.Contains(code)).ToArray();
        if (missingCodes.Length > 0)
        {
            throw new InvalidOperationException(
                $"AUTH_LOCAL_ADMIN_CATALOG_MISMATCH: active permissions were not synchronized: {string.Join(", ", missingCodes)}");
        }

        var role = await _roleService.GetRoleByCodeAsync(LocalAdministratorRoleCode, cancellationToken)
            ?? await _roleService.CreateRoleAsync(new RoleDto
            {
                Code = LocalAdministratorRoleCode,
                Name = "Local Administrator",
                Description = "Protected platform local administrator"
            }, "system", cancellationToken);
        await _roleService.AssignRolePermissionsAsync(role.Id, new AssignRolePermissionsRequest
        {
            PermissionCodes = permissionCodes
        }, "system", cancellationToken);
        var assigned = (await _roleService.GetRolePermissionsAsync(role.Id, cancellationToken))
            .PermissionCodes
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (!permissionCodes.SequenceEqual(assigned, StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                "AUTH_LOCAL_ADMIN_CATALOG_MISMATCH: LocalAdmin role permissions did not converge to the active catalog.");
        }

        _logger.LogInformation("Synchronized LocalAdmin role with {PermissionCount} permissions.", permissionCodes.Count);
    }
}
