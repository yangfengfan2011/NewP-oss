using ICP.Platform.Modeling.Definitions;
using ICP.Platform.Modules.Authorization.Persistence.Descriptors;
using ICP.Platform.Modules.ConfigCenter.Persistence.Descriptors;
using ICP.Platform.Modules.Organization.Persistence.Descriptors;

namespace ICP.Platform.Persistence.Composition;

/// <summary>
/// Oracle 与 MySQL 既有平台迁移程序集的设计期模型来源。
/// </summary>
public static class NonSqlPlatformBaselineModelSource
{
    /// <summary>
    /// 仅为尚未重建基线的非 SQL Server provider 聚合现有模型。
    /// </summary>
    public static IReadOnlyList<ModelDefinition> GetDefinitions() =>
        new IModelDefinitionProvider[]
        {
            new ConfigCenterModelMetadataProvider(),
            new AuthorizationModelMetadataProvider(),
            new OrganizationModelMetadataProvider()
        }
            .SelectMany(static provider => provider.GetDefinitions())
            .ToArray();
}
