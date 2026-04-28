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
