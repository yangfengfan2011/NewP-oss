#nullable enable annotations

namespace ICP.Platform.Modules.Example.DTOs;

public sealed class ExampleRecordDto
{
    public string Id { get; init; }
    public string CreatedBy { get; init; }
    public DateTime CreatedAt { get; init; }
    public string UpdatedBy { get; init; }
    public DateTime UpdatedAt { get; init; }
    public string DisplayName { get; init; }
    public string Name { get => DisplayName; init => DisplayName = value; }
    public string Code { get; init; }
    public string? ReviewerUserEID { get; init; }
    public string? ReviewerUserEID_DisplayValue { get; init; }
}
