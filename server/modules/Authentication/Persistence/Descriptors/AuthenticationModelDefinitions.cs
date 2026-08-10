using ICP.Platform.Modeling.Definitions;

namespace ICP.Platform.Modules.Authentication.Persistence.Descriptors;

public static class AuthenticationModelDefinitions
{
    private static readonly Lazy<IReadOnlyList<ModelDefinition>> Definitions = new(
        static () => new AuthenticationModelMetadataProvider().GetDefinitions());

    public static IReadOnlyList<ModelDefinition> All => Definitions.Value;
}
