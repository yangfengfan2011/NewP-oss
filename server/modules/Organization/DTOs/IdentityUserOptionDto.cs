namespace ICP.Platform.Modules.Organization.DTOs;

public sealed record IdentityUserOptionDto
{
    public required string UserId { get; init; }
    public required string DisplayName { get; init; }
}
