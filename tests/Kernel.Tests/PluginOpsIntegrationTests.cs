using System.Diagnostics;
using System.Text;
using ICP.Platform.Kernel.PluginPackages;
using Xunit;

namespace ICP.Platform.Kernel.Tests;

public class PluginOpsIntegrationTests : IDisposable
{
    private readonly UnifiedPluginPackageTestData _testData = new();
    private readonly List<string> _tempRoots = new();

    [Fact]
    public void Install_WhenCurrentVersionAlreadyEnabled_RebuildsRuntimeProjection()
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
            ]);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Audit:1.0.0:Installed", result.StdOut, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("export default 'updated-audit';\n", File.ReadAllText(projectedEntryPath));
    }

    [Fact]
    public void Uninstall_WhenModuleIsInstalled_RemovesStateAndPackages()
    {
        var installRoot = CreateTempRoot("plugin_ops_uninstall");
        var packageRoot = _testData.CreateValidPackage("Audit", "1.0.0");

        var stateStore = new InstalledPluginStateStore(installRoot);
        var installer = new UnifiedPluginPackageInstaller(stateStore);

        Assert.Equal("Installed", installer.Install(packageRoot, installRoot).Status);

        var result = PluginOpsProcess.Run(
            "uninstall",
            [
                "--module-id", "Audit",
                "--install-root", installRoot
            ]);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Audit::Uninstalled", result.StdOut, StringComparison.OrdinalIgnoreCase);
        Assert.True(stateStore.TryLoad("Audit", out var record, out var loadError));
        Assert.Null(loadError);
        Assert.Null(record);
        Assert.False(Directory.Exists(Path.Combine(installRoot, "packages", "Audit")));
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

    private static class PluginOpsProcess
    {
        public static PluginOpsResult Run(string command, IReadOnlyList<string> options)
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
            process.WaitForExit();
            Assert.Equal(0, process.ExitCode);
        }
    }
}
