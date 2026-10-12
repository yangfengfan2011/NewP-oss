using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using ICP.Platform.HostApi.PluginOperations;
using ICP.Platform.Kernel.PluginPackages;
using ICP.Platform.PluginPlatform.ComponentBundles;
using ICP.Platform.Testing;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed class NativeComponentInstallRecoveryProcessTests
{
    private const string WorkerEnvironment = "NEWP_RUN_NATIVE_INSTALL_PROCESS_WORKER";

    [Fact]
    public async Task FormalOfflineOperation_RejectsInstallationRootHeldByAnotherOwner()
    {
        using var input = new ComponentBundleTestInput();
        var root = Path.Combine(input.Root, "installed");
        using var lease = PluginDeploymentLease.Acquire(root);
        var result = await RunHostAsync(input, root, input.Root, "held-root",
            ["plugin-deploy", "install-native", "--package", input.Output]);
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("PLUGIN_HOST_MUST_BE_STOPPED", result.Output);
        Assert.False(File.Exists(Path.Combine(root, "native-install.pending")));
        Assert.Empty(Directory.GetFiles(root, "*.json", SearchOption.AllDirectories));
    }

    [Theory]
    [InlineData("offline", false, false)]
    [InlineData("host", false, false)]
    [InlineData("offline", true, false)]
    [InlineData("host", true, false)]
    [InlineData("offline", false, true)]
    public async Task KilledInstall_FormalConsumerRecoversOrFailsClosed(string consumer, bool corruptJournal, bool killRecovery)
    {
        using var input = new ComponentBundleTestInput();
        var root = Path.Combine(input.Root, "installed");
        var capsule = JsonNode.Parse(File.ReadAllText(input.Capsule))!;
        capsule["version"] = "0.9.0";
        File.WriteAllText(input.Capsule, capsule.ToJsonString());
        new ComponentBundleBuilder().Stage(input.Repository, "InforCenter", input.Output + "-old", input.Artifacts);
        new NativeComponentPackageInstaller(root, new InstalledPluginStateStore(root)).Install(input.Output + "-old");
        capsule["version"] = "1.0.0";
        File.WriteAllText(input.Capsule, capsule.ToJsonString());
        input.IncludePart();
        new ComponentBundleBuilder().Stage(input.Repository, "InforCenter", input.Output, input.Artifacts);
        var protectedFiles = SeedProtectedFiles(root);
        var evidence = Path.Combine(input.SourceRepository, ".codex-temp", "issue-267", "272", "process-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(evidence);
        var checkpoint = Path.Combine(input.Root, "checkpoint");
        await RunAndKillWorkerAsync(input, root, checkpoint, evidence, "install");
        var store = new InstalledPluginStateStore(root);
        Assert.True(store.TryLoad("InforCenter", out var partial, out _));
        Assert.Equal("1.0.0", partial!.Version);
        Assert.True(File.Exists(Path.Combine(root, "native-receipts", "InforCenter", "1.0.0.json")));
        Assert.True(Directory.Exists(Path.Combine(root, "packages", "InforCenter", "1.0.0")));
        Assert.False(File.Exists(Path.Combine(root, "state", "Part.json")));
        Assert.True(File.Exists(Path.Combine(root, "native-install.pending")));
        Assert.Throws<InvalidDataException>(() => NativeComponentInventory.EnsureComplete(root));
        WriteInventoryEvidence(evidence, "after-killed-install", root, store);
        File.Copy(Path.Combine(root, "native-install.pending"), Path.Combine(evidence, "journal-before-recovery.json"));
        if (killRecovery)
        {
            File.Delete(checkpoint);
            await RunAndKillWorkerAsync(input, root, checkpoint, evidence, "recover");
            Assert.True(store.TryLoad("InforCenter", out var restored, out _));
            Assert.Equal("0.9.0", restored!.Version);
            Assert.True(File.Exists(Path.Combine(root, "native-install.pending")));
            Assert.True(Directory.Exists(Path.Combine(root, "packages", "InforCenter", "1.0.0")));
            WriteInventoryEvidence(evidence, "after-killed-recovery", root, store);
        }
        if (corruptJournal) File.AppendAllText(Path.Combine(root, "native-install.pending"), "corrupt recovery facts");
        var journalBytes = File.ReadAllBytes(Path.Combine(root, "native-install.pending"));
        var result = await RunHostAsync(input, root, evidence, consumer,
            consumer == "host" ? [] : ["plugin-deploy", "select", "--module-id", "UnavailableAfterFileRecovery", "--version", "1.0.0"]);
        Assert.NotEqual(0, result.ExitCode);
        Assert.DoesNotContain("Now listening", result.Output);
        WriteInventoryEvidence(evidence, "after-formal-consumer", root, store);
        if (corruptJournal)
        {
            Assert.Contains("COMPONENT_BUNDLE_INSTALL_INCOMPLETE", result.Output);
            Assert.Equal(journalBytes, File.ReadAllBytes(Path.Combine(root, "native-install.pending")));
            Assert.True(Directory.Exists(Path.Combine(root, "packages", "InforCenter", "1.0.0")));
            Assert.Equal("1.0.0", Assert.Single(store.LoadAll()).Version);
        }
        else
        {
            Assert.Contains(consumer == "host" ? "UnavailableAfterFileRecovery@1.0.0" : "PLUGIN_SELECTION_INVALID", result.Output);
            Assert.False(File.Exists(Path.Combine(root, "native-install.pending")));
            Assert.False(Directory.Exists(Path.Combine(root, "packages", "InforCenter", "1.0.0")));
            Assert.False(Directory.Exists(Path.Combine(root, "packages", "Part", "1.0.0")));
            Assert.Equal("0.9.0", Assert.Single(store.LoadAll()).Version);
            foreach (var item in protectedFiles) Assert.Equal(item.Value, File.ReadAllBytes(Path.Combine(root, item.Key)));
            WriteInventoryEvidence(evidence, "recovered-old-inventory", root, store);
            for (var retry = 0; retry < 2; retry++)
            {
                var installed = await RunHostAsync(input, root, evidence, "install-" + retry,
                    ["plugin-deploy", "install-native", "--package", input.Output]);
                Assert.True(installed.ExitCode == 0, installed.Output);
                WriteInventoryEvidence(evidence, "formal-reinstall-" + retry, root, store);
            }
            Assert.Equal(["0.9.0", "1.0.0"], store.LoadAll().Single(item => item.ModuleId == "InforCenter").InstalledVersions);
            Assert.Equal(["InforCenter", "Part"], store.LoadAll().Select(item => item.ModuleId).Order().ToArray());
        }
        foreach (var item in protectedFiles.Where(item => !item.Key.StartsWith("state/")))
            Assert.Equal(item.Value, File.ReadAllBytes(Path.Combine(root, item.Key)));
        File.WriteAllBytes(Path.Combine(evidence, "summary.json"), JsonSerializer.SerializeToUtf8Bytes(new
        {
            consumer, corruptJournal, killRecovery, firstMemberCommitted = true, remainingMemberUncommitted = true,
            installProcessTerminated = true, recoveryProcessTerminated = killRecovery,
            journalRetained = File.Exists(Path.Combine(root, "native-install.pending")), result.ExitCode,
            installed = store.LoadAll().Select(item => new { item.ModuleId, item.Version, item.InstalledVersions }),
            protectedStatePreserved = true
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("NATIVE_INSTALL_PROCESS_EVIDENCE: " + evidence);
    }

    // 测试进程仅通过现有文件 I/O 接缝暂停；真实安装/恢复执行并持有正式安装根租约。
    [EnvironmentGatedFact(WorkerEnvironment, "多成员文件事务真实进程故障注入 worker")]
    public void NativeInstallProcessWorker()
    {
        var root = Environment.GetEnvironmentVariable("NEWP_NATIVE_INSTALL_WORKER_ROOT")!;
        var checkpoint = Environment.GetEnvironmentVariable("NEWP_NATIVE_INSTALL_WORKER_CHECKPOINT")!;
        using var lease = PluginDeploymentLease.Acquire(root);
        if (Environment.GetEnvironmentVariable("NEWP_NATIVE_INSTALL_WORKER_PHASE") == "recover")
        {
            NativeComponentInstallTransaction.Recover(root, (path, bytes) =>
            {
                NativeComponentInstallTransaction.WriteAtomic(path, bytes);
                PauseAfterCommit(checkpoint);
            });
        }
        else
        {
            var store = new InstalledPluginStateStore(root, (source, target, _) =>
            {
                File.Move(source, target, true);
                PauseAfterCommit(checkpoint);
            }, (source, target) =>
            {
                File.Move(source, target);
                PauseAfterCommit(checkpoint);
            });
            new NativeComponentPackageInstaller(root, store).Install(Environment.GetEnvironmentVariable("NEWP_NATIVE_INSTALL_WORKER_PACKAGE")!);
        }
        throw new InvalidOperationException("worker must be terminated at the durable file boundary");
    }

    private static void PauseAfterCommit(string checkpoint)
    {
        File.WriteAllText(checkpoint, "state file committed");
        // 父测试 Kill 精确进程树，故意不走安装器 catch 或 finally。
        Thread.Sleep(Timeout.Infinite);
    }

    private static void WriteInventoryEvidence(string evidence, string phase, string root, InstalledPluginStateStore store)
    {
        File.AppendAllText(Path.Combine(evidence, "inventory-phases.jsonl"), JsonSerializer.Serialize(new
        {
            phase, journalExists = File.Exists(Path.Combine(root, "native-install.pending")),
            inventory = store.LoadAll().Select(item => new { item.ModuleId, item.Version, item.InstalledVersions }),
            currentStateSha256 = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(Path.Combine(root, "state", "InforCenter.json")))),
            newPackageExists = Directory.Exists(Path.Combine(root, "packages", "InforCenter", "1.0.0")),
            newReceiptExists = File.Exists(Path.Combine(root, "native-receipts", "InforCenter", "1.0.0.json"))
        }) + Environment.NewLine);
    }

    private static Dictionary<string, byte[]> SeedProtectedFiles(string root)
    {
        var files = Directory.GetFiles(Path.Combine(root, "packages"), "*", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(Path.Combine(root, "native-receipts"), "*", SearchOption.AllDirectories))
            .Append(Path.Combine(root, "state", "InforCenter.json"))
            .ToDictionary(path => Path.GetRelativePath(root, path).Replace('\\', '/'), File.ReadAllBytes, StringComparer.Ordinal);
        // 非规范空白与 BOM 仍是合法旧库存；恢复必须保留原字节，不能重新序列化。
        files["state/InforCenter.json"] = new byte[] { 0xEF, 0xBB, 0xBF, 0x20, 0x0D, 0x0A }
            .Concat(files["state/InforCenter.json"]).ToArray();
        files.Add("lifecycle/v2/state.json", JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = "2.0", revision = 0,
            desiredModules = new[]
            {
                new { moduleId = "InforCenter", version = "0.9.0", enabled = false },
                new { moduleId = "UnavailableAfterFileRecovery", version = "1.0.0", enabled = true }
            }
        }));
        files.Add("persistence/preserved-catalog/state.json", "original pending and successful references"u8.ToArray());
        files.Add("persistence/preserved-catalog/assets/recovery.bin", "original recovery asset"u8.ToArray());
        foreach (var item in files)
        {
            var path = Path.Combine(root, item.Key);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, item.Value);
        }
        return files;
    }

    private static async Task RunAndKillWorkerAsync(ComponentBundleTestInput input, string root, string checkpoint, string evidence, string phase)
    {
        var start = new ProcessStartInfo("dotnet") { WorkingDirectory = input.SourceRepository,
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in new[] { "test", typeof(NativeComponentInstallRecoveryProcessTests).Assembly.Location,
            "--filter", "FullyQualifiedName~NativeInstallProcessWorker", "--logger", "console;verbosity=normal" })
            start.ArgumentList.Add(argument);
        start.Environment[WorkerEnvironment] = "1";
        start.Environment["NEWP_NATIVE_INSTALL_WORKER_ROOT"] = root;
        start.Environment["NEWP_NATIVE_INSTALL_WORKER_PACKAGE"] = input.Output;
        start.Environment["NEWP_NATIVE_INSTALL_WORKER_CHECKPOINT"] = checkpoint;
        start.Environment["NEWP_NATIVE_INSTALL_WORKER_PHASE"] = phase;
        using var process = Process.Start(start) ?? throw new InvalidOperationException("worker did not start");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            while (!File.Exists(checkpoint))
            {
                Assert.False(process.HasExited, "worker exited before its durable checkpoint");
                await Task.Delay(25, timeout.Token);
            }
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
            await File.WriteAllTextAsync(Path.Combine(evidence, phase + ".stdout.log"), await stdout);
            await File.WriteAllTextAsync(Path.Combine(evidence, phase + ".stderr.log"), await stderr);
        }
        Assert.NotEqual(0, process.ExitCode);
    }

    private static async Task<(int ExitCode, string Output)> RunHostAsync(ComponentBundleTestInput input, string root, string evidence, string phase, string[] arguments)
    {
        var output = new DirectoryInfo(AppContext.BaseDirectory);
        var host = Path.Combine(input.SourceRepository, "apps", "host-api", "bin", output.Parent!.Name, output.Name, "HostApi.dll");
        var start = new ProcessStartInfo("dotnet") { WorkingDirectory = Path.GetDirectoryName(host)!,
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(host);
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        start.Environment["Platform__PluginInstallRoot"] = root;
        start.Environment["Platform__ExternalPluginsEnabled"] = "false";
        start.Environment["ConnectionStrings__SqlServer"] = "Server=invalid-do-not-connect;Database=NativeRecoveryNoConnection;Integrated Security=true;Connect Timeout=1";
        start.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        using var process = Process.Start(start) ?? throw new InvalidOperationException("formal Host did not start");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30)); }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
        }
        var result = await stdout + Environment.NewLine + await stderr;
        await File.WriteAllTextAsync(Path.Combine(evidence, phase + ".log"), result);
        return (process.ExitCode, result);
    }
}
