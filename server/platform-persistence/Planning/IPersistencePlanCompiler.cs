using ICP.Platform.Kernel.ModulePlanning;

namespace ICP.Platform.Persistence.Planning;

/// <summary>
/// 从候选 Canonical Module Plan 编译 provider-specific 持久化计划。
/// </summary>
public interface IPersistencePlanCompiler
{
    /// <summary>
    /// 把唯一候选模块计划编译为指定 provider 的共享运行时模型与迁移计划。
    /// </summary>
    /// <param name="modules">已经通过内核验证的 Canonical Module Plan。</param>
    /// <param name="provider">当前进程实际选择的持久化 provider。</param>
    /// <returns>完整计划，或不包含部分计划的稳定诊断集合。</returns>
    PersistencePlanCompilationResult Compile(
        CanonicalModulePlan modules,
        PersistenceProvider provider);
}
