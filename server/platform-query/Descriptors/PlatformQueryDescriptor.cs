namespace ICP.Platform.Query.Descriptors;

/// <summary>
/// 描述一个查询资源允许暴露的真实字段、默认字段和 display value 声明。
/// 它只表达边界，不承载查询执行或冲突处理流程。
/// </summary>
public sealed class PlatformQueryDescriptor
{
    /// <summary>
    /// 获取或设置查询资源唯一键，用于稳定错误信息和跨模块定位。
    /// </summary>
    public required string ResourceKey { get; init; }

    /// <summary>
    /// 获取或设置当前资源对应的模型名。
    /// </summary>
    public required string ModelName { get; init; }

    /// <summary>
    /// 获取或设置当前资源的主键字段名。
    /// </summary>
    public required string IdField { get; init; }

    /// <summary>
    /// 获取或设置允许参与查询的真实模型字段白名单。
    /// </summary>
    public required IReadOnlyList<string> AllowedModelFields { get; init; }

    /// <summary>
    /// 获取或设置允许响应的 display value 字段声明。
    /// </summary>
    public IReadOnlyList<PlatformDisplayValueField> DisplayValueFields { get; init; } = [];

    /// <summary>
    /// 获取或设置调用方未声明 fields 时采用的默认响应字段。
    /// </summary>
    public IReadOnlyList<string> DefaultFields { get; init; } = [];

    /// <summary>
    /// 获取或设置默认排序字段。
    /// </summary>
    public string? DefaultSortField { get; init; }
}
