using System.Diagnostics;
using System.Reflection;
using System.Text;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Kernel.PluginPackages;
using ICP.Platform.Plugin.Contracts;
using ICP.Platform.PluginPlatform.Lifecycle;
using ICP.Platform.PluginPlatform.StandardData;
using Xunit;

namespace ICP.Platform.Kernel.Tests;

public class PluginOpsIntegrationTests : IDisposable
{
    private readonly UnifiedPluginPackageTestData _testData = new();
    private readonly List<string> _tempRoots = new();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Compile_ExternalDisabledKeepsDistributionMembersDespiteInstalledAliases(bool desiredEnabled)
    {
        var installRoot = CreateTempRoot("plugin_ops_disabled_aliases");
        CopyHostDistribution(installRoot);
        var baseline = CompileSelectedCandidate(installRoot, externalPluginsEnabled: false);
        var store = new InstalledPluginStateStore(installRoot);
        var installer = new UnifiedPluginPackageInstaller(store);
        foreach (var id in new[] { "Audit", "Files" })
            Assert.Equal("Installed", installer.Install(_testData.CreateValidPackage(id, "2.0.0"), installRoot).Status);
        var manager = new PluginLifecycleManager(new PluginLifecycleStateStore(installRoot, new PluginLifecycleTestDeployment()));
        var inventory = InstalledPluginInventoryAdapter.Create(store);
        Assert.True(manager.RequestDesired(inventory, [new("Audit", "2.0.0", desiredEnabled), new("Files", "2.0.0", desiredEnabled)]).IsSuccess);
        var desiredPath = Path.Combine(installRoot, "lifecycle", "v2", "state.json");
        var desired = File.ReadAllBytes(desiredPath);
        var recorded = manager.ReadRecordedDesired(inventory);

        // 真实发行 Capsule 的必需成员不能因不可消费的 ordinary external 同 ID 库存而消失。
        var plan = CompileSelectedCandidate(installRoot, externalPluginsEnabled: false);
        Assert.Equal(baseline.CanonicalBytes, plan.CanonicalBytes);
        foreach (var id in new[] { "Audit", "Files" })
            Assert.True(Assert.Single(plan.Modules, module => module.ModuleId == id).Provenance.AuthenticatesBuiltIn(
                Assert.Single(plan.Modules, module => module.ModuleId == id)));
        Assert.Equal(desired, File.ReadAllBytes(desiredPath));
        Assert.Equal(recorded, manager.ReadRecordedDesired(InstalledPluginInventoryAdapter.Create(store)));
    }

    [Fact]
    public void Install_UnselectedVersions_PreservesDesiredPackageAndCompleteCompositionUntilExplicitPrune()
    {
        var installRoot = CreateTempRoot("plugin_ops_selected_retention");
        var versionTwo = _testData.CreateValidPackage("Part", "2.0.0");
        var first = PluginOpsProcess.Run("install", ["--package", versionTwo, "--install-root", installRoot], externalPluginsEnabled: true);
        Assert.True(first.ExitCode == 0, first.StdOut + first.StdErr);
        var selected = PluginOpsProcess.Run("select", ["--module-id", "Part", "--version", "2.0.0", "--enabled", "true", "--install-root", installRoot], externalPluginsEnabled: true);
        Assert.True(selected.ExitCode == 0, selected.StdOut + selected.StdErr);
        var desiredPath = Path.Combine(installRoot, "lifecycle", "v2", "state.json");
        var desired = File.ReadAllBytes(desiredPath);
        var before = CompileSelectedCandidate(installRoot);
        var member = Assert.Single(before.CompositionGraph, node => node.ModuleId == "Part");
        Assert.Equal("2.0.0", member.Version);
        Assert.Equal("InforCenter", member.ParentModuleId);
        foreach (var version in new[] { "3.0.0", "4.0.0" })
        {
            var installed = PluginOpsProcess.Run("install", ["--package", _testData.CreateValidPackage("Part", version), "--install-root", installRoot], externalPluginsEnabled: true);
            Assert.True(installed.ExitCode == 0, installed.StdOut + installed.StdErr);
        }
        Assert.Equal(desired, File.ReadAllBytes(desiredPath));
        var after = CompileSelectedCandidate(installRoot);
        Assert.Equal(before.CanonicalBytes, after.CanonicalBytes);
        var store = new InstalledPluginStateStore(installRoot);
        Assert.True(store.TryLoad("Part", out var record, out _));
        Assert.Equal(["2.0.0", "3.0.0", "4.0.0"], record!.InstalledVersions);
        Assert.Null(record.CurrentEnabledVersion);
        Assert.True(Directory.Exists(Path.Combine(installRoot, "packages", "Part", "2.0.0")));
        Assert.False(Directory.Exists(Path.Combine(installRoot, ".pruned", "packages", "Part", "2.0.0")));
        var rejected = PluginOpsProcess.Run("prune", ["--module-id", "Part", "--version", "2.0.0", "--install-root", installRoot], externalPluginsEnabled: true);
        Assert.NotEqual(0, rejected.ExitCode);
        Assert.Contains("PLUGIN_SELECTED_VERSION_CANNOT_BE_PRUNED", rejected.StdErr);
        var pruned = PluginOpsProcess.Run("prune", ["--module-id", "Part", "--version", "3.0.0", "--install-root", installRoot], externalPluginsEnabled: true);
        Assert.True(pruned.ExitCode == 0, pruned.StdOut + pruned.StdErr);
        Assert.False(Directory.Exists(Path.Combine(installRoot, "packages", "Part", "3.0.0")));
        Assert.Equal(desired, File.ReadAllBytes(desiredPath));
        Assert.Equal(before.CanonicalBytes, CompileSelectedCandidate(installRoot).CanonicalBytes);
    }

    // 复用正式选定候选消费者；反射仅跨越保护builtin签发的内部构造入口，不构造手工计划或装载夹具代码。
    private static CanonicalModulePlan CompileSelectedCandidate(string installRoot, bool externalPluginsEnabled = true)
    {
        var hostRoot = Path.Combine(installRoot, "host-distribution");
        var source = TrustedBuiltInModuleSource.CreateForHostDistribution(hostRoot, Path.Combine(hostRoot, "modules"));
        var inspector = new StandardDataPackageInspector(new PlatformApiHostCatalog(PlatformApiReleaseIdentity.Version,
            [PlatformApiReleaseIdentity.StandardDataProfile], [new(PlatformApiReleaseIdentity.Version, PlatformApiReleaseIdentity.ReleaseDigest)]));
        var constructor = typeof(StandardDataCandidatePlanCompiler).GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null,
            [typeof(TrustedBuiltInModuleSource), typeof(StandardDataPackageInspector), typeof(IEnumerable<string>), typeof(string), typeof(bool)], null)!;
        var compiler = (StandardDataCandidatePlanCompiler)constructor.Invoke([source, inspector, null, installRoot, externalPluginsEnabled]);
        var inventory = InstalledPluginInventoryAdapter.Create(new InstalledPluginStateStore(installRoot));
        return compiler.Compile(inventory, new PluginLifecycleManager(new PluginLifecycleStateStore(installRoot, new PluginLifecycleTestDeployment())).Resolve(inventory));
    }

    [Fact]
    public void Install_WhenCurrentVersionAlreadyEnabled_LeavesRuntimeProjectionForHostRestart()
    {
        var installRoot = CreateTempRoot("plugin_ops_install");
        var initialPackageRoot = _testData.CreateValidPackage("Audit", "1.0.0");
        var replacementPackageRoot = _testData.CreateValidPackage("Audit", "1.0.0");

        var stateStore = new InstalledPluginStateStore(installRoot);
        var installer = new UnifiedPluginPackageInstaller(stateStore);

        Assert.Equal("Installed", installer.Install(initialPackageRoot, installRoot).Status);
        Assert.True(stateStore.TryLoad("Audit", out var record, out var loadError));
        Assert.Null(loadError);
        Assert.NotNull(record);
        record!.CurrentEnabledVersion = "1.0.0";
        Assert.True(stateStore.TrySave(record, out var saveError));
        Assert.Null(saveError);

        new RuntimeViewMaterializer(stateStore).Materialize(installRoot);

        var projectedEntryPath = Path.Combine(installRoot, "public", "Audit", "1.0.0", "assets", "entry.js");
        Assert.Equal("export default 'audit';\n", File.ReadAllText(projectedEntryPath));

        File.WriteAllText(
            Path.Combine(replacementPackageRoot, "frontend", "assets", "entry.js"),
            "export default 'updated-audit';\n");

        var result = PluginOpsProcess.Run(
            "install",
            [
                "--package", replacementPackageRoot,
                "--install-root", installRoot
            ], externalPluginsEnabled: true);

        Assert.True(result.ExitCode == 0, result.StdOut + result.StdErr);
        Assert.Contains("install:PendingRestart", result.StdOut, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("export default 'audit';\n", File.ReadAllText(projectedEntryPath));
    }

    [Fact]
    public void Uninstall_WhenModuleIsInstalled_RemovesStateAndPackages()
    {
        var installRoot = CreateTempRoot("plugin_ops_uninstall");
        var packageRoot = _testData.CreateValidPackage("Part", "1.0.0");

        var stateStore = new InstalledPluginStateStore(installRoot);
        var installer = new UnifiedPluginPackageInstaller(stateStore);

        Assert.Equal("Installed", installer.Install(packageRoot, installRoot).Status);

        // 正式发行仍声明 InforCenter 的三个成员；单独删除 Part 必须先拒绝且不修改库存或代码。
        CopyHostDistribution(installRoot);
        var removedIds = new[] { "InforCenter", "Workspace", "Part", "Document" };
        var modulesRoot = Path.Combine(installRoot, "host-distribution", "modules");
        var statePath = Path.Combine(installRoot, "state", "Part.json");
        var stateBytes = File.ReadAllBytes(statePath);
        var protectedFiles = Directory.GetFiles(Path.Combine(installRoot, "packages", "Part"), "*", SearchOption.AllDirectories)
            .Concat(removedIds.SelectMany(id => Directory.GetFiles(Path.Combine(modulesRoot, id), "*", SearchOption.AllDirectories)))
            .ToDictionary(path => path, File.ReadAllBytes);
        var blocked = PluginOpsProcess.Run(
            "uninstall",
            [
                "--module-id", "Part",
                "--install-root", installRoot
            ]);

        Assert.NotEqual(0, blocked.ExitCode);
        Assert.Contains("MODULE_COMPOSITION_MEMBER_MISSING", blocked.StdErr);
        Assert.Equal(stateBytes, File.ReadAllBytes(statePath));
        foreach (var file in protectedFiles) Assert.Equal(file.Value, File.ReadAllBytes(file.Key));
        var desiredPath = Path.Combine(installRoot, "lifecycle", "v2", "state.json");
        Assert.False(File.Exists(desiredPath));

        // 通过调用方精确列出的完整集合解除组合和活动依赖，不改写真实发行声明。
        var selections = Path.Combine(installRoot, "uninstall.json");
        File.WriteAllText(selections, System.Text.Json.JsonSerializer.Serialize(removedIds));
        var result = PluginOpsProcess.Run("uninstall", ["--file", selections, "--install-root", installRoot]);
        Assert.True(result.ExitCode == 0, result.StdOut + result.StdErr);
        Assert.Contains("uninstall-components:[\"InforCenter\",\"Workspace\",\"Part\",\"Document\"]", result.StdOut);
        Assert.Contains("uninstall:PendingRestart", result.StdOut, StringComparison.OrdinalIgnoreCase);
        Assert.True(stateStore.TryLoad("Part", out var record, out var loadError));
        Assert.Null(loadError);
        Assert.Null(record);
        Assert.False(Directory.Exists(Path.Combine(installRoot, "packages", "Part")));
        foreach (var id in removedIds) Assert.False(Directory.Exists(Path.Combine(modulesRoot, id)));
        Assert.True(Directory.Exists(Path.Combine(modulesRoot, "Audit")));
        using var lifecycle = System.Text.Json.JsonDocument.Parse(File.ReadAllBytes(desiredPath));
        var desiredIds = lifecycle.RootElement.GetProperty("desiredModules").EnumerateArray()
            .Select(item => item.GetProperty("moduleId").GetString()).ToArray();
        Assert.DoesNotContain(desiredIds, id => removedIds.Contains(id));
        Assert.Contains("Audit", desiredIds);
    }

    [Fact]
    public void Desired_MissingOwnChoice_IsRejectedWithoutWritingLifecycleState()
    {
        var installRoot = CreateTempRoot("plugin_ops_invalid_desired");
        var file = Path.Combine(installRoot, "desired.json");
        File.WriteAllText(file, "[{\"moduleId\":\"Part\",\"version\":\"1.0.0\"}]");
        var result = PluginOpsProcess.Run("desired", ["--file", file, "--install-root", installRoot]);
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("PLUGIN_SELECTION_INVALID", result.StdErr);
        Assert.False(File.Exists(Path.Combine(installRoot, ".feature-gates", "external-plugins-disabled", "lifecycle", "v2", "state.json")));
    }

    [Fact]
    public void Prune_UnselectedVersion_RemovesOnlyItsPackageAndPreservesDeploymentAssets()
    {
        var installRoot = CreateTempRoot("plugin_ops_prune");
        var stateStore = new InstalledPluginStateStore(installRoot);
        var installer = new UnifiedPluginPackageInstaller(stateStore);
        foreach (var version in new[] { "1.0.0", "2.0.0" })
            Assert.Equal("Installed", installer.Install(_testData.CreateValidPackage("Audit", version), installRoot).Status);
        var archive = Path.Combine(installRoot, "persistence", "catalog", "retained.json");
        Directory.CreateDirectory(Path.GetDirectoryName(archive)!);
        File.WriteAllText(archive, "immutable validation asset");
        var result = PluginOpsProcess.Run("prune", ["--module-id", "Audit", "--version", "2.0.0", "--install-root", installRoot]);
        Assert.True(result.ExitCode == 0, result.StdOut + result.StdErr);
        Assert.False(Directory.Exists(Path.Combine(installRoot, "packages", "Audit", "2.0.0")));
        Assert.True(Directory.Exists(Path.Combine(installRoot, "packages", "Audit", "1.0.0")));
        Assert.Equal("immutable validation asset", File.ReadAllText(archive));
        Assert.True(stateStore.TryLoad("Audit", out var record, out _));
        Assert.Equal(["1.0.0"], record!.InstalledVersions);
        // 新正式入口拒绝裁剪被 builtin 选定的版本，且失败不改库存。
        var blocked = PluginOpsProcess.Run("prune", ["--module-id", "Audit", "--version", "1.0.0", "--install-root", installRoot]);
        Assert.NotEqual(0, blocked.ExitCode);
        Assert.Contains("PLUGIN_SELECTED_VERSION_CANNOT_BE_PRUNED", blocked.StdErr);
        Assert.True(Directory.Exists(Path.Combine(installRoot, "packages", "Audit", "1.0.0")));
    }

    [Fact]
    public void Enable_NewVersion_PrunesOldestPackage_ButKeepsCurrentEnabledAndRollbackVersions()
    {
        var installRoot = CreateTempRoot("plugin_ops_enable_cleanup");

        var stateStore = new InstalledPluginStateStore(installRoot);
        var installer = new UnifiedPluginPackageInstaller(stateStore);
        var operations = new UnifiedPluginPackageOperations(stateStore);
        var materializer = new RuntimeViewMaterializer(stateStore);

        Assert.Equal("Installed", installer.Install(_testData.CreateValidPackage("Audit", "1.0.0"), installRoot).Status);
        Assert.Equal("Installed", installer.Install(_testData.CreateValidPackage("Audit", "2.0.0"), installRoot).Status);
        Assert.Equal("Installed", installer.Install(_testData.CreateValidPackage("Audit", "3.0.0"), installRoot).Status);
        Assert.True(stateStore.TryLoad("Audit", out var record, out var loadError));
        Assert.Null(loadError);
        Assert.NotNull(record);
        record!.CurrentEnabledVersion = "2.0.0";
        record.IsDisabled = false;
        Assert.True(stateStore.TrySave(record, out var saveError));
        Assert.Null(saveError);

        var result = operations.Enable("Audit", "3.0.0");
        materializer.Materialize(installRoot);

        Assert.Equal("Enabled", result.Status);
        Assert.Equal("3.0.0", result.Version);
        Assert.True(stateStore.TryLoad("Audit", out var updatedRecord, out var updatedLoadError));
        Assert.Null(updatedLoadError);
        Assert.NotNull(updatedRecord);
        Assert.Equal("3.0.0", updatedRecord!.CurrentEnabledVersion);
        Assert.Equal(["2.0.0", "3.0.0"], updatedRecord.InstalledVersions);

        Assert.False(Directory.Exists(Path.Combine(installRoot, "packages", "Audit", "1.0.0")));
        Assert.True(Directory.Exists(Path.Combine(installRoot, "packages", "Audit", "2.0.0")));
        Assert.True(Directory.Exists(Path.Combine(installRoot, "packages", "Audit", "3.0.0")));
    }

    public void Dispose()
    {
        _testData.Dispose();

        foreach (var tempRoot in _tempRoots)
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    private string CreateTempRoot(string prefix)
    {
        var root = Path.Combine(Path.GetTempPath(), $"{prefix}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        _tempRoots.Add(root);
        return root;
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "tools", "PluginOps", "PluginOps.csproj")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Repository root not found.");
    }

    private sealed record PluginOpsResult(int ExitCode, string StdOut, string StdErr);

    private static string CopyHostDistribution(string installRoot)
    {
        var repoRoot = FindRepositoryRoot();
        var hostRoot = Path.Combine(installRoot, "host-distribution");
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        var hostSource = Path.Combine(repoRoot, "apps", "host-api", "bin", configuration, "net10.0");
        foreach (var file in Directory.EnumerateFiles(hostSource, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(hostRoot, Path.GetRelativePath(hostSource, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
        return hostRoot;
    }

    private static class PluginOpsProcess
    {
        public static PluginOpsResult Run(string command, IReadOnlyList<string> options, bool externalPluginsEnabled = false)
        {
            var repoRoot = FindRepositoryRoot();
            var projectPath = Path.Combine(repoRoot, "tools", "PluginOps", "PluginOps.csproj");
            EnsureBuilt(repoRoot, projectPath);
            var stdout = new StringBuilder();
            var stderr = new StringBuilder();

            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "dotnet",
                    WorkingDirectory = repoRoot,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };

            process.StartInfo.ArgumentList.Add("run");
            process.StartInfo.ArgumentList.Add("--project");
            process.StartInfo.ArgumentList.Add(projectPath);
            process.StartInfo.ArgumentList.Add("--no-build");
            process.StartInfo.ArgumentList.Add("--");
            process.StartInfo.ArgumentList.Add(command);
            var installRoot = options[options.ToList().IndexOf("--install-root") + 1];
            var hostRoot = CopyHostDistribution(installRoot);
            process.StartInfo.ArgumentList.Add("--host-path");
            process.StartInfo.ArgumentList.Add(Path.Combine(hostRoot, "HostApi.dll"));
            process.StartInfo.Environment["Platform__ExternalPluginsEnabled"] = externalPluginsEnabled ? "true" : "false";
            foreach (var option in options)
            {
                process.StartInfo.ArgumentList.Add(option);
            }

            process.StartInfo.Environment["DOTNET_NOLOGO"] = "1";

            process.OutputDataReceived += (_, args) =>
            {
                if (args.Data is not null)
                {
                    stdout.AppendLine(args.Data);
                }
            };

            process.ErrorDataReceived += (_, args) =>
            {
                if (args.Data is not null)
                {
                    stderr.AppendLine(args.Data);
                }
            };

            Assert.True(process.Start(), "Failed to start PluginOps.");
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            if (!process.WaitForExit(30000))
            {
                process.Kill(entireProcessTree: true);
                throw new TimeoutException("PluginOps child process timed out.");
            }
            process.WaitForExit();

            return new PluginOpsResult(process.ExitCode, stdout.ToString(), stderr.ToString());
        }

        private static void EnsureBuilt(string repoRoot, string projectPath)
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "dotnet",
                    WorkingDirectory = repoRoot,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };

            process.StartInfo.ArgumentList.Add("build");
            process.StartInfo.ArgumentList.Add(projectPath);
            process.StartInfo.ArgumentList.Add("-p:BuildProjectReferences=false");
            process.StartInfo.Environment["DOTNET_NOLOGO"] = "1";

            Assert.True(process.Start(), "Failed to start PluginOps build.");
            if (!process.WaitForExit(30000))
            {
                process.Kill(entireProcessTree: true);
                throw new TimeoutException("PluginOps child process timed out.");
            }
            process.WaitForExit();
            Assert.Equal(0, process.ExitCode);
        }
    }
}
