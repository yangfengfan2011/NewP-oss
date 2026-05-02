namespace ICP.Platform.Crud.Models;

public sealed record StandardCrudQueryCondition(
    string Field,
    string Operator,
    object? Value);
