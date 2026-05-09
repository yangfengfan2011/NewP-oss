namespace ICP.Platform.Modules.Organization.Models;

public sealed class Position
{
    public string Id { get; set; } = string.Empty;
    public string CompanyId { get; set; } = string.Empty;
    public string DepartmentId { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public EmploymentStatus EmploymentStatus { get; set; } = EmploymentStatus.FullTime;
    public bool IsEnabled { get; set; } = true;
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}
