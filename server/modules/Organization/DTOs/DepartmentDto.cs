namespace ICP.Platform.Modules.Organization.DTOs;

public sealed record DepartmentDto
{
    public Guid Id { get; init; }
    public Guid CompanyId { get; init; }
    public string? CompanyId_DisplayValue { get; init; }
    public Guid? ParentDepartmentId { get; init; }
    public string? ParentDepartmentId_DisplayValue { get; init; }
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public bool IsEnabled { get; init; }
    public int SortOrder { get; init; }
    public DateTime CreatedAt { get; init; }
}
