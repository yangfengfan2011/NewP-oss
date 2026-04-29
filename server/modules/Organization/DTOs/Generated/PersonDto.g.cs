#nullable enable annotations

using ICP.Platform.Modules.Organization.Models;

namespace ICP.Platform.Modules.Organization.DTOs;

public sealed class PersonDto
{
    public string Id { get; init; }
    public string? CreatedBy { get; init; }
    public DateTime CreatedAt { get; init; }
    public string? UpdatedBy { get; init; }
    public DateTime? UpdatedAt { get; init; }
    public string DisplayName { get; init; }
    public string PersonNo { get; init; }
    public string Name { get; init; }
    public string? CompanyId { get; init; }
    public string? DepartmentId { get; init; }
    public string? PositionId { get; init; }
    public EmploymentStatus? EmploymentStatus { get; init; }
    public string? EmploymentStatus_DisplayValue { get; init; }
    public bool IsEnabled { get; init; } = true;
}
