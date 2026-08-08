using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Persistence.Planning;
using ICP.Platform.PluginPlatform.Lifecycle;

namespace ICP.Platform.PluginPlatform.Activation;

/// <summary>
/// 在任何候选计划编译前验证安装包、Platform API release 与 profile。
/// </summary>
public interface IPluginPackagePreflight
{
    void Validate(
        IReadOnlyList<InstalledPluginInventoryItem> inventory,
        ResolvedPluginLifecycleState state);
}

/// <summary>
/// 从 desired state 产生整份 Canonical Module Plan。
/// </summary>
public interface IPluginCandidatePlanCompiler
{
    CanonicalModulePlan Compile(
        IReadOnlyList<InstalledPluginInventoryItem> inventory,
        ResolvedPluginLifecycleState state);
}

/// <summary>
/// 在 Host 组合前验证候选计划的有效权限目录。
/// </summary>
public interface IPluginPermissionCatalogValidator
{
    void Validate(CanonicalModulePlan plan);
}

/// <summary>
/// 从同一个 Canonical Module Plan 编译唯一持久化计划实例。
/// </summary>
public interface IPluginPersistencePlanCompiler
{
    CompiledPersistencePlan Compile(CanonicalModulePlan modulePlan);
}

/// <summary>
/// 执行候选持久化计划；失败后 DatabaseAhead 指示是否已有安全前缀提交。
/// </summary>
public interface IPluginMigrationExecutor
{
    bool DatabaseAhead { get; }

    Task ExecuteAsync(CompiledPersistencePlan plan, CancellationToken cancellationToken);
}

/// <summary>
/// 把候选活动权限同步到运行时依赖，但不提交活动快照。
/// </summary>
public interface IPluginPermissionSynchronizer
{
    Task SynchronizeAsync(CanonicalModulePlan plan, CancellationToken cancellationToken);
}

/// <summary>
/// 为候选计划构建尚不可见的 runtime/public 投影。
/// </summary>
public interface IPluginRuntimeProjection
{
    IPluginRuntimeStage Stage(CanonicalModulePlan plan);
}

/// <summary>
/// 由 Plugin Platform 拥有、失败时可直接清理的投影 staging。
/// </summary>
public interface IPluginRuntimeStage : IDisposable
{
    IPluginRuntimeCommit BeginCommit();
}

/// <summary>
/// 已开始但尚未最终确认的 projection 切换；Dispose 必须在未 Complete 时恢复旧投影。
/// </summary>
public interface IPluginRuntimeCommit : IDisposable
{
    void Complete();
}
