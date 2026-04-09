namespace ICP.Platform.Modules.ConfigCenter.Models;

public class ValueListItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    public required string Code { get; set; }

    public required string Name { get; set; }

    public string? Description { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public string? UpdatedBy { get; set; }
}
