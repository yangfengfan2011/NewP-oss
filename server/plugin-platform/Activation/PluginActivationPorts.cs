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
/// 将恢复输入、候选归档、最终确认与活动读取绑定到同一部署目标。
/// </summary>
public interface IPluginActivationDeployment
{
    /// <summary>使用本部署目标的可信恢复输入编译唯一持久化计划。</summary>
    CompiledPersistencePlan Compile(CanonicalModulePlan modulePlan);

    /// <summary>在 Host Build 与首个 DDL 前保存候选的完整可信恢复输入。</summary>
    void PrepareCandidate(CanonicalModulePlan modulePlan, CompiledPersistencePlan persistencePlan);

    /// <summary>在完整活动提交后发布候选的最终部署确认。</summary>
    void CompleteCandidate(CompiledPersistencePlan persistencePlan);

    /// <summary>仅接受本部署目标已最终确认的活动或 LKG 快照。</summary>
    bool IsSnapshotCommitted(PluginActiveSnapshot snapshot);
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
    /// <summary>
    /// 在任何持久化或运行时副作用前验证候选将发布的 metadata 集合。
    /// </summary>
    /// <param name="plan">已冻结的 Canonical Module Plan。</param>
    void Validate(CanonicalModulePlan plan);

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
