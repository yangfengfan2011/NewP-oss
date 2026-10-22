using ICP.Platform.Modeling.Definitions;

namespace ICP.Platform.Modules.Example.Persistence.Descriptors;

public static class ExampleModelDefinitions
{
    private static readonly Lazy<IReadOnlyList<ModelDefinition>> Definitions = new(
        static () => new ExampleModelMetadataProvider().GetDefinitions());

    public static IReadOnlyList<ModelDefinition> All => Definitions.Value;
}
