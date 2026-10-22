#nullable enable annotations

namespace ICP.Platform.Modules.Organization.DTOs;

public sealed class PositionDto
{
    public string Id { get; init; }
    public string CreatedBy { get; init; }
    public string? CreatedBy_DisplayValue { get; init; }
    public DateTime CreatedAt { get; init; }
    public string? CreatedAt_DisplayValue { get; init; }
    public string UpdatedBy { get; init; }
    public string? UpdatedBy_DisplayValue { get; init; }
    public DateTime UpdatedAt { get; init; }
    public string? UpdatedAt_DisplayValue { get; init; }
    public string DisplayName { get; init; }
    public string Name { get => DisplayName; init => DisplayName = value; }
    public string? Description { get; init; }
    public bool IsEnabled { get; init; } = true;
}
