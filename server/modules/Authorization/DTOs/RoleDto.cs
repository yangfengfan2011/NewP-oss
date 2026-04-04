namespace ICP.Platform.Modules.Authorization.DTOs;

public record RoleDto
{
    public required string Id { get; init; }
    public required string Code { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public bool IsEnabled { get; init; }
    public bool IsBuiltIn { get; init; }
    public DateTime CreatedAt { get; init; }
}

public record CreateRoleRequest
{
    public required string Code { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
}

public record UpdateRoleRequest
{
    public required string Name { get; init; }
    public string? Description { get; init; }
    public bool IsEnabled { get; init; }
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
