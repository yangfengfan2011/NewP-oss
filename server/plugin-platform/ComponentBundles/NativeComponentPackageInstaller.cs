using System.Text.Json;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Kernel.PluginPackages;

namespace ICP.Platform.PluginPlatform.ComponentBundles;

/// <summary>仅供持有停服租约的本机部署入口接纳原生发行；安装不修改 desired 或 active。</summary>
internal sealed class NativeComponentPackageInstaller(string installRoot, InstalledPluginStateStore stateStore)
{
    // 先完整验证和暂存，再提交成员资产与接纳事实；异常恢复原库存，进程中断则留下失败关闭标记。
    internal ComponentBundleManifest Install(string packagePath, string? forbiddenPlatformRole = null)
    {
        NativeComponentInventory.EnsureComplete(installRoot);
        var staging = Path.Combine(installRoot, ".native-stage-" + Guid.NewGuid().ToString("N"));
        var marker = Path.Combine(installRoot, "native-install.pending");
        var createdFiles = new List<string>();
        var createdDirectories = new List<string>();
        var oldState = new Dictionary<string, byte[]?>();
        try
        {
            Directory.CreateDirectory(installRoot);
            var inspector = new ComponentBundleInspector();
            ComponentBundleInspection inspection;
            if (File.Exists(packagePath)) inspection = inspector.ExtractVerified(packagePath, staging);
            else
            {
                inspector.Inspect(packagePath);
                CopyTree(packagePath, staging);
                inspection = inspector.Inspect(staging);
            }
            foreach (var component in inspection.Manifest.Components)
            {
                if (component.ModuleId == forbiddenPlatformRole)
                    throw new InvalidDataException("COMPONENT_BUNDLE_PLATFORM_ROLE_FORBIDDEN: bundle cannot replace the distribution platform composition.");
                var target = ComponentBundleIntegrity.Resolve(Path.Combine(installRoot, "packages"), component.ModuleId + "/" + component.Version);
                if (Directory.Exists(target)) NativeComponentInventory.ValidateSnapshot(component, VerifiedModuleArtifactSnapshot.Capture(target));
                var receipt = NativeComponentInventory.ReadReceipt(installRoot, component.ModuleId, component.Version);
                if (receipt is not null) ComponentBundleInspector.RequireReusable(receipt.Component, component);
                if (!stateStore.TryLoad(component.ModuleId, out _, out var error)) throw new IOException(error);
                var statePath = ComponentBundleIntegrity.Resolve(Path.Combine(installRoot, "state"), component.ModuleId + ".json");
                oldState.Add(statePath, File.Exists(statePath) ? File.ReadAllBytes(statePath) : null);
            }
            File.WriteAllText(marker, inspection.Manifest.ContentDigest);
            foreach (var component in inspection.Manifest.Components)
            {
                var target = ComponentBundleIntegrity.Resolve(Path.Combine(installRoot, "packages"), component.ModuleId + "/" + component.Version);
                if (!Directory.Exists(target))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    Directory.Move(ComponentBundleIntegrity.Resolve(staging, component.Root), target);
                    createdDirectories.Add(target);
                }
                var receiptPath = NativeComponentInventory.ReceiptPath(installRoot, component.ModuleId, component.Version);
                if (!File.Exists(receiptPath))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(receiptPath)!);
                    File.WriteAllBytes(receiptPath, ComponentBundleIntegrity.Serialize(new NativeComponentReceipt(inspection.Manifest.PlatformRuntime, inspection.Manifest.ContentDigest, component)));
                    createdFiles.Add(receiptPath);
                }
                if (!stateStore.TryLoad(component.ModuleId, out var previous, out var loadError)) throw new IOException(loadError);
                var record = new InstalledPluginRecord
                {
                    ModuleId = component.ModuleId, Version = component.Version, Status = InstalledPluginLifecycleState.Installed,
                    PackageRoot = target, CurrentEnabledVersion = previous?.CurrentEnabledVersion, IsDisabled = previous?.IsDisabled ?? false,
                    InstalledVersions = (previous?.InstalledVersions ?? []).Append(component.Version).Distinct(StringComparer.Ordinal).ToList()
                };
                if (!stateStore.TrySave(record, out var saveError)) throw new IOException(saveError);
            }
            File.Delete(marker);
            return inspection.Manifest;
        }
        catch
        {
            foreach (var state in oldState)
                if (state.Value is null) File.Delete(state.Key); else File.WriteAllBytes(state.Key, state.Value);
            foreach (var path in createdFiles) File.Delete(path);
            foreach (var path in createdDirectories) DeleteOwnedTree(path);
            File.Delete(marker);
            throw;
        }
        finally { if (Directory.Exists(staging)) DeleteOwnedTree(staging); }
    }

    // 暂存只复制检查器接受的普通文件；不追随包内链接或允许覆盖另一任务目录。
    private static void CopyTree(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var relative in ComponentBundleIntegrity.Files(source))
        {
            var target = ComponentBundleIntegrity.Resolve(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllBytes(target, PathContainment.ReadContainedFile(source, ComponentBundleIntegrity.Resolve(source, relative)));
        }
    }

    // 清理范围必须是当前安装根内本方法创建的精确目录，且树内不含链接。
    private void DeleteOwnedTree(string path)
    {
        if (!PathContainment.Contains(installRoot, path, allowRoot: false)) throw new InvalidDataException("COMPONENT_BUNDLE_PATH_INVALID: cleanup outside installation.");
        _ = ComponentBundleIntegrity.Files(path).ToArray();
        Directory.Delete(path, recursive: true);
    }
}
