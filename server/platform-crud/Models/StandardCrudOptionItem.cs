namespace ICP.Platform.Crud.Models;

public sealed record StandardCrudOptionItem
{
    public required string Value { get; init; }
    public required string Label { get; init; }
}
