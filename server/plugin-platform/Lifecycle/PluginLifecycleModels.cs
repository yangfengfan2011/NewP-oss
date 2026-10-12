namespace ICP.Platform.PluginPlatform.Lifecycle;

/// <summary>
/// 已安装插件包的只读清单。LegacyCurrentEnabledVersion 与 LegacyExplicitlyDisabledVersion
/// 只投影已有库存的显式运维选择；Plugin Platform 永远不会回写这些字段。
/// </summary>
public sealed record InstalledPluginInventoryItem(
    string ModuleId,
    IReadOnlyList<string> InstalledVersions,
    string? LegacyCurrentEnabledVersion,
    IReadOnlyDictionary<string, string>? PackageRoots = null,
    string? LegacyExplicitlyDisabledVersion = null)
{
    public string? FindPackageRoot(string version)
    {
        if (PackageRoots is null)
        {
            return null;
        }

        return PackageRoots.FirstOrDefault(item =>
            string.Equals(item.Key, version, StringComparison.OrdinalIgnoreCase)).Value;
    }
}

/// <summary>
/// 运维方声明的下一次冷启动期望状态。
/// </summary>
public sealed record PluginDesiredModuleState(
    string ModuleId,
    string Version,
    bool Enabled);

/// <summary>
/// 已通过启动闸门并属于活动快照的模块。
/// </summary>
public sealed record PluginActiveModule(
    string ModuleId,
    string Version,
    string SourceKind);

/// <summary>
/// Host 与 Shell 共同消费的不可变活动插件快照。
/// </summary>
public sealed class PluginActiveSnapshot : IEquatable<PluginActiveSnapshot>
{
    public PluginActiveSnapshot(
        string snapshotId,
        string planHash,
        string persistencePlanDigest,
        IReadOnlyList<PluginActiveModule> modules)
    {
        SnapshotId = snapshotId;
        PlanHash = planHash;
        PersistencePlanDigest = persistencePlanDigest;
        Modules = modules.ToArray();
    }

    public string SnapshotId { get; init; }

    public string PlanHash { get; init; }

    public string PersistencePlanDigest { get; init; }

    public IReadOnlyList<PluginActiveModule> Modules { get; init; }

    public bool Equals(PluginActiveSnapshot? other)
    {
        return other is not null
            && string.Equals(SnapshotId, other.SnapshotId, StringComparison.Ordinal)
            && string.Equals(PlanHash, other.PlanHash, StringComparison.Ordinal)
            && string.Equals(PersistencePlanDigest, other.PersistencePlanDigest, StringComparison.Ordinal)
            && Modules.SequenceEqual(other.Modules);
    }

    public override bool Equals(object? obj) => Equals(obj as PluginActiveSnapshot);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(SnapshotId, StringComparer.Ordinal);
        hash.Add(PlanHash, StringComparer.Ordinal);
        hash.Add(PersistencePlanDigest, StringComparer.Ordinal);
        foreach (var module in Modules)
        {
            hash.Add(module);
        }

        return hash.ToHashCode();
    }
}

/// <summary>
/// desired、active 与 LKG 的一次解析结果。
/// </summary>
public sealed record ResolvedPluginLifecycleState(
    IReadOnlyList<PluginDesiredModuleState> DesiredModules,
    PluginActiveSnapshot? ActiveSnapshot,
    PluginActiveSnapshot? LastKnownGoodSnapshot,
    bool UsesLegacyFallback);

/// <summary>
/// 生命周期管理命令的稳定结果。
/// </summary>
public sealed record PluginLifecycleCommandResult(
    bool IsSuccess,
    bool RestartRequired,
    string Status,
    string ModuleId,
    string? Version,
    string Message)
{
    public static PluginLifecycleCommandResult PendingRestart(string moduleId, string? version) =>
        new(
            true,
            true,
            "PendingRestart",
            moduleId,
            version,
            "期望状态已更新，需要重启 Host 才会尝试激活。");

    public static PluginLifecycleCommandResult Failed(string moduleId, string? version, string message) =>
        new(false, false, "Failed", moduleId, version, message);
}
