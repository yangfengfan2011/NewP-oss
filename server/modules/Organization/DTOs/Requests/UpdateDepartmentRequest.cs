namespace ICP.Platform.Modules.Organization.DTOs.Requests;

public sealed record UpdateDepartmentRequest
{
    public required Guid CompanyId { get; init; }
    public Guid? ParentDepartmentId { get; init; }
    public required string Name { get; init; }
    public bool IsEnabled { get; init; } = true;
    public int SortOrder { get; init; }
}
