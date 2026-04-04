namespace ICP.Platform.Modules.Authorization.DTOs;

public record UserRoleDto
{
    public required string UserId { get; init; }
    public required List<string> RoleCodes { get; init; }
}

public record AssignUserRolesRequest
{
    public required List<string> RoleCodes { get; init; }
}
