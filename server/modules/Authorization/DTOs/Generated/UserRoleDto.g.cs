#nullable enable annotations

namespace ICP.Platform.Modules.Authorization.DTOs;

public sealed class UserRoleDto
{
    public string Id { get; init; }
    public string? CreatedBy { get; init; }
    public DateTime CreatedAt { get; init; }
    public string? UpdatedBy { get; init; }
    public DateTime? UpdatedAt { get; init; }
    public string UserId { get; init; }
    public string RoleId { get; init; }
    public DateTime AssignedAt { get; init; }
    public string? AssignedBy { get; init; }
}
