#nullable enable annotations

namespace ICP.Platform.Modules.Document.DTOs;

public sealed class DocumentFileLinkDto
{
    public string LID { get; init; }
    public string PEID { get; init; }
    public string CEID { get; init; }
    public string? LCreatedBy { get; init; }
    public DateTime LCreatedAt { get; init; }
    public string? LUpdatedBy { get; init; }
    public DateTime? LUpdatedAt { get; init; }
    public string Role { get; init; }
    public int Ordinal { get; init; }
    public string FileUsageId { get; init; }
}
