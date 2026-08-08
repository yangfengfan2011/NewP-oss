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
                SchemaName = "dbo"
            })
            .OrderBy(static definition => definition.LogicalKey, StringComparer.Ordinal)
            .ToArray();
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
