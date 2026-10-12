using System.Security.Cryptography;
using System.Text.Json;
using ICP.Platform.Kernel.ModulePlanning;

namespace ICP.Platform.PluginPlatform.ComponentBundles;

// 接纳事实保存在安装根，不来自包字段；只有本机原生安装入口写入它。
internal sealed record NativeComponentReceipt(PlatformRuntimeRelease PlatformRuntime, string BundleDigest, ComponentBundleMember Component);

internal static class NativeComponentInventory
{
    // Host 与正式离线入口已经持有安装根独占租约，先恢复再允许读取库存。
    internal static void Recover(string installRoot) => NativeComponentInstallTransaction.Recover(installRoot);

    // 状态与 receipt 均处于停服租约范围；未完成文件事务不得进入启动候选。
    internal static void EnsureComplete(string installRoot)
    {
        var marker = Path.Combine(installRoot, "native-install.pending");
        if (File.Exists(marker) || Directory.Exists(marker))
            throw new InvalidDataException("COMPONENT_BUNDLE_INSTALL_INCOMPLETE: finish offline recovery before reading inventory.");
    }

    // 严格相对路径由同一包路径规则裁决，不能用身份字段逃出接纳目录。
    internal static string ReceiptPath(string installRoot, string moduleId, string version) =>
        ComponentBundleIntegrity.Resolve(Path.Combine(installRoot, "native-receipts"), moduleId + "/" + version + ".json");

    // 缺失 receipt 是普通外部来源；存在但损坏必须失败，不能降级为另一来源。
    internal static NativeComponentReceipt? ReadReceipt(string installRoot, string moduleId, string version)
    {
        EnsureComplete(installRoot);
        var path = ReceiptPath(installRoot, moduleId, version);
        if (!File.Exists(path)) return null;
        var receipt = JsonSerializer.Deserialize<NativeComponentReceipt>(PathContainment.ReadContainedFile(installRoot, path), ComponentBundleIntegrity.Json)
            ?? throw new InvalidDataException("COMPONENT_BUNDLE_RECEIPT_INVALID: empty acceptance receipt.");
        if (receipt.PlatformRuntime != PlatformRuntimeReleaseIdentity.Current || receipt.Component.ModuleId != moduleId || receipt.Component.Version != version)
            throw new InvalidDataException($"COMPONENT_BUNDLE_RECEIPT_INVALID: '{moduleId}@{version}' identity or runtime mismatch.");
        return receipt;
    }

    // 接纳的每一个字节在签发前重新固定并核对；SourceKind 和公开 Inspect 结果都不能签发此能力。
    internal static ModuleCapsuleCandidate? ReadCandidate(string installRoot, string moduleId, string version,
        string packageRoot, TrustedBuiltInModuleSource source)
    {
        var receipt = ReadReceipt(installRoot, moduleId, version);
        if (receipt is null) return null;
        var expectedRoot = ComponentBundleIntegrity.Resolve(Path.Combine(installRoot, "packages"), moduleId + "/" + version);
        if (!string.Equals(Path.GetFullPath(packageRoot), expectedRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("COMPONENT_BUNDLE_RECEIPT_INVALID: inventory points outside accepted package root.");
        var snapshot = VerifiedModuleArtifactSnapshot.Capture(expectedRoot);
        ValidateSnapshot(receipt.Component, snapshot);
        source.RegisterAcceptedNativeModule(expectedRoot, snapshot);
        return new ModuleCapsuleCandidateReader().ReadTrustedNativeModule(expectedRoot, source);
    }

    // 既定内容身份覆盖完整资产集合，包括实际后端、迁移和前端懒加载文件。
    internal static void ValidateSnapshot(ComponentBundleMember component, VerifiedModuleArtifactSnapshot snapshot)
    {
        var paths = snapshot.EnumerateAllFiles();
        if (paths.Count != component.Files.Count || component.ContentDigest != ComponentBundleIntegrity.Hash(new SortedDictionary<string, string>(component.Files.ToDictionary(pair => pair.Key, pair => pair.Value), StringComparer.Ordinal)))
            throw new InvalidDataException($"COMPONENT_BUNDLE_ASSET_MISMATCH: '{component.ModuleId}' accepted asset set changed.");
        foreach (var path in paths)
            if (!component.Files.TryGetValue(path, out var digest) || Convert.ToHexStringLower(SHA256.HashData(snapshot.ReadFile(path))) != digest)
                throw new InvalidDataException($"COMPONENT_BUNDLE_ASSET_MISMATCH: '{component.ModuleId}/{path}' differs from accepted bytes.");
    }
}
