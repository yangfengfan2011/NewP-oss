#nullable enable annotations

namespace ICP.Platform.Modules.Files.DTOs;

public sealed class FileCopyLeaseDto
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
    public string? FileId { get; init; }
    public string? SourceFileId { get; init; }
    public string? SourceUsageId { get; init; }
    public string? SourceOwnerId { get; init; }
    public string ActorId { get; init; }
    public string RequestKey { get; init; }
    public string? SourceOwnerKey { get; init; }
    public string UsageOwnerKey { get; init; }
    public string State { get; init; }
    public DateTime ExpiresAt { get; init; }
    public string? ConfirmedUsageId { get; init; }
}
