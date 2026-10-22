#nullable enable annotations

namespace ICP.Platform.Modules.Audit.DTOs;

public sealed class BusinessChangeDto
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
    public string EventType { get; init; }
    public string Resource { get; init; }
    public string TargetId { get; init; }
    public string ActorId { get; init; }
    public DateTime OccurredAt { get; init; }
    public string? OccurredAt_DisplayValue { get; init; }
    public string? IpAddress { get; init; }
    public string TraceId { get; init; }
    public string FieldsJson { get; init; }
}
