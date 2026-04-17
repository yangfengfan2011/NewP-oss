namespace ICP.Platform.Modules.Organization.DTOs;

public sealed record CompanyDto
{
    public Guid Id { get; init; }
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public bool IsEnabled { get; init; }
    public int SortOrder { get; init; }
    public DateTime CreatedAt { get; init; }
}
