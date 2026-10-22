namespace ICP.Platform.ExternalPluginTemplate.DTOs;

public sealed class ExternalPluginStatusResponse
{
    public required string ModuleId { get; init; }
    public required string Message { get; init; }
}
