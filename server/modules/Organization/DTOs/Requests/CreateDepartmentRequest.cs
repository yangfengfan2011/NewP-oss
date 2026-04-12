namespace ICP.Platform.Modules.Organization.DTOs.Requests;

public sealed record CreateDepartmentRequest
{
    public required Guid CompanyId { get; init; }
    public Guid? ParentDepartmentId { get; init; }
    public required string Code { get; init; }
    public required string Name { get; init; }
    public int SortOrder { get; init; }
}
