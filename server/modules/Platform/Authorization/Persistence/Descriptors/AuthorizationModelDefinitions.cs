using ICP.Platform.Modeling.Definitions;

namespace ICP.Platform.Modules.Authorization.Persistence.Descriptors;

public static class AuthorizationModelDefinitions
{
    private static readonly Lazy<IReadOnlyList<ModelDefinition>> Definitions = new(
        static () => new AuthorizationModelMetadataProvider().GetDefinitions());

    /// <summary>
    /// 授权模块运行时模型定义。
    /// 该 legacy facade 只转发 metadata provider，避免重新形成手写定义真源。
    /// </summary>
    public static IReadOnlyList<ModelDefinition> All => Definitions.Value;
}
