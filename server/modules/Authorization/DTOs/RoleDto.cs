namespace ICP.Platform.Modules.Authorization.DTOs;

public record RoleDto
{
    public string Id { get; init; } = string.Empty;
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public bool IsEnabled { get; init; }
    public bool IsBuiltIn { get; init; }
    public DateTime CreatedAt { get; init; }
}

public record RolePermissionsDto
{
    public required string RoleId { get; init; }
    public required List<string> PermissionCodes { get; init; }
}

public record AssignRolePermissionsRequest
{
    public required List<string> PermissionCodes { get; init; }
}
