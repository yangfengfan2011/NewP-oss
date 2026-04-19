using ICP.Platform.Modules.Organization.Models;

namespace ICP.Platform.Modules.Organization.DTOs;

public sealed record PersonListItemDto
{
    public required Guid Id { get; init; }
    public required string PersonNo { get; init; }
    public required string Name { get; init; }
    public required string CompanyName { get; init; }
    public required string DepartmentName { get; init; }
    public required EmploymentStatus EmploymentStatus { get; init; }
    public required bool IsEnabled { get; init; }
}
