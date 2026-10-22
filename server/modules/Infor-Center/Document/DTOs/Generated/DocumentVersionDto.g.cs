#nullable enable annotations

namespace ICP.Platform.Modules.Document.DTOs;

public sealed class DocumentVersionDto
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
    public string DocumentId { get; init; }
    public int VersionOrdinal { get; init; }
    public string VersionLabel { get; init; }
    public string State { get; init; }
    public string? CurrentIterationId { get; init; }
}
