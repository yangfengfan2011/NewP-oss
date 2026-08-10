namespace ICP.Platform.Modeling.Metadata;

/// <summary>
/// 模型 authoring 中的显式外键事实。
/// </summary>
public sealed class ModelMetadataForeignKey
{
    public string Name { get; init; } = string.Empty;

    public List<string> Fields { get; init; } = [];

    public string PrincipalModuleId { get; init; } = string.Empty;

    public string PrincipalModel { get; init; } = string.Empty;

    public List<string> PrincipalFields { get; init; } = [];

    public string OnDelete { get; init; } = "Restrict";
}
