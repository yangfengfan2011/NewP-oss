using ICP.Platform.Authentication.Contracts;

namespace ICP.Platform.Modules.Authentication.Models;

public sealed class AuthenticationSession
{
    public string EID { get; set; } = string.Empty;
    public string AccountEID { get; set; } = string.Empty;
    public string TokenHash { get; set; } = string.Empty;
    public AuthenticationSessionType SessionType { get; set; }
    public string TimeZoneId { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public string? RevocationReason { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public string UpdatedBy { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; }
}
