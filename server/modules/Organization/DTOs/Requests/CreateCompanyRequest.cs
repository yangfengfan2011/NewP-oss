namespace ICP.Platform.Modules.Organization.DTOs.Requests;

public sealed record CreateCompanyRequest
{
    public required string Code { get; init; }
    public required string Name { get; init; }
    public int SortOrder { get; init; }
}
