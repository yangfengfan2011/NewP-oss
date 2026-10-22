using ICP.Platform.Modeling.Definitions;

namespace ICP.Platform.Modules.Identity.Persistence.Descriptors;

public static class IdentityModelDefinitions
{
    private static readonly Lazy<IReadOnlyList<ModelDefinition>> Definitions = new(
        static () => new IdentityModelMetadataProvider().GetDefinitions());

    public static IReadOnlyList<ModelDefinition> All => Definitions.Value;
}
