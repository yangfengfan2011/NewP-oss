namespace ICP.Platform.Modules.Organization.Models;

public sealed class Person
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string PersonNo { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public Guid? CompanyId { get; set; }
    public Guid? DepartmentId { get; set; }
    public Guid? PositionId { get; set; }
    public EmploymentStatus EmploymentStatus { get; set; } = EmploymentStatus.FullTime;
    public bool IsEnabled { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}
