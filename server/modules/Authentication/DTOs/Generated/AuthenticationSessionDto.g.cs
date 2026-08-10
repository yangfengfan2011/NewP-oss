#nullable enable annotations

using ICP.Platform.Modules.Authentication.Models;

namespace ICP.Platform.Modules.Authentication.DTOs;

public sealed class AuthenticationSessionDto
{
    public string Id { get; init; }
    public string CreatedBy { get; init; }
    public DateTime CreatedAt { get; init; }
    public string UpdatedBy { get; init; }
    public DateTime UpdatedAt { get; init; }
    public string UserEID { get; init; }
    public string TokenHash { get; init; }
    public ICP.Platform.Authentication.Contracts.AuthenticationSessionType SessionType { get; init; }
    public string? SessionType_DisplayValue { get; init; }
    public DateTime ExpiresAt { get; init; }
    public DateTime? RevokedAt { get; init; }
    public string? RevocationReason { get; init; }
}
