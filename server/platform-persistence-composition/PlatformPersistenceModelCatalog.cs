using ICP.Platform.Modeling.Definitions;
using ICP.Platform.Modules.Authorization.Persistence.Descriptors;
using ICP.Platform.Modules.ConfigCenter.Persistence.Descriptors;

namespace ICP.Platform.Persistence.Composition;

public static class PlatformPersistenceModelCatalog
{
    public static IReadOnlyList<ModelDefinition> GetDefinitions() =>
        ConfigCenterModelDefinitions.All
            .Concat(AuthorizationModelDefinitions.All)
            .Concat(GetOrganizationDefinitions())
            .ToArray();

    private static IReadOnlyList<ModelDefinition> GetOrganizationDefinitions()
    {
        var organizationDefinitionsType = Type.GetType(
            "ICP.Platform.Modules.Organization.Persistence.Descriptors.OrganizationModelDefinitions, OrganizationModule");

        var field = organizationDefinitionsType?.GetField("All");
        return field?.GetValue(null) as IReadOnlyList<ModelDefinition> ?? [];
    }
}
