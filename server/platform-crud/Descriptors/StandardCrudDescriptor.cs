using ICP.Platform.Query.Descriptors;

namespace ICP.Platform.Crud.Descriptors;

public sealed class StandardCrudDescriptor
{
    public required string ResourceKey { get; init; }
    public required string ModuleId { get; init; }
    public required string ModelName { get; init; }
    public required string Route { get; init; }
    public required string IdField { get; init; }
    public required string DisplayField { get; init; }
    public string? EnabledField { get; init; }
    public required StandardCrudFieldSet FieldSet { get; init; }
    public StandardCrudAuthorization? Authorization { get; init; }
    public StandardCrudDeletePolicy DeletePolicy { get; init; } = StandardCrudDeletePolicy.None;
    public IReadOnlyList<string> OptionLabelFields { get; init; } = [];
    public IReadOnlyList<string> SummaryBlockKeys { get; init; } = [];
    public IReadOnlyList<PlatformDisplayValueField> DisplayValueFields { get; init; } = [];

    /// <summary>
    /// 标准写入前置必填字段声明；这里只描述字段、作用范围和错误契约，缺失判断由运行时内核执行。
    /// </summary>
    public IReadOnlyList<StandardCrudRequiredField> RequiredFields { get; init; } = [];

    /// <summary>
    /// 标准写入前置唯一约束声明；这里只描述字段、作用域和错误契约，查重流程由运行时内核执行。
    /// </summary>
    public IReadOnlyList<StandardCrudUniqueConstraint> UniqueConstraints { get; init; } = [];
}
