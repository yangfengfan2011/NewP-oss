namespace ICP.Platform.Query.Descriptors;

/// <summary>
/// 声明一个可响应的 display value 字段，以及它依赖的真实模型字段与目标模型信息。
/// 这些元数据只描述字段归属，不承担跨模型解析或回滚逻辑。
/// </summary>
/// <param name="Field">对外响应字段名。</param>
/// <param name="SourceField">当前模型中存储引用标识的真实字段。</param>
/// <param name="TargetModelName">被引用模型名。</param>
/// <param name="TargetIdField">被引用模型标识字段。</param>
/// <param name="TargetDisplayField">被引用模型显示字段。</param>
public sealed record PlatformDisplayValueField(
    string Field,
    string SourceField,
    string TargetModelName,
    string TargetIdField,
    string TargetDisplayField);
