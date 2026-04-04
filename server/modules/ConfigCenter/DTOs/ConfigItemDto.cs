namespace ICP.Platform.Modules.ConfigCenter.DTOs;

public class ConfigItemDto
{
    public required string Id { get; set; }
    public required string Key { get; set; }
    public string? Value { get; set; }
    public required string Type { get; set; }
    public required string ModuleId { get; set; }
    public string? Group { get; set; }
    public string? Environment { get; set; }
    public string? TenantId { get; set; }
    public required string DisplayName { get; set; }
    public string? Description { get; set; }
    public bool IsSensitive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}

public class ConfigItemCreateDto
{
    public required string Key { get; set; }
    public string? Value { get; set; }
    public required string Type { get; set; }
    public required string ModuleId { get; set; }
    public string? Group { get; set; }
    public string? Environment { get; set; }
    public string? TenantId { get; set; }
    public required string DisplayName { get; set; }
    public string? Description { get; set; }
    public bool IsSensitive { get; set; }
}

public class ConfigItemUpdateDto
{
    public string? Value { get; set; }
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
}

public class ConfigGroupDto
{
    public required string ModuleId { get; set; }
    public string? Group { get; set; }
    public List<ConfigItemDto> Items { get; set; } = new();
}
