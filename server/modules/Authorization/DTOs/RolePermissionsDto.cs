namespace ICP.Platform.Modules.Authorization.DTOs;

public record RolePermissionsDto
{
    public required string RoleId { get; init; }
    public required List<string> PermissionCodes { get; init; }
}
