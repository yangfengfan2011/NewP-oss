namespace ICP.Platform.Modules.Identity.Models;

public enum IdentitySecurityActorKind
{
    IdentityAccount,
    LocalMaintenance,
    System
}

public sealed class IdentitySecurityEvent
{
    public string EID { get; set; } = string.Empty;
    public string TargetAccountEID { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public DateTime OccurredAt { get; set; }
    public string TraceId { get; set; } = string.Empty;
    public IdentitySecurityActorKind ActorKind { get; set; }
    public string? ActorAccountEID { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public string UpdatedBy { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; }
}
