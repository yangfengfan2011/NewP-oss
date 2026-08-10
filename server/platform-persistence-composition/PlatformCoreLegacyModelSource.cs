using ICP.Platform.Modeling.Definitions;
using ICP.Platform.Modules.Authorization.Persistence.Descriptors;
using ICP.Platform.Modules.Authentication.Persistence.Descriptors;
using ICP.Platform.Modules.ConfigCenter.Persistence.Descriptors;
using ICP.Platform.Modules.Identity.Persistence.Descriptors;
using ICP.Platform.Modules.Organization.Persistence.Descriptors;

namespace ICP.Platform.Persistence.Composition;

/// <summary>
/// 冻结的 platform.core 统一 migrations 设计期模型来源；仅供 legacy baseline、工具与迁移测试使用。
/// </summary>
public static class PlatformCoreLegacyModelSource
{
    /// <summary>
    /// 仅为现有统一 migrations、设计期工具和迁移测试聚合冻结模型。
    /// </summary>
    public static IReadOnlyList<ModelDefinition> GetDefinitions() =>
        new IModelDefinitionProvider[]
        {
            new ConfigCenterModelMetadataProvider(),
            new AuthorizationModelMetadataProvider(),
            new OrganizationModelMetadataProvider(),
            new IdentityModelMetadataProvider(),
            new AuthenticationModelMetadataProvider()
        }
            .SelectMany(static provider => provider.GetDefinitions())
            .ToArray();
}
