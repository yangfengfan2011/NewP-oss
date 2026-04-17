using ICP.Platform.Modules.Organization.Models;

namespace ICP.Platform.Modules.Organization.DTOs;

public sealed record PositionDto
{
    public Guid Id { get; init; }
    public Guid CompanyId { get; init; }
    public string? CompanyId_DisplayValue { get; init; }
    public Guid DepartmentId { get; init; }
    public string? DepartmentId_DisplayValue { get; init; }
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public EmploymentStatus EmploymentStatus { get; init; } = EmploymentStatus.FullTime;
    public string? EmploymentStatus_DisplayValue { get; init; }
    public bool IsEnabled { get; init; }
    public int SortOrder { get; init; }
    public DateTime CreatedAt { get; init; }
}
