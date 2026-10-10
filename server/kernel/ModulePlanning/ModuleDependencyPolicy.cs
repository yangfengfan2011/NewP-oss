namespace ICP.Platform.Kernel.ModulePlanning;

/// <summary>共享活动组件之间的平台基础可用或显式业务依赖判定；不授予数据访问权限。</summary>
public static class ModuleDependencyPolicy
{
    /// <summary>两端都必须活动；同 owner、必需基础目标或来源的显式依赖允许能力引用。</summary>
    public static bool Allows(CanonicalModulePlan plan, string sourceModuleId, string targetModuleId)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var source = plan.Modules.SingleOrDefault(module => module.ModuleId == sourceModuleId);
        if (source is null || !plan.Modules.Any(module => module.ModuleId == targetModuleId)) return false;
        return sourceModuleId == targetModuleId
            || plan.RequiredModuleIds.Contains(targetModuleId, StringComparer.Ordinal)
            || source.Dependencies.Contains(targetModuleId, StringComparer.Ordinal);
    }
}
