namespace ICP.Platform.Modules.Identity.Models;

public sealed class LocalCredential
{
    public string EID { get; set; } = string.Empty;
    public string AccountEID { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public bool IsTemporary { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public string UpdatedBy { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; }
}
