#nullable enable annotations

namespace ICP.Platform.Modules.Document.DTOs;

public sealed class DocumentIterationDto
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
    public string Code { get; init; }
    public string VersionId { get; init; }
    public int IterationOrdinal { get; init; }
    public string Kind { get; init; }
    public string DatasetTypeKey { get; init; }
    public string? Description { get; init; }
}
