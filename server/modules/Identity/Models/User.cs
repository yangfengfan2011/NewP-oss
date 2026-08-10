using ICP.Platform.Identity.Contracts;

namespace ICP.Platform.Modules.Identity.Models;

public sealed class User
{
    public string EID { get; set; } = string.Empty;
    public string ECODE { get; set; } = string.Empty;
    public string NormalizedECODE { get; set; } = string.Empty;
    public string ENAME { get; set; } = string.Empty;
    public IdentityAccountStatus Status { get; set; }
    public bool IsLocalAdministrator { get; set; }
    public bool IsPersonnelBound { get; set; }
    public bool PersonnelDisableHold { get; set; }
    public long Revision { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public string UpdatedBy { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; }
}
