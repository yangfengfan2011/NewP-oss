using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Kernel.PluginPackages;

namespace ICP.Platform.PluginPlatform.ComponentBundles;

/// <summary>仅供持有停服租约的本机部署入口接纳原生发行；安装不修改 desired 或 active。</summary>
internal sealed class NativeComponentPackageInstaller(string installRoot, InstalledPluginStateStore stateStore,
    Action<string, string>? copyFile = null, Action<string, string>? moveDirectory = null)
{
    // 整批验证、暂存和持久恢复输入先完成；journal 移除才表示多成员库存全部提交。
    internal ComponentBundleManifest Install(string packagePath, string? forbiddenPlatformRole = null)
    {
        NativeComponentInventory.Recover(installRoot);
        var stagingName = ".native-stage-" + Guid.NewGuid().ToString("N");
        var staging = ComponentBundleIntegrity.Resolve(installRoot, stagingName);
        var journalPath = NativeComponentInstallTransaction.JournalPath(installRoot);
        var prepared = false;
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
            var recovery = new List<NativeInstallMemberRecovery>();
            foreach (var component in inspection.Manifest.Components)
            {
                if (component.ModuleId == forbiddenPlatformRole)
                    throw new InvalidDataException("COMPONENT_BUNDLE_PLATFORM_ROLE_FORBIDDEN: bundle cannot replace the distribution platform composition.");
                var target = NativeComponentInstallTransaction.PackagePath(installRoot, component.ModuleId, component.Version);
                if (File.Exists(target)) throw new InvalidDataException("COMPONENT_BUNDLE_PATH_INVALID: package target is a file.");
                if (Directory.Exists(target)) NativeComponentInventory.ValidateSnapshot(component, VerifiedModuleArtifactSnapshot.Capture(target));
                var receipt = NativeComponentInventory.ReadReceipt(installRoot, component.ModuleId, component.Version);
                if (receipt is not null) ComponentBundleInspector.RequireReusable(receipt.Component, component);
                if (!stateStore.TryLoad(component.ModuleId, out _, out var error)) throw new IOException(error);
                var statePath = NativeComponentInstallTransaction.StatePath(installRoot, component.ModuleId);
                recovery.Add(new(component.ModuleId, component.Version,
                    File.Exists(statePath) ? PathContainment.ReadContainedFile(installRoot, statePath) : null,
                    Directory.Exists(target), receipt is not null));
            }
            NativeComponentInstallTransaction.Prepare(installRoot, new(stagingName, recovery));
            prepared = true;
            foreach (var component in inspection.Manifest.Components)
            {
                var target = NativeComponentInstallTransaction.PackagePath(installRoot, component.ModuleId, component.Version);
                if (!Directory.Exists(target))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    (moveDirectory ?? Directory.Move)(ComponentBundleIntegrity.Resolve(staging, component.Root), target);
                }
                var receiptPath = NativeComponentInventory.ReceiptPath(installRoot, component.ModuleId, component.Version);
                if (!File.Exists(receiptPath))
                    NativeComponentInstallTransaction.WriteAtomic(receiptPath, ComponentBundleIntegrity.Serialize(
                        new NativeComponentReceipt(inspection.Manifest.PlatformRuntime, inspection.Manifest.ContentDigest, component)));
                if (!stateStore.TryLoad(component.ModuleId, out var previous, out var loadError)) throw new IOException(loadError);
                var record = new InstalledPluginRecord
                {
                    ModuleId = component.ModuleId, Version = component.Version, Status = InstalledPluginLifecycleState.Installed,
                    PackageRoot = target, CurrentEnabledVersion = previous?.CurrentEnabledVersion, IsDisabled = previous?.IsDisabled ?? false,
                    InstalledVersions = (previous?.InstalledVersions ?? []).Append(component.Version).Distinct(StringComparer.Ordinal).ToList()
                };
                if (!stateStore.TrySave(record, out var saveError)) throw new IOException(saveError);
            }
            NativeComponentInstallTransaction.DeleteOwnedTree(installRoot, staging);
            File.Delete(journalPath);
            return inspection.Manifest;
        }
        catch (Exception failure)
        {
            if (prepared)
            {
                try { NativeComponentInventory.Recover(installRoot); }
                catch (Exception recoveryFailure) when (recoveryFailure is IOException or UnauthorizedAccessException or InvalidDataException)
                {
                    throw new IOException("COMPONENT_BUNDLE_INSTALL_INCOMPLETE: install and file recovery failed; recovery inputs retained.", new AggregateException(failure, recoveryFailure));
                }
            }
            throw;
        }
        finally
        {
            if (!File.Exists(journalPath)) NativeComponentInstallTransaction.DeleteOwnedTree(installRoot, staging);
        }
    }

    // 暂存只复制检查器接受的普通文件；不追随包内链接或允许覆盖另一任务目录。
    private void CopyTree(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var relative in ComponentBundleIntegrity.Files(source))
        {
            var target = ComponentBundleIntegrity.Resolve(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            var sourcePath = ComponentBundleIntegrity.Resolve(source, relative);
            if (copyFile is null) File.WriteAllBytes(target, PathContainment.ReadContainedFile(source, sourcePath));
            else copyFile(sourcePath, target);
        }
    }
}
