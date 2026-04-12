using ICP.Platform.Modules.Organization.Models;

namespace ICP.Platform.Modules.Organization.DTOs.Requests;

public sealed record CreatePersonRequest
{
    public required string PersonNo { get; init; }
    public required string Name { get; init; }
    public required Guid CompanyId { get; init; }
    public required Guid DepartmentId { get; init; }
    public Guid? PositionId { get; init; }
    public EmploymentStatus EmploymentStatus { get; init; } = EmploymentStatus.FullTime;
    public bool IsEnabled { get; init; } = true;
}
