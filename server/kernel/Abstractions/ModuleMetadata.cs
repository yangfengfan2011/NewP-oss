namespace ICP.Platform.Kernel.Abstractions;

/// <summary>
/// 模块元数据实现
/// </summary>
public class ModuleMetadata : IModuleMetadata
{
    public required string ModuleId { get; init; }
    public required string DisplayName { get; init; }
    public required string Version { get; init; }
    public string? Description { get; init; }
    public IReadOnlyList<string> Dependencies { get; init; } = Array.Empty<string>();
}
