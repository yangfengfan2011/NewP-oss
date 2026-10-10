using System.Text.Json;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Kernel.PluginPackages;
using ICP.Platform.PluginPlatform.Lifecycle;
using ICP.Platform.PluginPlatform.Activation;
using ICP.Platform.PluginPlatform.StandardData;

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

    // builtin 信任仅由当前发行 Host 签发；外部命令不能指定任意目录成为可信内建来源。
    internal static int Run(string[] args, string installRoot, TrustedBuiltInModuleSource source,
        bool externalPluginsEnabled, StandardDataPackageInspector inspector, PluginLifecycleManager manager)
    {
        try
        {
            var options = Parse(args[2..]);
            var store = new InstalledPluginStateStore(installRoot);
            var installed = externalPluginsEnabled ? InstalledPluginInventoryAdapter.Create(store) : [];
            var builtIns = new ModuleCapsuleCandidateReader().ReadTrustedBuiltIn(source);
            var inventory = installed.Concat(builtIns.Where(module => !installed.Any(item => item.ModuleId == module.ModuleId))
                .Select(module => new InstalledPluginInventoryItem(module.ModuleId, [module.Version], module.Version))).ToArray();
            var current = manager.Resolve(installed);
            var desired = current.DesiredModules.Concat(builtIns.Where(module => !current.DesiredModules.Any(item => item.ModuleId == module.ModuleId))
                .Select(module => new PluginDesiredModuleState(module.ModuleId, module.Version, true))).ToArray();
            var action = args[1].ToLowerInvariant();
            switch (action)
            {
                case "install":
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
                            var result = new UnifiedPluginPackageInstaller(store).Install(package, installRoot);
                            if (result.Status != "Installed") throw new InvalidDataException(result.ErrorMessage);
                        }
                    }
                    break;
                case "desired":
                    var selections = ReadSelections(Required(options, "file"));
                    Check(manager.RequestDesired(inventory, desired.Where(item => !selections.Any(selection => selection.ModuleId == item.ModuleId)).Concat(selections).ToArray()));
                    break;
                case "select":
                case "enable":
                    var selectedId = Required(options, "module-id");
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
                    Check(manager.RequestDesired(inventory, desired.Where(item => item.ModuleId != rollbackId).Append(new(rollbackId, rollbackVersion, true)).ToArray()));
                    break;
                case "uninstall":
                    var id = Required(options, "module-id");
                    var item = inventory.SingleOrDefault(item => item.ModuleId == id) ?? throw new InvalidDataException("PLUGIN_NOT_INSTALLED");
                    // 必需身份先由同一规范编译器裁决；可选成员引用在卸载后的完整启动候选中验证。
                    var plan = new StandardDataCandidatePlanCompiler(source, inspector).Compile(externalPluginsEnabled ? installed : [], current with { DesiredModules = desired });
                    if (plan.RequiredModuleIds.Contains(id)) throw new InvalidDataException("PLATFORM_REQUIRED_MODULE_MISSING: " + id);
                    var builtIn = builtIns.SingleOrDefault(module => module.ModuleId == id);
                    ValidateDirectory(Path.Combine(installRoot, "packages"), Path.Combine(installRoot, "packages", item.ModuleId));
                    ValidateDirectory(Path.Combine(installRoot, ".pruned", "packages"), Path.Combine(installRoot, ".pruned", "packages", item.ModuleId));
                    if (builtIn is not null) DeleteDirectory(source.ModulesRoot, builtIn.PackageRoot);
                    if (!store.TryDeleteModule(item.ModuleId, out var error)) throw new IOException(error);
                    Check(manager.RequestDesired(inventory.Where(item => item.ModuleId != id).ToArray(), desired.Where(item => item.ModuleId != id).ToArray()));
                    break;
                case "prune":
                    Prune(store, manager, inventory, Required(options, "module-id"), Required(options, "version"));
                    break;
                default: throw new ArgumentException("PLUGIN_OFFLINE_ACTION_UNSUPPORTED");
            }
            Console.WriteLine(action + ":PendingRestart");
            return 0;
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or ArgumentException or JsonException or FormatException or PluginActivationException)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
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
        if (document.RootElement.ValueKind != JsonValueKind.Array) throw new InvalidDataException("PLUGIN_SELECTION_INVALID");
        foreach (var selection in document.RootElement.EnumerateArray())
        {
            if (selection.ValueKind != JsonValueKind.Object || !selection.TryGetProperty("moduleId", out var id) || id.ValueKind != JsonValueKind.String
                || !selection.TryGetProperty("version", out var version) || version.ValueKind != JsonValueKind.String
                || !selection.TryGetProperty("enabled", out var enabled) || enabled.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                throw new InvalidDataException("PLUGIN_SELECTION_INVALID: moduleId, version and enabled are required.");
        }
        return document.RootElement.Deserialize<PluginDesiredModuleState[]>(JsonOptions)!;
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
