using ICP.Platform.Modeling.Definitions;

namespace ICP.Platform.Modules.Organization.Persistence.Descriptors;

public static class OrganizationModelDefinitions
{
    private static readonly Lazy<IReadOnlyList<ModelDefinition>> Definitions = new(
        static () => new OrganizationModelMetadataProvider().GetDefinitions());

    /// <summary>
    /// 组织模块运行时模型定义。
    /// 该 legacy facade 只转发 metadata provider，避免重新形成手写定义真源。
    /// </summary>
    public static IReadOnlyList<ModelDefinition> All => Definitions.Value;
}
