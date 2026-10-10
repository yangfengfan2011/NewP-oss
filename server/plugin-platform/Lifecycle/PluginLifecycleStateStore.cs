using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ICP.Platform.PluginPlatform.Activation;

namespace ICP.Platform.PluginPlatform.Lifecycle;

/// <summary>
/// 持久化 v2 desired/active/LKG 状态和不可变活动快照。
/// </summary>
public sealed class PluginLifecycleStateStore
{
    private const string CurrentSchemaVersion = "2.0";
    private readonly string _stateRoot;
    private readonly string _snapshotsRoot;
    private readonly string _statePath;
    internal IPluginActivationDeployment Deployment { get; }
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    /// <summary>显式绑定统一部署适配器；候选提交与活动读取共用同一确认策略。</summary>
    public PluginLifecycleStateStore(string installRoot, IPluginActivationDeployment deployment)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installRoot);
        ArgumentNullException.ThrowIfNull(deployment);
        var resolvedInstallRoot = Path.GetFullPath(installRoot);
        _stateRoot = Path.Combine(resolvedInstallRoot, "lifecycle", "v2");
        _snapshotsRoot = Path.Combine(_stateRoot, "snapshots");
        _statePath = Path.Combine(_stateRoot, "state.json");
        Deployment = deployment;
    }

    // snapshot 文件与活动指针可先写入，只有宿主的最终确认允许消费者将其视为活动或 LKG。
    public ResolvedPluginLifecycleState Resolve(IReadOnlyList<InstalledPluginInventoryItem> inventory)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        ValidateInventory(inventory);

        var document = LoadDocument();
        if (document is null)
        {
            var desired = BuildLegacyDesired(inventory);
            var legacySnapshot = BuildLegacySnapshot(inventory);
            return new ResolvedPluginLifecycleState(
                desired,
                AcceptSnapshot(legacySnapshot),
                AcceptSnapshot(legacySnapshot),
                legacySnapshot is not null);
        }

        var desiredModules = MergeNewlyInstalledModules(document.DesiredModules, inventory);
        var active = AcceptSnapshot(LoadSnapshot(document.ActiveSnapshotId));
        var lastKnownGood = AcceptSnapshot(LoadSnapshot(document.LastKnownGoodSnapshotId));
        return new ResolvedPluginLifecycleState(
            desiredModules,
            active,
            lastKnownGood,
            UsesLegacyFallback: false);
    }

    // 持久化确认由 Host 组合注入，插件生命周期不绑定具体 provider 或部署资产实现。
    private PluginActiveSnapshot? AcceptSnapshot(PluginActiveSnapshot? snapshot) =>
        snapshot is not null && Deployment.IsSnapshotCommitted(snapshot) ? snapshot : null;

    // 保存离线意图保留既有 raw 指针；最终确认过滤只影响读取，不允许 desired 写入删掉准备事实。
    internal void SaveDesired(
        ResolvedPluginLifecycleState current,
        IReadOnlyList<PluginDesiredModuleState> desiredModules)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(desiredModules);
        ValidateDesired(desiredModules);

        PersistSnapshotIfNeeded(current.ActiveSnapshot);
        PersistSnapshotIfNeeded(current.LastKnownGoodSnapshot);

        var existing = LoadDocument();
        var document = new PluginLifecycleStateDocument
        {
            SchemaVersion = CurrentSchemaVersion,
            Revision = checked((existing?.Revision ?? 0) + 1),
            DesiredModules = desiredModules
                .OrderBy(static item => item.ModuleId, StringComparer.Ordinal)
                .ToList(),
            ActiveSnapshotId = existing is null ? current.ActiveSnapshot?.SnapshotId : existing.ActiveSnapshotId,
            LastKnownGoodSnapshotId = existing is null ? current.LastKnownGoodSnapshot?.SnapshotId : existing.LastKnownGoodSnapshotId
        };
        WriteState(document);
    }

    // raw 指针提交保留此前已确认的 LKG；读取方仍须等待宿主最终确认候选。
    internal void CommitActive(
        ResolvedPluginLifecycleState current,
        PluginActiveSnapshot candidate)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(candidate);
        ValidateSnapshot(candidate);

        PersistSnapshotIfNeeded(candidate);
        PersistSnapshotIfNeeded(current.ActiveSnapshot);
        PersistSnapshotIfNeeded(current.LastKnownGoodSnapshot);

        PluginActiveSnapshot lastKnownGood;
        if (current.ActiveSnapshot is null)
        {
            lastKnownGood = current.LastKnownGoodSnapshot ?? candidate;
        }
        else if (string.Equals(
                     current.ActiveSnapshot.SnapshotId,
                     candidate.SnapshotId,
                     StringComparison.Ordinal))
        {
            lastKnownGood = current.LastKnownGoodSnapshot ?? candidate;
        }
        else
        {
            lastKnownGood = current.ActiveSnapshot;
        }

        PersistSnapshotIfNeeded(lastKnownGood);
        var existing = LoadDocument();
        var document = new PluginLifecycleStateDocument
        {
            SchemaVersion = CurrentSchemaVersion,
            Revision = checked((existing?.Revision ?? 0) + 1),
            DesiredModules = current.DesiredModules
                .OrderBy(static item => item.ModuleId, StringComparer.Ordinal)
                .ToList(),
            ActiveSnapshotId = candidate.SnapshotId,
            LastKnownGoodSnapshotId = lastKnownGood.SnapshotId
        };
        WriteState(document);
    }

    private PluginLifecycleStateDocument? LoadDocument()
    {
        if (!File.Exists(_statePath))
        {
            return null;
        }

        var document = JsonSerializer.Deserialize<PluginLifecycleStateDocument>(
            File.ReadAllText(_statePath),
            _jsonOptions)
            ?? throw new InvalidDataException("PLUGIN_STATE_INVALID: state.json deserialized to null.");
        if (!string.Equals(document.SchemaVersion, CurrentSchemaVersion, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"PLUGIN_STATE_SCHEMA_UNSUPPORTED: expected '{CurrentSchemaVersion}', got '{document.SchemaVersion}'.");
        }

        ValidateDesired(document.DesiredModules);
        ValidateSnapshotId(document.ActiveSnapshotId);
        ValidateSnapshotId(document.LastKnownGoodSnapshotId);
        return document;
    }

    private PluginActiveSnapshot? LoadSnapshot(string? snapshotId)
    {
        if (string.IsNullOrWhiteSpace(snapshotId))
        {
            return null;
        }

        ValidateSnapshotId(snapshotId);
        var path = Path.Combine(_snapshotsRoot, snapshotId + ".json");
        if (!File.Exists(path))
        {
            throw new InvalidDataException(
                $"PLUGIN_SNAPSHOT_MISSING: snapshot '{snapshotId}' is referenced by lifecycle state.");
        }

        var snapshot = JsonSerializer.Deserialize<PluginActiveSnapshot>(
            File.ReadAllText(path),
            _jsonOptions)
            ?? throw new InvalidDataException(
                $"PLUGIN_SNAPSHOT_INVALID: snapshot '{snapshotId}' deserialized to null.");
        ValidateSnapshot(snapshot);
        if (!string.Equals(snapshot.SnapshotId, snapshotId, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"PLUGIN_SNAPSHOT_ID_MISMATCH: expected '{snapshotId}', got '{snapshot.SnapshotId}'.");
        }

        return snapshot;
    }

    private void PersistSnapshotIfNeeded(PluginActiveSnapshot? snapshot)
    {
        if (snapshot is null)
        {
            return;
        }

        ValidateSnapshot(snapshot);
        Directory.CreateDirectory(_snapshotsRoot);
        var path = Path.Combine(_snapshotsRoot, snapshot.SnapshotId + ".json");
        var bytes = Serialize(snapshot);
        if (File.Exists(path))
        {
            if (!File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes))
            {
                throw new InvalidDataException(
                    $"PLUGIN_SNAPSHOT_IMMUTABLE: snapshot '{snapshot.SnapshotId}' already exists with different content.");
            }

            return;
        }

        WriteNewFileAtomically(path, bytes);
    }

    private void WriteState(PluginLifecycleStateDocument document)
    {
        Directory.CreateDirectory(_stateRoot);
        WriteReplacingFileAtomically(_statePath, Serialize(document));
    }

    private byte[] Serialize<T>(T value)
    {
        var json = JsonSerializer.Serialize(value, _jsonOptions) + Environment.NewLine;
        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(json);
    }

    private static void WriteNewFileAtomically(string path, byte[] bytes)
    {
        var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(temporaryPath, bytes);
            File.Move(temporaryPath, path);
        }
        finally
        {
            TryDeleteFile(temporaryPath);
        }
    }

    private static void WriteReplacingFileAtomically(string path, byte[] bytes)
    {
        var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(temporaryPath, bytes);
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            TryDeleteFile(temporaryPath);
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }

    private static IReadOnlyList<PluginDesiredModuleState> BuildLegacyDesired(
        IReadOnlyList<InstalledPluginInventoryItem> inventory)
    {
        return inventory
            .Select(item =>
            {
                var legacyVersion = FindInstalledVersion(item, item.LegacyCurrentEnabledVersion);
                var version = legacyVersion ?? item.InstalledVersions.LastOrDefault() ?? string.Empty;
                return new PluginDesiredModuleState(
                    item.ModuleId,
                    version,
                    legacyVersion is not null);
            })
            .Where(static item => item.Version.Length > 0)
            .OrderBy(static item => item.ModuleId, StringComparer.Ordinal)
            .ToArray();
    }

    private static PluginActiveSnapshot? BuildLegacySnapshot(
        IReadOnlyList<InstalledPluginInventoryItem> inventory)
    {
        var modules = inventory
            .Select(item => new
            {
                item.ModuleId,
                Version = FindInstalledVersion(item, item.LegacyCurrentEnabledVersion)
            })
            .Where(static item => item.Version is not null)
            .Select(static item => new PluginActiveModule(
                item.ModuleId,
                item.Version!,
                "installed-package"))
            .OrderBy(static item => item.ModuleId, StringComparer.Ordinal)
            .ToArray();
        if (modules.Length == 0)
        {
            return null;
        }

        var canonical = string.Join(
            "\n",
            modules.Select(static item => $"{item.ModuleId}\0{item.Version}\0{item.SourceKind}"));
        var digest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
        return new PluginActiveSnapshot(
            "legacy-" + digest[..24],
            digest,
            new string('0', 64),
            modules);
    }

    private static IReadOnlyList<PluginDesiredModuleState> MergeNewlyInstalledModules(
        IReadOnlyList<PluginDesiredModuleState> desired,
        IReadOnlyList<InstalledPluginInventoryItem> inventory)
    {
        var merged = desired.ToDictionary(static item => item.ModuleId, StringComparer.OrdinalIgnoreCase);
        foreach (var item in inventory)
        {
            if (merged.ContainsKey(item.ModuleId) || item.InstalledVersions.Count == 0)
            {
                continue;
            }

            merged[item.ModuleId] = new PluginDesiredModuleState(
                item.ModuleId,
                item.InstalledVersions[^1],
                Enabled: false);
        }

        return merged.Values
            .OrderBy(static item => item.ModuleId, StringComparer.Ordinal)
            .ToArray();
    }

    private static string? FindInstalledVersion(
        InstalledPluginInventoryItem item,
        string? requestedVersion)
    {
        if (string.IsNullOrWhiteSpace(requestedVersion))
        {
            return null;
        }

        return item.InstalledVersions.FirstOrDefault(version =>
            string.Equals(version, requestedVersion, StringComparison.OrdinalIgnoreCase));
    }

    private static void ValidateInventory(IReadOnlyList<InstalledPluginInventoryItem> inventory)
    {
        var moduleIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in inventory)
        {
            ValidatePathSegment(item.ModuleId, "moduleId");
            if (!moduleIds.Add(item.ModuleId))
            {
                throw new InvalidDataException(
                    $"PLUGIN_INVENTORY_MODULE_CONFLICT: module '{item.ModuleId}' appears more than once.");
            }

            foreach (var version in item.InstalledVersions)
            {
                ValidatePathSegment(version, "version");
            }
        }
    }

    private static void ValidateDesired(IReadOnlyList<PluginDesiredModuleState> desiredModules)
    {
        var moduleIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in desiredModules)
        {
            ValidatePathSegment(item.ModuleId, "moduleId");
            ValidatePathSegment(item.Version, "version");
            if (!moduleIds.Add(item.ModuleId))
            {
                throw new InvalidDataException(
                    $"PLUGIN_DESIRED_MODULE_CONFLICT: module '{item.ModuleId}' appears more than once.");
            }
        }
    }

    private static void ValidateSnapshot(PluginActiveSnapshot snapshot)
    {
        ValidateSnapshotId(snapshot.SnapshotId);
        if (string.IsNullOrWhiteSpace(snapshot.PlanHash)
            || string.IsNullOrWhiteSpace(snapshot.PersistencePlanDigest))
        {
            throw new InvalidDataException(
                $"PLUGIN_SNAPSHOT_INVALID: snapshot '{snapshot.SnapshotId}' has incomplete plan identity.");
        }

        var moduleIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var module in snapshot.Modules)
        {
            ValidatePathSegment(module.ModuleId, "moduleId");
            ValidatePathSegment(module.Version, "version");
            if (!moduleIds.Add(module.ModuleId))
            {
                throw new InvalidDataException(
                    $"PLUGIN_SNAPSHOT_MODULE_CONFLICT: module '{module.ModuleId}' appears more than once.");
            }
        }
    }

    private static void ValidateSnapshotId(string? snapshotId)
    {
        if (snapshotId is null)
        {
            return;
        }

        ValidatePathSegment(snapshotId, "snapshotId");
    }

    private static void ValidatePathSegment(string value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value is "." or ".."
            || value.Contains("..", StringComparison.Ordinal)
            || value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || value.Contains(Path.DirectorySeparatorChar)
            || value.Contains(Path.AltDirectorySeparatorChar))
        {
            throw new InvalidDataException(
                $"PLUGIN_STATE_PATH_INVALID: {fieldName} '{value}' is not a safe path segment.");
        }
    }

    private sealed class PluginLifecycleStateDocument
    {
        public string SchemaVersion { get; set; } = CurrentSchemaVersion;

        public long Revision { get; set; }

        public List<PluginDesiredModuleState> DesiredModules { get; set; } = [];

        public string? ActiveSnapshotId { get; set; }

        public string? LastKnownGoodSnapshotId { get; set; }
    }
}
