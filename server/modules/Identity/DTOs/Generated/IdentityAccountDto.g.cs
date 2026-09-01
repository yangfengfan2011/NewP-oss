#nullable enable annotations

using ICP.Platform.Modules.Identity.Models;

namespace ICP.Platform.Modules.Identity.DTOs;

public sealed class IdentityAccountDto
{
    public string Id { get; init; }
    public string CreatedBy { get; init; }
    public DateTime CreatedAt { get; init; }
    public string UpdatedBy { get; init; }
    public DateTime UpdatedAt { get; init; }
    public string DisplayName { get; init; }
    public string Name { get => DisplayName; init => DisplayName = value; }
    public string Code { get; init; }
    public string NormalizedECODE { get; init; }
    public ICP.Platform.Identity.Contracts.IdentityAccountStatus Status { get; init; }
    public bool IsLocalAdministrator { get; init; }
    public bool IsPersonnelBound { get; init; }
    public bool PersonnelDisableHold { get; init; }
    public long Revision { get; init; }
}
