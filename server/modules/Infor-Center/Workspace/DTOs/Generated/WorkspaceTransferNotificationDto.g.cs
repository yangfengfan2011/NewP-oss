#nullable enable annotations

namespace ICP.Platform.Modules.Workspace.DTOs;

public sealed class WorkspaceTransferNotificationDto
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
    public string RecipientId { get; init; }
    public string SenderId { get; init; }
    public string ResourceKey { get; init; }
    public string ObjectId { get; init; }
    public string SpaceId { get; init; }
    public string DirectoryId { get; init; }
    public string LinkId { get; init; }
}
