#nullable enable annotations

namespace ICP.Platform.Modules.Organization.DTOs;

public sealed class PersonUserLinkDto
{
    public string Id { get; init; }
    public string CreatedBy { get; init; }
    public DateTime CreatedAt { get; init; }
    public string UpdatedBy { get; init; }
    public DateTime UpdatedAt { get; init; }
    public string PersonId { get; init; }
    public string UserId { get; init; }
    public DateTime LinkedAt { get; init; }
    public string LinkedBy { get; init; }
}
