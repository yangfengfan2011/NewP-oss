namespace ICP.Platform.Modules.Organization.Models;

public sealed class PersonUserLink
{
    public string PersonId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public DateTime LinkedAt { get; set; } = DateTime.UtcNow;
    public string LinkedBy { get; set; } = "system";
}
