namespace ICP.Platform.Modules.ConfigCenter.DTOs;

public class ConfigItemDto
{
    public string Id { get; init; } = string.Empty;
    public string Key { get; init; } = string.Empty;
    public string? Value { get; init; }
    public string Type { get; init; } = string.Empty;
    public string ModuleId { get; init; } = string.Empty;
    public string? Group { get; init; }
    public string? Environment { get; init; }
    public string? TenantId { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public string? Description { get; init; }
    public bool IsSensitive { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
    public string? UpdatedBy { get; init; }
}
