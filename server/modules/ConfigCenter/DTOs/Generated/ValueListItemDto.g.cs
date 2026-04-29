#nullable enable annotations

namespace ICP.Platform.Modules.ConfigCenter.DTOs;

public sealed class ValueListItemDto
{
    public string Id { get; init; }
    public string? CreatedBy { get; init; }
    public DateTime CreatedAt { get; init; }
    public string? UpdatedBy { get; init; }
    public DateTime? UpdatedAt { get; init; }
    public string Code { get; init; }
    public string Name { get; init; }
    public string? Description { get; init; }
}
