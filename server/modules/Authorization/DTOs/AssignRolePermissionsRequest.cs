namespace ICP.Platform.Modules.Authorization.DTOs;

public record AssignRolePermissionsRequest
{
    public required List<string> PermissionCodes { get; init; }
}
