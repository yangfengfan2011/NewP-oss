using ICP.Platform.Modules.Organization.Models;

namespace ICP.Platform.Modules.Organization.DTOs;

public sealed record PersonDetailDto
{
    public required string Id { get; init; }
    public required string PersonNo { get; init; }
    public required string Name { get; init; }
    public string? CompanyId { get; init; }
    public string? DepartmentId { get; init; }
    public string? PositionId { get; init; }
    public EmploymentStatus EmploymentStatus { get; init; }
    public bool IsEnabled { get; init; }
    public DateTime CreatedAt { get; init; }
    public PersonUserLinkDto? UserLink { get; init; }
    public PersonSummaryBlockDto UserLinkSummary { get; init; } = new() { Status = "notConfigured" };
    public PersonSummaryBlockDto AuthorizationSummary { get; init; } = new() { Status = "notConfigured" };
}
