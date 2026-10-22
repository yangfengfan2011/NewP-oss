namespace ICP.Platform.Modules.Organization.DTOs;

public sealed record DepartmentTreeNodeDto
{
    public required string Id { get; init; }
    public required string Code { get; init; }
    public required string Name { get; init; }
    public string? ParentDepartmentId { get; init; }
    public List<DepartmentTreeNodeDto> Children { get; init; } = [];
}
