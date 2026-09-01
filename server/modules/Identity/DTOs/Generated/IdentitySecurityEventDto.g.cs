#nullable enable annotations

using ICP.Platform.Modules.Identity.Models;

namespace ICP.Platform.Modules.Identity.DTOs;

public sealed class IdentitySecurityEventDto
{
    public string Id { get; init; }
    public string CreatedBy { get; init; }
    public DateTime CreatedAt { get; init; }
    public string UpdatedBy { get; init; }
    public DateTime UpdatedAt { get; init; }
    public string TargetAccountEID { get; init; }
    public string EventType { get; init; }
    public string Reason { get; init; }
    public DateTime OccurredAt { get; init; }
    public string? OccurredAt_DisplayValue { get; init; }
    public string TraceId { get; init; }
    public IdentitySecurityActorKind ActorKind { get; init; }
    public string? ActorAccountEID { get; init; }
}
