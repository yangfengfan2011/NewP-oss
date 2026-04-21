using ICP.Platform.Modules.Organization.Models;

namespace ICP.Platform.Modules.Organization.DTOs;

public sealed record PersonDto
{
    public Guid Id { get; init; }
    public string PersonNo { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public Guid? CompanyId { get; init; }
    public string? CompanyId_DisplayValue { get; init; }
    public Guid? DepartmentId { get; init; }
    public string? DepartmentId_DisplayValue { get; init; }
    public Guid? PositionId { get; init; }
    public string? PositionId_DisplayValue { get; init; }
    public EmploymentStatus? EmploymentStatus { get; init; }
    public string? EmploymentStatus_DisplayValue { get; init; }
    public bool IsEnabled { get; init; } = true;
    public DateTime CreatedAt { get; init; }
}
