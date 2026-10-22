namespace ICP.Platform.Modeling.Dto;

/// <summary>
/// 描述单个模型对应的 DTO 输出。
/// </summary>
public sealed record ModelDtoDescriptor(
    string Name,
    IReadOnlyList<ModelDtoProperty> Properties);
