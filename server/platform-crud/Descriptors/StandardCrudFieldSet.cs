namespace ICP.Platform.Crud.Descriptors;

public sealed record StandardCrudFieldSet(
    IReadOnlyList<string> ListFields,
    IReadOnlyList<string> DetailFields,
    IReadOnlyList<string> CreateFields,
    IReadOnlyList<string> UpdateFields);
