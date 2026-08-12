using ICP.Platform.Modeling.Definitions;
using ICP.Platform.Persistence.Planning;

namespace ICP.Platform.Persistence.Composition;

/// <summary>
/// 把冻结的统一 migrations baseline 显式适配为 platform.core 归属；不发现或枚举模块真源。
/// </summary>
public static class PlatformPersistenceModelCatalog
{
    /// <summary>
    /// 把冻结统一 migrations 的设计期模型适配到显式 platform.core 归属。
    /// </summary>
    public static IReadOnlyList<ModelDefinition> AdaptPlatformCore(
        IEnumerable<ModelDefinition> legacyDefinitions)
    {
        ArgumentNullException.ThrowIfNull(legacyDefinitions);
        return legacyDefinitions
            .Select(static definition => definition with
            {
                OwnerModuleId = "platform.core",
                SchemaName = "dbo",
                ForeignKeys = definition.ForeignKeys
                    .Select(static foreignKey => foreignKey with
                    {
                        PrincipalModelKey = $"platform.core/{GetModelName(foreignKey.PrincipalModelKey)}"
                    })
                    .ToArray()
            })
            .OrderBy(static definition => definition.LogicalKey, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>
    /// 为 SQL Server 平台基础迁移生成当前物理模型。设计期实体名保持未限定，
    /// 但列可空性和其他 facet 必须与当前运行时目标一致。
    /// </summary>
    public static IReadOnlyList<ModelDefinition> AdaptSqlServerPlatformFoundation(
        IEnumerable<ModelDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        return definitions
            .Select(static definition =>
            {
                return definition with
                {
                    // 设计期 migration 使用裸实体名，运行时归属仍由 Compiled Plan 保留。
                    OwnerModuleId = string.Empty,
                    SchemaName = string.Empty,
                    ForeignKeys = definition.ForeignKeys
                        .Select(static foreignKey => foreignKey with
                        {
                            PrincipalModelKey = $"platform.core/{GetModelName(foreignKey.PrincipalModelKey)}"
                        })
                        .ToArray()
                };
            })
            .OrderBy(static definition => definition.LogicalKey, StringComparer.Ordinal)
            .ToArray();
    }

    private static string GetModelName(string logicalKey)
    {
        var separator = logicalKey.LastIndexOf('/');
        return separator >= 0 ? logicalKey[(separator + 1)..] : logicalKey;
    }

    /// <summary>
    /// 返回已编译计划中的唯一运行时模型集合。
    /// </summary>
    public static IReadOnlyList<ModelDefinition> GetDefinitions(CompiledPersistencePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return plan.Models;
    }
}
