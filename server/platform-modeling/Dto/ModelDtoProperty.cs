namespace ICP.Platform.Modeling.Dto;

/// <summary>
/// 描述单个 DTO 属性的生成信息。
/// </summary>
// PropertyName 表示最终对外输出的属性名，必要时可与原字段名保持独立。
public sealed record ModelDtoProperty(
    string PropertyName,
    string TypeName,
    bool Nullable);
