namespace ICP.Platform.Modules.Authorization.DTOs;

public record AssignUserRolesRequest
{
    public required List<string> RoleCodes { get; init; }
}
