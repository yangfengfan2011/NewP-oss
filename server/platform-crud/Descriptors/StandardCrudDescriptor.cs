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
    public StandardCrudDeletePolicy DeletePolicy { get; init; } = StandardCrudDeletePolicy.None;
    public IReadOnlyList<string> OptionLabelFields { get; init; } = [];
    public IReadOnlyList<string> SummaryBlockKeys { get; init; } = [];
    public IReadOnlyList<string> DisplayValueFields { get; init; } = [];
}
