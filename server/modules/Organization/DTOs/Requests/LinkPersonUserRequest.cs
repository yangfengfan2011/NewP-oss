namespace ICP.Platform.Modules.Organization.DTOs.Requests;

public sealed record LinkPersonUserRequest
{
    public required string UserId { get; init; }
    public string LinkedBy { get; init; } = "system";
}
