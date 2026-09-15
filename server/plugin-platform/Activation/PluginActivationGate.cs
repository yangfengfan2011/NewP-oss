using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Persistence.Planning;
using ICP.Platform.PluginPlatform.Lifecycle;

namespace ICP.Platform.PluginPlatform.Activation;

/// <summary>
/// 把 desired state 编译为 Host 启动期间唯一可提交的候选激活会话。
/// </summary>
public sealed class PluginActivationGate
{
    private readonly PluginLifecycleManager _lifecycleManager;
    private readonly IPluginPackagePreflight _packagePreflight;
    private readonly IPluginCandidatePlanCompiler _modulePlanCompiler;
    private readonly IPluginPersistencePlanCompiler _persistencePlanCompiler;
    private readonly IPluginRuntimeProjection _runtimeProjection;

    public PluginActivationGate(
        PluginLifecycleManager lifecycleManager,
        IPluginPackagePreflight packagePreflight,
        IPluginCandidatePlanCompiler modulePlanCompiler,
        IPluginPersistencePlanCompiler persistencePlanCompiler,
        IPluginRuntimeProjection runtimeProjection)
    {
        _lifecycleManager = lifecycleManager;
        _packagePreflight = packagePreflight;
        _modulePlanCompiler = modulePlanCompiler;
        _persistencePlanCompiler = persistencePlanCompiler;
        _runtimeProjection = runtimeProjection;
    }

    // Prepare 在迁移与权限同步前完成整份候选的静态编译和 runtime metadata 验证。
    public PluginActivationSession Prepare(IReadOnlyList<InstalledPluginInventoryItem> inventory)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        var state = _lifecycleManager.Resolve(inventory);

        RunPhase(
            PluginActivationFailureCodes.PackagePreflightFailed,
            () => _packagePreflight.Validate(inventory, state));
        var modulePlan = RunPhase(
            PluginActivationFailureCodes.ModulePlanFailed,
            () => _modulePlanCompiler.Compile(inventory, state));
        RunPhase(
            PluginActivationFailureCodes.RuntimeMetadataValidationFailed,
            () => _runtimeProjection.Validate(modulePlan));
        var persistencePlan = RunPhase(
            PluginActivationFailureCodes.PersistencePlanFailed,
            () => _persistencePlanCompiler.Compile(modulePlan));

        if (!string.Equals(modulePlan.SnapshotId, persistencePlan.SnapshotId, StringComparison.Ordinal)
            || !string.Equals(modulePlan.PlanHash, persistencePlan.ModulePlanHash, StringComparison.Ordinal))
        {
            throw new PluginActivationException(
                PluginActivationFailureCodes.PersistencePlanFailed,
                databaseAhead: false,
                new InvalidDataException(
                    "Compiled Persistence Plan is not bound to the candidate Canonical Module Plan."));
        }

        return new PluginActivationSession(
            _lifecycleManager,
            _runtimeProjection,
            inventory,
            modulePlan,
            persistencePlan);
    }

    private static void RunPhase(string failureCode, Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw PluginActivationException.Wrap(
                failureCode,
                databaseAhead: false,
                exception);
        }
    }

    private static T RunPhase<T>(string failureCode, Func<T> action)
    {
        try
        {
            return action();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw PluginActivationException.Wrap(
                failureCode,
                databaseAhead: false,
                exception);
        }
    }
}

/// <summary>
/// 已完成 Host 组合前编译、等待 builder.Build 之后继续的候选激活会话。
/// </summary>
public sealed class PluginActivationSession : IDisposable
{
    private readonly PluginLifecycleManager _lifecycleManager;
    private readonly IPluginRuntimeProjection _runtimeProjection;
    private readonly IReadOnlyList<InstalledPluginInventoryItem> _inventory;
    private bool _completed;
    private bool _disposed;

    internal PluginActivationSession(
        PluginLifecycleManager lifecycleManager,
        IPluginRuntimeProjection runtimeProjection,
        IReadOnlyList<InstalledPluginInventoryItem> inventory,
        CanonicalModulePlan modulePlan,
        CompiledPersistencePlan persistencePlan)
    {
        _lifecycleManager = lifecycleManager;
        _runtimeProjection = runtimeProjection;
        _inventory = inventory.ToArray();
        ModulePlan = modulePlan;
        PersistencePlan = persistencePlan;
    }

    public CanonicalModulePlan ModulePlan { get; }

    public CompiledPersistencePlan PersistencePlan { get; }

    public async Task CompleteAsync(
        IPluginMigrationExecutor migrationExecutor,
        IPluginPermissionSynchronizer permissionSynchronizer,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_completed)
        {
            throw new PluginActivationException(
                PluginActivationFailureCodes.SessionAlreadyCompleted,
                databaseAhead: false);
        }

        ArgumentNullException.ThrowIfNull(migrationExecutor);
        ArgumentNullException.ThrowIfNull(permissionSynchronizer);

        try
        {
            await migrationExecutor.ExecuteAsync(PersistencePlan, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw PluginActivationException.Wrap(
                PluginActivationFailureCodes.MigrationFailed,
                migrationExecutor.DatabaseAhead,
                exception);
        }

        IPluginRuntimeStage runtimeStage;
        try
        {
            runtimeStage = _runtimeProjection.Stage(ModulePlan);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw PluginActivationException.Wrap(
                PluginActivationFailureCodes.RuntimeStagingFailed,
                databaseAhead: true,
                exception);
        }

        using (runtimeStage)
        {
            // 所有运行时资产 Stage 成功后才允许目录同步，失败候选不触及授权数据。
            try
            {
                await permissionSynchronizer.SynchronizeAsync(ModulePlan, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                throw PluginActivationException.Wrap(
                    PluginActivationFailureCodes.PermissionSyncFailed,
                    databaseAhead: true,
                    exception);
            }

            IPluginRuntimeCommit runtimeCommit;
            try
            {
                runtimeCommit = runtimeStage.BeginCommit();
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                throw PluginActivationException.Wrap(
                    PluginActivationFailureCodes.RuntimeCommitFailed,
                    databaseAhead: true,
                    exception);
            }

            using (runtimeCommit)
            {
                var snapshot = new PluginActiveSnapshot(
                    ModulePlan.SnapshotId,
                    ModulePlan.PlanHash,
                    PersistencePlan.PlanDigest,
                    ModulePlan.Modules
                        .Select(static module => new PluginActiveModule(
                            module.ModuleId,
                            module.Version,
                            module.Provenance.SourceKind))
                        .ToArray());

                try
                {
                    _lifecycleManager.CommitActive(_inventory, snapshot);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    throw PluginActivationException.Wrap(
                        PluginActivationFailureCodes.ActiveCommitFailed,
                        databaseAhead: true,
                        exception);
                }

                try
                {
                    runtimeCommit.Complete();
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    throw PluginActivationException.Wrap(
                        PluginActivationFailureCodes.RuntimeCommitFailed,
                        databaseAhead: true,
                        exception);
                }
            }
        }

        _completed = true;
    }

    public void Dispose()
    {
        _disposed = true;
    }
}
