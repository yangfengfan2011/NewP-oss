namespace ICP.Platform.Modules.Organization.DTOs;

public sealed record DepartmentDto
{
    public required Guid Id { get; init; }
    public required Guid CompanyId { get; init; }
    public Guid? ParentDepartmentId { get; init; }
    public required string Code { get; init; }
    public required string Name { get; init; }
    public bool IsEnabled { get; init; }
    public int SortOrder { get; init; }
    public DateTime CreatedAt { get; init; }
}
