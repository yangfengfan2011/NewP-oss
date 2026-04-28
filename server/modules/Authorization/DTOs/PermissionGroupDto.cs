namespace ICP.Platform.Modules.Authorization.DTOs;

public record PermissionGroupDto
{
    public required string ModuleId { get; init; }
    public string? Group { get; init; }
    public required List<PermissionDto> Permissions { get; init; }
}
