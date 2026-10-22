#nullable enable annotations

namespace ICP.Platform.Modules.Authorization.DTOs;

public sealed class DataScopeObjectActionDto
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
    public string EntryId { get; init; }
    public string ActionKey { get; init; }
    public string Opinion { get; init; }
}
