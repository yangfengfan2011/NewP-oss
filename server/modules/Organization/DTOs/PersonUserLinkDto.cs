namespace ICP.Platform.Modules.Organization.DTOs;

public sealed record PersonUserLinkDto
{
    public Guid PersonId { get; init; }
    public string? PersonId_DisplayValue { get; init; }
    public string UserId { get; init; } = string.Empty;
    public string? UserId_DisplayValue { get; init; }
    public DateTime LinkedAt { get; init; }
    public string LinkedBy { get; init; } = string.Empty;
}
