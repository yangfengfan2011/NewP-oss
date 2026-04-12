using ICP.Platform.Modeling.Definitions;
using ICP.Platform.Modules.Authorization.Persistence.Descriptors;
using ICP.Platform.Modules.ConfigCenter.Persistence.Descriptors;
using ICP.Platform.Modules.Organization.Persistence.Descriptors;

namespace ICP.Platform.Persistence.Composition;

public static class PlatformPersistenceModelCatalog
{
    public static IReadOnlyList<ModelDefinition> GetDefinitions() =>
        ConfigCenterModelDefinitions.All
            .Concat(AuthorizationModelDefinitions.All)
            .Concat(OrganizationModelDefinitions.All)
            .ToArray();
}
