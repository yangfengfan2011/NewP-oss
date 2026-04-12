using ICP.Platform.Modules.Organization.Models;

namespace ICP.Platform.Modules.Organization.DTOs.Requests;

public sealed record CreatePositionRequest
{
    public required Guid CompanyId { get; init; }
    public required Guid DepartmentId { get; init; }
    public required string Code { get; init; }
    public required string Name { get; init; }
    public EmploymentStatus EmploymentStatus { get; init; } = EmploymentStatus.FullTime;
    public int SortOrder { get; init; }
}
