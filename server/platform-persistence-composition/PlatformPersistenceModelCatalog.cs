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
    /// 为 SQL Server 统一迁移保留已冻结模型的历史表形态，只对新身份基础模型启用严格审计约束。
    /// </summary>
    public static IReadOnlyList<ModelDefinition> AdaptSqlServerMigrationBaseline(
        IEnumerable<ModelDefinition> legacyDefinitions)
    {
        ArgumentNullException.ThrowIfNull(legacyDefinitions);
        return legacyDefinitions
            .Select(static definition =>
            {
                var isIdentityFoundation = definition.OwnerModuleId is "Identity" or "Authentication";
                return definition with
                {
                    // 空 owner 是设计期兼容标记：让 EF 继续使用冻结快照中的裸实体名，
                    // 避免 #31 的新增迁移顺带重写全部历史模型。
                    OwnerModuleId = isIdentityFoundation ? "platform.core" : string.Empty,
                    SchemaName = isIdentityFoundation ? "dbo" : string.Empty,
                    Fields = isIdentityFoundation
                        ? definition.Fields
                        : definition.Fields
                            .Select(static field => field.Name is "CreatedBy" or "UpdatedBy"
                                ? field with { Required = false }
                                : field)
                            .ToArray(),
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
