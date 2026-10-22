using System.Collections.Generic;

namespace ICP.Platform.Modules.ConfigCenter.DTOs;

public class ConfigGroupDto
{
    public required string ModuleId { get; set; }

    public string? Group { get; set; }

    public List<ConfigItemDto> Items { get; set; } = new();
}
