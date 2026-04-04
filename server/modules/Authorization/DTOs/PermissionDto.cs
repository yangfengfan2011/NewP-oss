namespace ICP.Platform.Modules.Authorization.DTOs;

public record PermissionDto
{
    public required string Id { get; init; }
    public required string Code { get; init; }
    public required string DisplayName { get; init; }
    public string? Description { get; init; }
    public required string ModuleId { get; init; }
    public string? Group { get; init; }
}

public record PermissionGroupDto
{
    public required string ModuleId { get; init; }
    public string? Group { get; init; }
    public required List<PermissionDto> Permissions { get; init; }
}
