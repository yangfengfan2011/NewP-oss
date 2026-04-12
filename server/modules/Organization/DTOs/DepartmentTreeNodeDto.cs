namespace ICP.Platform.Modules.Organization.DTOs;

public sealed record DepartmentTreeNodeDto
{
    public required Guid Id { get; init; }
    public required string Code { get; init; }
    public required string Name { get; init; }
    public List<DepartmentTreeNodeDto> Children { get; init; } = [];
}
