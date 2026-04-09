namespace ICP.Platform.Modules.ConfigCenter.DTOs;

public class ValueListItemDto
{
    public required string Id { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}

public class ValueListItemCreateDto
{
    public required string Code { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
}

public class ValueListItemUpdateDto
{
    public required string Code { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
}
