namespace ICP.Platform.Modules.Organization.DTOs.Requests;

public sealed record UpdateCompanyRequest
{
    public required string Name { get; init; }
    public bool IsEnabled { get; init; } = true;
    public int SortOrder { get; init; }
}
