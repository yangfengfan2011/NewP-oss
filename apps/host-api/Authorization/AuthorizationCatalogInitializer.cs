using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Kernel.Abstractions;
using ICP.Platform.Modules.Authorization.DTOs;
using ICP.Platform.Modules.Authorization.Services;

namespace ICP.Platform.HostApi.Authorization;

public sealed class AuthorizationCatalogInitializer
{
    public const string LocalAdministratorRoleCode = "LocalAdmin";
    private readonly IPermissionService _permissionService;
    private readonly ILocalAdministratorCatalog _catalog;
    private readonly ILogger<AuthorizationCatalogInitializer> _logger;

    // 只组合启动期目录入口，普通角色命令不参与初始化。
    public AuthorizationCatalogInitializer(
        IPermissionService permissionService,
        ILocalAdministratorCatalog catalog,
        ILogger<AuthorizationCatalogInitializer> logger)
    {
        _permissionService = permissionService;
        _catalog = catalog;
        _logger = logger;
    }

    // 仅将正式活动计划发布到权限目录与管理员全集。
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

        await _catalog.SynchronizeAsync(permissionCodes, cancellationToken);

        _logger.LogInformation("Synchronized LocalAdmin role with {PermissionCount} permissions.", permissionCodes.Count);
    }
}
