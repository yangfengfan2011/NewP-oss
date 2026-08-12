using ICP.Platform.Modeling.Definitions;
using ICP.Platform.Modules.Authorization.Persistence.Descriptors;
using ICP.Platform.Modules.ConfigCenter.Persistence.Descriptors;

namespace ICP.Platform.Persistence.Composition;

/// <summary>
/// 返回当前由平台基础迁移流承载的内建模型。
/// </summary>
public static class PlatformFoundationModelSource
{
    /// <summary>
    /// 平台基础流只承载尚无独立 migration bundle 的 Authorization 与 ConfigCenter。
    /// </summary>
    public static IReadOnlyList<ModelDefinition> GetDefinitions() =>
        AdaptOwner(new AuthorizationModelMetadataProvider().GetDefinitions(), "Authorization")
            .Concat(AdaptOwner(new ConfigCenterModelMetadataProvider().GetDefinitions(), "ConfigCenter"))
            .ToArray();

    private static IEnumerable<ModelDefinition> AdaptOwner(
        IEnumerable<ModelDefinition> definitions,
        string ownerModuleId) =>
        definitions.Select(definition => definition with
        {
            OwnerModuleId = ownerModuleId,
            ForeignKeys = definition.ForeignKeys
                .Select(foreignKey => foreignKey with
                {
                    PrincipalModelKey = $"{ownerModuleId}/{GetModelName(foreignKey.PrincipalModelKey)}"
                })
                .ToArray()
        });

    private static string GetModelName(string logicalKey)
    {
        var separator = logicalKey.LastIndexOf('/');
        return separator >= 0 ? logicalKey[(separator + 1)..] : logicalKey;
    }
}
