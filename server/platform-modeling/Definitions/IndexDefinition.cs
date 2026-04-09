namespace ICP.Platform.Modeling.Definitions;

public sealed record IndexDefinition(
    string Name,
    IReadOnlyList<string> Fields,
    bool IsUnique);
