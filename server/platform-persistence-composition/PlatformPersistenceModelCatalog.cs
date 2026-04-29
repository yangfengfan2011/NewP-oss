using ICP.Platform.Modeling.Definitions;
using ICP.Platform.Modules.Authorization.Persistence.Descriptors;
using ICP.Platform.Modules.ConfigCenter.Persistence.Descriptors;
using ICP.Platform.Modules.Organization.Persistence.Descriptors;

namespace ICP.Platform.Persistence.Composition;

public static class PlatformPersistenceModelCatalog
{
    /// <summary>
    /// 聚合平台内建模块 provider 投影后的持久化模型定义。
    /// </summary>
    public static IReadOnlyList<ModelDefinition> GetDefinitions() =>
        new IModelDefinitionProvider[]
        {
            new ConfigCenterModelMetadataProvider(),
            new AuthorizationModelMetadataProvider(),
            new OrganizationModelMetadataProvider()
        }
            // Catalog 只聚合 provider 的投影结果；模型真源读取和校验必须保留在各模块 provider 内。
            .SelectMany(static provider => provider.GetDefinitions())
            .ToArray();
}
