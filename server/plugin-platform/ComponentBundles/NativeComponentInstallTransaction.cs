using System.Text.Json;
using ICP.Platform.Kernel.ModulePlanning;

namespace ICP.Platform.PluginPlatform.ComponentBundles;

// 只归档本轮文件所有权与原库存字节；不拥有 desired、active 或数据库恢复事实。
internal sealed record NativeInstallMemberRecovery(string ModuleId, string Version, byte[]? OriginalState,
    bool PackageExisted, bool ReceiptExisted);
internal sealed record NativeInstallRecovery(string StagingDirectory, IReadOnlyList<NativeInstallMemberRecovery> Members);
internal sealed record NativeInstallJournal(NativeInstallRecovery Recovery, string Digest);

internal static class NativeComponentInstallTransaction
{
    // journal 是唯一提交屏障；在它存在期间，库存消费者必须失败关闭。
    internal static string JournalPath(string root) => ComponentBundleIntegrity.Resolve(root, "native-install.pending");

    // 在任何目录、receipt 或库存提交前，同卷原子发布已落盘的完整恢复输入。
    internal static void Prepare(string root, NativeInstallRecovery recovery)
    {
        Validate(root, recovery);
        WriteAtomic(JournalPath(root), ComponentBundleIntegrity.Serialize(new NativeInstallJournal(recovery, ComponentBundleIntegrity.Hash(recovery))));
    }

    // 持有安装根独占租约时恢复整个旧集合；中断或失败保留 journal，下次可以重复执行。
    internal static void Recover(string root, Action<string, byte[]>? restoreState = null)
    {
        var journalPath = JournalPath(root);
        if (Directory.Exists(journalPath))
            throw new InvalidDataException("COMPONENT_BUNDLE_INSTALL_INCOMPLETE: recovery journal is not a file; recovery assets retained.");
        if (!File.Exists(journalPath)) return;
        try
        {
            var journal = JsonSerializer.Deserialize<NativeInstallJournal>(PathContainment.ReadContainedFile(root, journalPath), ComponentBundleIntegrity.Json)
                ?? throw new InvalidDataException("empty recovery journal");
            if (journal.Recovery is null || journal.Digest != ComponentBundleIntegrity.Hash(journal.Recovery))
                throw new InvalidDataException("recovery journal digest mismatch");
            Validate(root, journal.Recovery);
            foreach (var member in journal.Recovery.Members)
            {
                var statePath = StatePath(root, member.ModuleId);
                if (member.OriginalState is null) { if (File.Exists(statePath)) File.Delete(statePath); }
                else if (restoreState is null) WriteAtomic(statePath, member.OriginalState);
                else restoreState(statePath, member.OriginalState);
                var receiptPath = NativeComponentInventory.ReceiptPath(root, member.ModuleId, member.Version);
                if (!member.ReceiptExisted && File.Exists(receiptPath)) File.Delete(receiptPath);
                if (!member.PackageExisted) DeleteOwnedTree(root, PackagePath(root, member.ModuleId, member.Version));
            }
            DeleteOwnedTree(root, ComponentBundleIntegrity.Resolve(root, journal.Recovery.StagingDirectory));
            File.Delete(journalPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or ArgumentException)
        {
            throw new InvalidDataException("COMPONENT_BUNDLE_INSTALL_INCOMPLETE: file recovery failed; journal and recovery assets retained.", exception);
        }
    }

    // 所有恢复目标先校验，再开始写入，避免损坏 journal 导致部分清理。
    private static void Validate(string root, NativeInstallRecovery recovery)
    {
        PathContainment.RejectReparsePoints(root);
        if (string.IsNullOrWhiteSpace(recovery.StagingDirectory) || !recovery.StagingDirectory.StartsWith(".native-stage-", StringComparison.Ordinal)
            || !Guid.TryParseExact(recovery.StagingDirectory[".native-stage-".Length..], "N", out _)
            || recovery.Members is null || recovery.Members.Count == 0)
            throw new InvalidDataException("invalid file recovery identity");
        var identities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var member in recovery.Members)
        {
            if (member is null || string.IsNullOrWhiteSpace(member.ModuleId) || string.IsNullOrWhiteSpace(member.Version)
                || member.ModuleId.Contains('/') || member.Version.Contains('/') || !identities.Add(member.ModuleId))
                throw new InvalidDataException("invalid or duplicate file recovery member");
            var statePath = StatePath(root, member.ModuleId);
            var receiptPath = NativeComponentInventory.ReceiptPath(root, member.ModuleId, member.Version);
            var packagePath = PackagePath(root, member.ModuleId, member.Version);
            if (Directory.Exists(statePath) || Directory.Exists(receiptPath) || File.Exists(packagePath))
                throw new InvalidDataException("file recovery target has an incompatible kind");
            if (member.PackageExisted && !Directory.Exists(packagePath) || member.ReceiptExisted && !File.Exists(receiptPath))
                throw new InvalidDataException("retained file recovery asset is missing");
            if (!member.PackageExisted) ValidateTreeIfPresent(packagePath);
        }
        var staging = ComponentBundleIntegrity.Resolve(root, recovery.StagingDirectory);
        if (File.Exists(staging)) throw new InvalidDataException("file recovery staging target has an incompatible kind");
        ValidateTreeIfPresent(staging);
    }

    // 只需要链接校验，不为无界文件集合额外分配路径数组。
    private static void ValidateTreeIfPresent(string path)
    {
        if (!Directory.Exists(path)) return;
        foreach (var _ in ComponentBundleIntegrity.Files(path)) { }
    }

    // 库存路径固定在 state owner 下，不从 journal 读取任意路径。
    internal static string StatePath(string root, string moduleId) => ComponentBundleIntegrity.Resolve(root, "state/" + moduleId + ".json");

    // 资产路径固定在 packages owner 下，版本本身不能跨目录。
    internal static string PackagePath(string root, string moduleId, string version) => ComponentBundleIntegrity.Resolve(root, "packages/" + moduleId + "/" + version);

    // 只删除恢复输入记录的精确安装目录；链接或越界时保留材料并拒绝恢复。
    internal static void DeleteOwnedTree(string root, string path)
    {
        if (!PathContainment.Contains(root, path, allowRoot: false)) throw new InvalidDataException("COMPONENT_BUNDLE_PATH_INVALID: cleanup outside installation.");
        if (!Directory.Exists(path)) return;
        PathContainment.RejectReparsePoints(path);
        ValidateTreeIfPresent(path);
        Directory.Delete(path, recursive: true);
    }

    // 精确字节恢复也先 flush 再同卷移动，进程中断不会留下半截 state 或 receipt。
    internal static void WriteAtomic(string path, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
