#nullable enable annotations

namespace ICP.Platform.Modules.Authorization.DTOs;

public sealed class RolePermissionDto
{
    public string Id { get; init; }
    public string CreatedBy { get; init; }
    public DateTime CreatedAt { get; init; }
    public string UpdatedBy { get; init; }
    public DateTime UpdatedAt { get; init; }
    public string RoleId { get; init; }
    public string PermissionId { get; init; }
    public DateTime GrantedAt { get; init; }
    public string? GrantedBy { get; init; }
}
