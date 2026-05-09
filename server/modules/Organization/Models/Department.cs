namespace ICP.Platform.Modules.Organization.Models;

public sealed class Department
{
    public string Id { get; set; } = string.Empty;
    public string CompanyId { get; set; } = string.Empty;
    public string? ParentDepartmentId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}
