namespace ICP.Platform.Modules.Organization.DTOs;

public sealed record PersonUserLinkDto
{
    public required Guid PersonId { get; init; }
    public required string UserId { get; init; }
    public DateTime LinkedAt { get; init; }
    public required string LinkedBy { get; init; }
}
