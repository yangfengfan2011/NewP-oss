namespace ICP.Platform.Modeling.Dto;

/// <summary>
/// 描述单个 DTO 属性的生成信息。
/// </summary>
public sealed record ModelDtoProperty(
    string PropertyName,
    string TypeName,
    bool Nullable);
