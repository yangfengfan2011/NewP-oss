using System.Text.Json;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Kernel.PluginPackages;
using ICP.Platform.PluginPlatform.Lifecycle;
using ICP.Platform.PluginPlatform.Activation;
using ICP.Platform.PluginPlatform.StandardData;
using ICP.Platform.PluginPlatform.ComponentBundles;

namespace ICP.Platform.HostApi.PluginOperations;

/// <summary>同一安装根只允许一个 Host 或离线部署进程持有；整个 Host 生命周期保持停服边界。</summary>
internal static class PluginDeploymentLease
{
    // 租约位于开关无关的安装根，两个 lifecycle state root 均不能在 Host 运行时被修改。
    internal static FileStream Acquire(string installRoot)
    {
        Directory.CreateDirectory(installRoot);
        PathContainment.RejectReparsePoints(installRoot);
        try { return new FileStream(Path.Combine(installRoot, "host-deployment.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException exception) { throw new InvalidOperationException("PLUGIN_HOST_MUST_BE_STOPPED", exception); }
    }
}

/// <summary>正式发行 Host 内的离线入口，只保存库存和 desired；不构建 Host、访问 SQL 或发布活动视图。</summary>
internal static class PluginOfflineDeployment
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    // 旧关闭态有差异时失败关闭；已覆盖的旧文件仅退出选择入口，原字节和恢复材料保留。
    internal static void RejectConflictingLegacyDesired(string installRoot)
    {
        var legacy = LegacyDesiredPath(installRoot);
        if (!File.Exists(legacy)) return;
        var canonical = Path.Combine(installRoot, "lifecycle", "v2", "state.json");
        var previous = ReadDesiredSource(installRoot, legacy);
        var current = File.Exists(canonical) ? ReadDesiredSource(installRoot, canonical) : [];
        var differences = DescribeSourceDifferences(current, previous);
        if (differences.Length > 0)
            throw new InvalidDataException("PLUGIN_DESIRED_SOURCE_CONFLICT: " + JsonSerializer.Serialize(differences, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        RetireLegacyDesired(installRoot);
    }

    private sealed record DesiredSourceDifference(string ModuleId, PluginDesiredModuleState? Canonical, PluginDesiredModuleState Legacy);

    // 来源迁移严格核对旧协议与显式启停值，不把损坏或缺项静默解释为默认值。
    private static PluginDesiredModuleState[] ReadDesiredSource(string installRoot, string path)
    {
        using var document = JsonDocument.Parse(PathContainment.ReadContainedFile(installRoot, path));
        if (!document.RootElement.TryGetProperty("schemaVersion", out var schema) || schema.GetString() != "2.0"
            || !document.RootElement.TryGetProperty("desiredModules", out var desired))
            throw new InvalidDataException("PLUGIN_DESIRED_SOURCE_CONFLICT: invalid desired source.");
        var selections = ReadSelections(desired);
        if (selections.Select(item => item.ModuleId).Distinct(StringComparer.OrdinalIgnoreCase).Count() != selections.Length)
            throw new InvalidDataException("PLUGIN_DESIRED_SOURCE_CONFLICT: duplicate identity in desired source.");
        return selections;
    }

    // 当前根额外选择保持；旧根每个未被当前覆盖的选择都必须有一次明确裁决。
    private static DesiredSourceDifference[] DescribeSourceDifferences(IReadOnlyList<PluginDesiredModuleState> canonical,
        IReadOnlyList<PluginDesiredModuleState> legacy)
    {
        var current = canonical.ToDictionary(item => item.ModuleId, StringComparer.OrdinalIgnoreCase);
        return legacy.Where(old => !current.TryGetValue(old.ModuleId, out var item) || item.Version != old.Version || item.Enabled != old.Enabled)
            .Select(old => new DesiredSourceDifference(old.ModuleId, current.GetValueOrDefault(old.ModuleId), old))
            .OrderBy(item => item.ModuleId, StringComparer.Ordinal).ToArray();
    }

    // 精确退出旧状态文件，保留其字节与旁边的 snapshots/persistence；中断后重试仍失败关闭或幂等完成。
    private static void RetireLegacyDesired(string installRoot)
    {
        var path = LegacyDesiredPath(installRoot);
        if (File.Exists(path)) File.Move(path, Path.Combine(Path.GetDirectoryName(path)!, "state.reconciled-" + Guid.NewGuid().ToString("N") + ".json"));
    }

    // 第三方关闭态旧根是唯一兼容入口；不得随着开关继续写入。
    private static string LegacyDesiredPath(string installRoot) =>
        Path.Combine(installRoot, ".feature-gates", "external-plugins-disabled", "lifecycle", "v2", "state.json");

    // builtin 信任仅由当前发行 Host 签发；外部命令不能指定任意目录成为可信内建来源。
    internal static int Run(string[] args, string installRoot, TrustedBuiltInModuleSource source,
        bool externalPluginsEnabled, StandardDataPackageInspector inspector, PluginLifecycleManager manager)
    {
        try
        {
            var options = Parse(args[2..]);
            var action = args[1].ToLowerInvariant();
            var store = new InstalledPluginStateStore(installRoot);
            NativeComponentInventory.Recover(installRoot);
            if (action != "reconcile-desired") RejectConflictingLegacyDesired(installRoot);
            var installed = InstalledPluginInventoryAdapter.Create(store);
            var builtIns = new ModuleCapsuleCandidateReader().ReadTrustedBuiltIn(source);
            var inventory = installed.Concat(builtIns.Where(module => !installed.Any(item => item.ModuleId == module.ModuleId))
                .Select(module => new InstalledPluginInventoryItem(module.ModuleId, [module.Version], module.Version))).ToArray();
            var current = manager.Resolve(installed);
            var recorded = manager.ReadRecordedDesired(installed);
            var desired = recorded.Concat(builtIns.Where(module => !recorded.Any(item => string.Equals(item.ModuleId, module.ModuleId, StringComparison.OrdinalIgnoreCase)))
                .Select(module => new PluginDesiredModuleState(module.ModuleId, module.Version, true))).ToArray();
            switch (action)
            {
                case "install-native":
                    new NativeComponentPackageInstaller(installRoot, store).Install(Required(options, "package"), source.PlatformCompositionModuleId);
                    break;
                case "install":
                    if (!externalPluginsEnabled) throw new InvalidDataException("PLUGIN_EXTERNAL_DISABLED");
                    var package = Required(options, "package");
                    using (var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(package, "plugin.json"))))
                    {
                        if (manifest.RootElement.TryGetProperty("profile", out _))
                        {
                            var result = new StandardDataPackageInstaller(installRoot, store, inspector).Install(package);
                            if (!result.IsSuccess) throw new InvalidDataException(result.Message);
                        }
                        else
                        {
                            // 安装未选包不能隐式移除 desired 版本；正式库存由已有显式 prune 管理。
                            var result = new UnifiedPluginPackageInstaller(store).Install(package, installRoot, pruneOldVersions: false);
                            if (result.Status != "Installed") throw new InvalidDataException(result.ErrorMessage);
                        }
                    }
                    break;
                case "desired":
                    var selections = ReadSelections(Required(options, "file"));
                    foreach (var selection in selections) RequireExternalSelection(externalPluginsEnabled, installRoot, installed, selection.ModuleId, selection.Version);
                    Check(manager.RequestDesired(inventory, desired.Where(item => !selections.Any(selection => selection.ModuleId == item.ModuleId)).Concat(selections).ToArray()));
                    break;
                case "select-bundle":
                case "enable-bundle":
                    SelectBundle(installRoot, inventory, desired, manager, Required(options, "package"),
                        action == "enable-bundle", options.GetValueOrDefault("decisions"), externalPluginsEnabled);
                    break;
                case "reconcile-desired":
                    ReconcileLegacyDesired(installRoot, inventory, desired, manager, ReadSelections(Required(options, "file")), externalPluginsEnabled);
                    break;
                case "select":
                case "enable":
                    var selectedId = Required(options, "module-id");
                    RequireExternalSelection(externalPluginsEnabled, installRoot, installed, selectedId, Required(options, "version"));
                    var previous = desired.SingleOrDefault(item => item.ModuleId == selectedId);
                    var enabled = action == "enable" || (options.TryGetValue("enabled", out var value) ? bool.Parse(value) : previous?.Enabled ?? false);
                    Check(manager.RequestDesired(inventory, desired.Where(item => item.ModuleId != selectedId).Append(new(selectedId, Required(options, "version"), enabled)).ToArray()));
                    break;
                case "disable":
                    var disabledId = Required(options, "module-id");
                    var disabled = desired.SingleOrDefault(item => item.ModuleId == disabledId) ?? throw new InvalidDataException("PLUGIN_NOT_INSTALLED");
                    Check(manager.RequestDesired(inventory, desired.Where(item => item.ModuleId != disabledId).Append(disabled with { Enabled = false }).ToArray()));
                    break;
                case "rollback":
                    var rollbackId = Required(options, "module-id");
                    var rollbackVersion = current.LastKnownGoodSnapshot?.Modules.SingleOrDefault(item => item.ModuleId == rollbackId)?.Version
                        ?? throw new InvalidDataException("PLUGIN_LAST_KNOWN_GOOD_MISSING");
                    RequireExternalSelection(externalPluginsEnabled, installRoot, installed, rollbackId, rollbackVersion);
                    Check(manager.RequestDesired(inventory, desired.Where(item => item.ModuleId != rollbackId).Append(new(rollbackId, rollbackVersion, true)).ToArray()));
                    break;
                case "uninstall":
                    Uninstall(installRoot, source, inspector, manager, store, installed, inventory, builtIns,
                        desired, current, ReadUninstallSet(options), externalPluginsEnabled);
                    break;
                case "prune":
                    Prune(store, manager, inventory, Required(options, "module-id"), Required(options, "version"));
                    break;
                default: throw new ArgumentException("PLUGIN_OFFLINE_ACTION_UNSUPPORTED");
            }
            Console.WriteLine(action + ":PendingRestart");
            return 0;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or InvalidOperationException or ArgumentException or JsonException or FormatException or PluginActivationException)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }

    // 显式集合必须完整有效；单组件入口也走同一裁决，不从包或依赖推导额外成员。
    private static string[] ReadUninstallSet(IReadOnlyDictionary<string, string> options)
    {
        if (!options.TryGetValue("file", out var file)) return [Required(options, "module-id")];
        if (options.ContainsKey("module-id")) throw new InvalidDataException("PLUGIN_UNINSTALL_SET_INVALID: choose file or module-id.");
        using var document = JsonDocument.Parse(File.ReadAllText(file));
        if (document.RootElement.ValueKind != JsonValueKind.Array
            || document.RootElement.EnumerateArray().Any(item => item.ValueKind != JsonValueKind.String))
            throw new InvalidDataException("PLUGIN_UNINSTALL_SET_INVALID: expected an array of component ids.");
        return document.RootElement.EnumerateArray().Select(item => item.GetString()!).ToArray();
    }

    // 删除前整批验证必需身份、保留组合与活动依赖；只移除代码/库存/选择，部署归档与数据库由原恢复链保留。
    private static void Uninstall(string installRoot, TrustedBuiltInModuleSource source, StandardDataPackageInspector inspector,
        PluginLifecycleManager manager, InstalledPluginStateStore store, IReadOnlyList<InstalledPluginInventoryItem> installed,
        IReadOnlyList<InstalledPluginInventoryItem> inventory, IReadOnlyList<ModuleCapsuleCandidate> builtIns,
        IReadOnlyList<PluginDesiredModuleState> desired, ResolvedPluginLifecycleState current, IReadOnlyList<string> requested,
        bool externalPluginsEnabled)
    {
        var ids = requested.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (requested.Count == 0 || requested.Any(string.IsNullOrWhiteSpace) || ids.Count != requested.Count)
            throw new InvalidDataException("PLUGIN_UNINSTALL_SET_INVALID: a non-empty exact set is required.");
        var items = requested.Select(id => inventory.SingleOrDefault(item => string.Equals(item.ModuleId, id, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException("PLUGIN_NOT_INSTALLED: " + id)).ToArray();
        var plan = new StandardDataCandidatePlanCompiler(source, inspector, installRoot: installRoot,
            externalPluginsEnabled: externalPluginsEnabled).Compile(installed, current with { DesiredModules = desired });
        var required = plan.RequiredModuleIds.Where(ids.Contains).ToArray();
        if (required.Length > 0) throw new InvalidDataException("PLATFORM_REQUIRED_MODULE_MISSING: " + string.Join(", ", required));
        var remainingDesired = desired.Where(item => !ids.Contains(item.ModuleId)).ToArray();
        _ = new StandardDataCandidatePlanCompiler(source, inspector, excludedBuiltInModuleIds: ids, installRoot: installRoot,
            externalPluginsEnabled: externalPluginsEnabled).Compile(installed.Where(item => !ids.Contains(item.ModuleId)).ToArray(),
                current with { DesiredModules = remainingDesired });
        foreach (var item in items)
        {
            ValidateDirectory(Path.Combine(installRoot, "packages"), Path.Combine(installRoot, "packages", item.ModuleId));
            ValidateDirectory(Path.Combine(installRoot, ".pruned", "packages"), Path.Combine(installRoot, ".pruned", "packages", item.ModuleId));
            if (builtIns.SingleOrDefault(module => module.ModuleId == item.ModuleId) is { } builtIn)
                ValidateDirectory(source.ModulesRoot, builtIn.PackageRoot);
        }
        Console.WriteLine("uninstall-components:" + JsonSerializer.Serialize(items.Select(item => item.ModuleId)));
        foreach (var item in items)
        {
            if (builtIns.SingleOrDefault(module => module.ModuleId == item.ModuleId) is { } builtIn)
                DeleteDirectory(source.ModulesRoot, builtIn.PackageRoot);
            if (!store.TryDeleteModule(item.ModuleId, out var error)) throw new IOException(error);
        }
        Check(manager.RequestDesired(inventory.Where(item => !ids.Contains(item.ModuleId)).ToArray(), remainingDesired));
    }

    // 裁决覆盖完整旧来源差异后原子合并到唯一 desired，成功保存才退出旧入口。
    private static void ReconcileLegacyDesired(string installRoot, IReadOnlyList<InstalledPluginInventoryItem> inventory,
        IReadOnlyList<PluginDesiredModuleState> desired, PluginLifecycleManager manager,
        IReadOnlyList<PluginDesiredModuleState> decisions, bool externalPluginsEnabled)
    {
        var legacyPath = LegacyDesiredPath(installRoot);
        if (!File.Exists(legacyPath)) throw new InvalidDataException("PLUGIN_DESIRED_SOURCE_CONFLICT: legacy source is absent or already reconciled.");
        var differences = DescribeSourceDifferences(desired, ReadDesiredSource(installRoot, legacyPath));
        Console.WriteLine("desired-source-differences:" + JsonSerializer.Serialize(differences, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var required = differences.Select(item => item.ModuleId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var ids = decisions.Select(item => item.ModuleId).ToArray();
        if (ids.Length != ids.Distinct(StringComparer.OrdinalIgnoreCase).Count() || !required.SetEquals(ids))
            throw new InvalidDataException("PLUGIN_DESIRED_SOURCE_CONFLICT: decisions must cover every difference exactly once.");
        foreach (var decision in decisions) RequireExternalSelection(externalPluginsEnabled, installRoot, inventory, decision.ModuleId, decision.Version);
        Check(manager.RequestDesired(inventory, desired.Where(item => !required.Contains(item.ModuleId)).Concat(decisions).ToArray()));
        RetireLegacyDesired(installRoot);
    }

    // 只选择已完整接纳的原生包；核验交付输入和库存身份后才一次保存完整 desired。
    private static void SelectBundle(string installRoot, IReadOnlyList<InstalledPluginInventoryItem> inventory,
        IReadOnlyList<PluginDesiredModuleState> desired, PluginLifecycleManager manager, string packagePath,
        bool enableNewMembers, string? decisionsPath, bool externalPluginsEnabled)
    {
        var staging = Path.Combine(installRoot, ".native-selection-" + Guid.NewGuid().ToString("N"));
        try
        {
            var inspector = new ComponentBundleInspector();
            var bundle = (File.Exists(packagePath) ? inspector.ExtractVerified(packagePath, staging) : inspector.Inspect(packagePath)).Manifest;
            foreach (var member in bundle.Components)
            {
                var installed = inventory.SingleOrDefault(item => item.ModuleId == member.ModuleId);
                var packageRoot = installed?.FindPackageRoot(member.Version);
                var receipt = NativeComponentInventory.ReadReceipt(installRoot, member.ModuleId, member.Version);
                if (packageRoot is null || receipt is null)
                    throw new InvalidDataException("PLUGIN_BUNDLE_NOT_INSTALLED: " + member.ModuleId + "@" + member.Version);
                ComponentBundleInspector.RequireReusable(receipt.Component, member);
                NativeComponentInventory.ValidateSnapshot(member, VerifiedModuleArtifactSnapshot.Capture(packageRoot));
            }
            Console.WriteLine("bundle-version-differences:" + JsonSerializer.Serialize(
                OfflineDesiredSelectionService.DescribeDifferences(bundle, desired), new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            var decisions = decisionsPath is null ? [] : ReadVersionDecisions(decisionsPath);
            var selections = OfflineDesiredSelectionService.SelectBundle(bundle, desired, enableNewMembers, decisions);
            foreach (var member in bundle.Components)
            {
                var selected = selections.Single(item => item.ModuleId == member.ModuleId);
                RequireExternalSelection(externalPluginsEnabled, installRoot, inventory, selected.ModuleId, selected.Version);
            }
            Check(manager.RequestDesired(inventory, selections));
        }
        finally { if (Directory.Exists(staging)) DeleteDirectory(installRoot, staging); }
    }

    // 差异裁决只携带组件与版本，不能顺便改写已有成员的启停选择。
    private static BundleVersionDecision[] ReadVersionDecisions(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        if (document.RootElement.ValueKind != JsonValueKind.Array) throw new InvalidDataException("PLUGIN_BUNDLE_VERSION_DECISION_INVALID");
        foreach (var item in document.RootElement.EnumerateArray())
            if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("moduleId", out var id) || id.ValueKind != JsonValueKind.String
                || !item.TryGetProperty("version", out var version) || version.ValueKind != JsonValueKind.String)
                throw new InvalidDataException("PLUGIN_BUNDLE_VERSION_DECISION_INVALID: moduleId and version are required.");
        return document.RootElement.Deserialize<BundleVersionDecision[]>(JsonOptions)!;
    }

    // 关闭第三方开关时只接纳受控原生库存的选版；原选择在唯一 desired 中保留。
    private static void RequireExternalSelection(bool externalPluginsEnabled, string installRoot,
        IReadOnlyList<InstalledPluginInventoryItem> installed, string moduleId, string version)
    {
        if (externalPluginsEnabled) return;
        var item = installed.SingleOrDefault(item => string.Equals(item.ModuleId, moduleId, StringComparison.OrdinalIgnoreCase));
        if (item?.FindPackageRoot(version) is not null && NativeComponentInventory.ReadReceipt(installRoot, item.ModuleId, version) is null)
            throw new InvalidDataException("PLUGIN_EXTERNAL_DISABLED: ordinary external selection is unavailable for " + moduleId);
    }

    // 只裁剪未选定的 installed 包版本；不读取或删除 catalog 归档，也不以旧 active 版本决定 desired。
    private static void Prune(InstalledPluginStateStore store, PluginLifecycleManager manager,
        IReadOnlyList<InstalledPluginInventoryItem> inventory, string id, string version)
    {
        if (manager.Resolve(inventory).DesiredModules.Any(item => item.ModuleId == id && item.Version == version))
            throw new InvalidDataException("PLUGIN_SELECTED_VERSION_CANNOT_BE_PRUNED");
        if (!store.TryLoad(id, out var record, out var error) || record is null || !record.InstalledVersions.Contains(version, StringComparer.Ordinal))
            throw new InvalidDataException(error ?? "PLUGIN_NOT_INSTALLED");
        var remaining = record.InstalledVersions.Where(item => item != version).ToList();
        if (remaining.Count == 0) throw new InvalidDataException("PLUGIN_LAST_VERSION_REQUIRES_UNINSTALL");
        var packageRoot = Path.Combine(store.InstallRoot, "packages", record.ModuleId, version);
        ValidateDirectory(Path.Combine(store.InstallRoot, "packages"), packageRoot);
        record.InstalledVersions = remaining;
        record.Version = remaining[^1];
        record.PackageRoot = Path.Combine(store.InstallRoot, "packages", record.ModuleId, record.Version);
        if (record.CurrentEnabledVersion == version) record.CurrentEnabledVersion = null;
        if (!store.TrySave(record, out error)) throw new IOException(error);
        // 先收敛库存再清包；清理失败只留下未登记的包，不让 inventory 指向已经删除的版本。
        DeleteDirectory(Path.Combine(store.InstallRoot, "packages"), packageRoot);
    }

    // 删除前确认精确范围及整棵树不经过链接；数据归档处于该范围之外。
    private static void DeleteDirectory(string root, string path)
    {
        ValidateDirectory(root, path);
        Directory.Delete(path, recursive: true);
    }

    // 安装和归档包目录都必须精确落在自己的根内；拒绝链接后才能执行递归清理。
    private static void ValidateDirectory(string root, string path)
    {
        if (!PathContainment.Contains(root, path, allowRoot: false)) throw new InvalidDataException("PLUGIN_PACKAGE_PATH_INVALID");
        if (!Directory.Exists(path)) return;
        PathContainment.RejectReparsePoints(path);
        foreach (var entry in Directory.EnumerateFileSystemEntries(path, "*", SearchOption.AllDirectories)) PathContainment.RejectReparsePoints(entry);
    }

    // 批量选择要求明确自身启用值，不能把缺失 enabled 静默解释为 false。
    private static PluginDesiredModuleState[] ReadSelections(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return ReadSelections(document.RootElement);
    }

    // 来源兼容与批量CLI使用同一严格显式选择形状。
    private static PluginDesiredModuleState[] ReadSelections(JsonElement selections)
    {
        if (selections.ValueKind != JsonValueKind.Array) throw new InvalidDataException("PLUGIN_SELECTION_INVALID");
        foreach (var selection in selections.EnumerateArray())
        {
            if (selection.ValueKind != JsonValueKind.Object || !selection.TryGetProperty("moduleId", out var id) || id.ValueKind != JsonValueKind.String
                || !selection.TryGetProperty("version", out var version) || version.ValueKind != JsonValueKind.String
                || !selection.TryGetProperty("enabled", out var enabled) || enabled.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                throw new InvalidDataException("PLUGIN_SELECTION_INVALID: moduleId, version and enabled are required.");
        }
        return selections.Deserialize<PluginDesiredModuleState[]>(JsonOptions)!;
    }

    // 命令失败仅报告未保存的选择；成功不表示已完成组合、迁移或活动提交。
    private static void Check(PluginLifecycleCommandResult result)
    {
        if (!result.IsSuccess) throw new InvalidDataException(result.Message);
    }

    // 固定 name/value 形状与重复拒绝使离线输入没有静默覆盖。
    private static Dictionary<string, string> Parse(string[] args)
    {
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < args.Length; index += 2)
        {
            if (index + 1 >= args.Length || !args[index].StartsWith("--") || !options.TryAdd(args[index][2..], args[index + 1]))
                throw new ArgumentException("PLUGIN_OFFLINE_ARGUMENT_INVALID");
        }
        return options;
    }

    // 必填参数在任何部署写入前读取。
    private static string Required(IReadOnlyDictionary<string, string> options, string key) =>
        options.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : throw new ArgumentException("Missing --" + key);
}
