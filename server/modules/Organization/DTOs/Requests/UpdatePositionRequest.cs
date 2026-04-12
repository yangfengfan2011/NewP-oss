using ICP.Platform.Modules.Organization.Models;

namespace ICP.Platform.Modules.Organization.DTOs.Requests;

public sealed record UpdatePositionRequest
{
    public required Guid DepartmentId { get; init; }
    public required string Name { get; init; }
    public EmploymentStatus EmploymentStatus { get; init; } = EmploymentStatus.FullTime;
    public bool IsEnabled { get; init; } = true;
    public int SortOrder { get; init; }
}
