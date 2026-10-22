#nullable enable annotations

namespace ICP.Platform.Modules.Workspace.DTOs;

public sealed class WorkspaceFolderContentDto
{
    public string LID { get; init; }
    public string PEID { get; init; }
    public string CEID { get; init; }
    public string? LCreatedBy { get; init; }
    public DateTime LCreatedAt { get; init; }
    public string? LUpdatedBy { get; init; }
    public DateTime? LUpdatedAt { get; init; }
    public bool? IsScopeNeutral { get; init; }
    public long? TreeOrdinal { get; init; }
    public string ChildModelKey { get; init; }
}
