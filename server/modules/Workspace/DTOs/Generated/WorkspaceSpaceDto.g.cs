#nullable enable annotations

namespace ICP.Platform.Modules.Workspace.DTOs;

public sealed class WorkspaceSpaceDto
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
    public string DisplayName { get; init; }
    public string Name { get => DisplayName; init => DisplayName = value; }
    public string SpaceKind { get; init; }
    public string? SubjectModelKey { get; init; }
    public string? SubjectId { get; init; }
    public string RootFolderId { get; init; }
}
