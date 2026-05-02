namespace ICP.Platform.Crud.Models;

public sealed record StandardCrudError
{
    public required int StatusCode { get; init; }
    public required string Code { get; init; }
    public required string Message { get; init; }
    public string? ReasonKey { get; init; }
    public IReadOnlyList<StandardCrudFieldError> FieldErrors { get; init; } = [];
    public IReadOnlyDictionary<string, object?> Args { get; init; }
        = new Dictionary<string, object?>(StringComparer.Ordinal);
}

public sealed record StandardCrudFieldError(
    string Field,
    string Code,
    string Message);
