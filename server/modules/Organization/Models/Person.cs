namespace ICP.Platform.Modules.Organization.Models;

public sealed class Person
{
    public string Id { get; set; } = string.Empty;
    public string PersonNo { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? CompanyId { get; set; }
    public string? DepartmentId { get; set; }
    public string? PositionId { get; set; }
    public EmploymentStatus EmploymentStatus { get; set; } = EmploymentStatus.FullTime;
    public bool IsEnabled { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}
