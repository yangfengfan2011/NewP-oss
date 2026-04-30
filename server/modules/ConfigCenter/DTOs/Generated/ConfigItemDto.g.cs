#nullable enable annotations

namespace ICP.Platform.Modules.ConfigCenter.DTOs;

public sealed class ConfigItemDto
{
    public string Id { get; init; }
    public string? CreatedBy { get; init; }
    public DateTime CreatedAt { get; init; }
    public string? UpdatedBy { get; init; }
    public DateTime? UpdatedAt { get; init; }
    public string DisplayName { get; init; }
    public string Name { get => DisplayName; init => DisplayName = value; }
    public string Key { get; init; }
    public string? Value { get; init; }
    public string Type { get; init; }
    public string ModuleId { get; init; }
    public string? Group { get; init; }
    public string? Environment { get; init; }
    public string? TenantId { get; init; }
    public string? Description { get; init; }
    public bool IsSensitive { get; init; }
}
