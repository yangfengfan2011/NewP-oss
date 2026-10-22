namespace ICP.Platform.Crud.Models;

public sealed record StandardCrudSummaryBlock
{
    public required string Status { get; init; }
    public object? Data { get; init; }
}
