using ICP.Platform.Kernel.PluginPackages;
using Xunit;

namespace ICP.Platform.Kernel.Tests;

public class UnifiedPluginPackageLifecycleOperationsTests : IDisposable
{
    private readonly UnifiedPluginPackageTestData _testData = new();
    private readonly List<string> _installRoots = new();

    [Fact]
    public void Enable_SetsRequestedInstalledVersionAsCurrentEnabledVersion()
    {
        var installRoot = CreateInstallRoot();
        var stateStore = new InstalledPluginStateStore(installRoot);
        var installer = new UnifiedPluginPackageInstaller(stateStore);
        var operations = new UnifiedPluginPackageOperations(stateStore);

        Assert.Equal("Installed", installer.Install(_testData.CreateValidPackage("Audit", "1.0.0"), installRoot).Status);
        Assert.Equal("Installed", installer.Install(_testData.CreateValidPackage("Audit", "2.0.0"), installRoot).Status);

        var result = operations.Enable("Audit", "1.0.0");

        Assert.Equal("Enabled", result.Status);
        Assert.Equal("Audit", result.ModuleId);
        Assert.Equal("1.0.0", result.Version);
        Assert.True(stateStore.TryLoad("Audit", out var record, out var loadError));
        Assert.Null(loadError);
        Assert.NotNull(record);
        Assert.Equal("2.0.0", record!.Version);
        Assert.Equal("1.0.0", record.CurrentEnabledVersion);
        Assert.Equal(["1.0.0", "2.0.0"], record.InstalledVersions);
    }

    [Fact]
    public void Disable_ClearsCurrentEnabledVersion()
    {
        var installRoot = CreateInstallRoot();
        var stateStore = new InstalledPluginStateStore(installRoot);
        var installer = new UnifiedPluginPackageInstaller(stateStore);
        var operations = new UnifiedPluginPackageOperations(stateStore);

        Assert.Equal("Installed", installer.Install(_testData.CreateValidPackage("Audit", "1.0.0"), installRoot).Status);
        Assert.Equal("Enabled", operations.Enable("Audit", "1.0.0").Status);

        var result = operations.Disable("Audit");

        Assert.Equal("Disabled", result.Status);
        Assert.Equal("Audit", result.ModuleId);
        Assert.True(stateStore.TryLoad("Audit", out var record, out var loadError));
        Assert.Null(loadError);
        Assert.NotNull(record);
        Assert.Null(record!.CurrentEnabledVersion);
    }

    [Fact]
    public void Rollback_SetsPreviousInstalledVersionAsCurrentEnabledVersion()
    {
        var installRoot = CreateInstallRoot();
        var stateStore = new InstalledPluginStateStore(installRoot);
        var installer = new UnifiedPluginPackageInstaller(stateStore);
        var operations = new UnifiedPluginPackageOperations(stateStore);

        Assert.Equal("Installed", installer.Install(_testData.CreateValidPackage("Audit", "1.0.0"), installRoot).Status);
        Assert.Equal("Installed", installer.Install(_testData.CreateValidPackage("Audit", "2.0.0"), installRoot).Status);
        Assert.Equal("Enabled", operations.Enable("Audit", "2.0.0").Status);

        var result = operations.Rollback("Audit");

        Assert.Equal("Enabled", result.Status);
        Assert.Equal("Audit", result.ModuleId);
        Assert.Equal("1.0.0", result.Version);
        Assert.True(stateStore.TryLoad("Audit", out var record, out var loadError));
        Assert.Null(loadError);
        Assert.NotNull(record);
        Assert.Equal("2.0.0", record!.Version);
        Assert.Equal("1.0.0", record.CurrentEnabledVersion);
    }

    [Fact]
    public void Rollback_WhenCurrentEnabledVersionDiffersFromLatestInstalledVersion_UsesEnabledVersionAsSource()
    {
        var installRoot = CreateInstallRoot();
        var stateStore = new InstalledPluginStateStore(installRoot);
        var installer = new UnifiedPluginPackageInstaller(stateStore);
        var operations = new UnifiedPluginPackageOperations(stateStore);

        Assert.Equal("Installed", installer.Install(_testData.CreateValidPackage("Audit", "1.0.0"), installRoot).Status);
        Assert.Equal("Installed", installer.Install(_testData.CreateValidPackage("Audit", "2.0.0"), installRoot).Status);
        Assert.Equal("Installed", installer.Install(_testData.CreateValidPackage("Audit", "3.0.0"), installRoot).Status);
        Assert.Equal("Enabled", operations.Enable("Audit", "2.0.0").Status);

        var result = operations.Rollback("Audit");

        Assert.Equal("Enabled", result.Status);
        Assert.Equal("1.0.0", result.Version);
        Assert.True(stateStore.TryLoad("Audit", out var record, out var loadError));
        Assert.Null(loadError);
        Assert.NotNull(record);
        Assert.Equal("3.0.0", record!.Version);
        Assert.Equal("1.0.0", record.CurrentEnabledVersion);
        Assert.Equal(["1.0.0", "2.0.0", "3.0.0"], record.InstalledVersions);
    }

    [Fact]
    public void Rollback_ReturnsFailed_WhenCurrentEnabledVersionIsEmpty()
    {
        var installRoot = CreateInstallRoot();
        var stateStore = new InstalledPluginStateStore(installRoot);
        var installer = new UnifiedPluginPackageInstaller(stateStore);
        var operations = new UnifiedPluginPackageOperations(stateStore);

        Assert.Equal("Installed", installer.Install(_testData.CreateValidPackage("Audit", "1.0.0"), installRoot).Status);
        Assert.Equal("Installed", installer.Install(_testData.CreateValidPackage("Audit", "2.0.0"), installRoot).Status);
        Assert.True(stateStore.TrySave(
            new InstalledPluginRecord
            {
                ModuleId = "Audit",
                Version = "2.0.0",
                Status = "Installed",
                PackageRoot = Path.GetFullPath(Path.Combine(installRoot, "packages", "Audit", "2.0.0")),
                CurrentEnabledVersion = string.Empty,
                InstalledVersions = ["1.0.0", "2.0.0"]
            },
            out var saveError));
        Assert.Null(saveError);

        var result = operations.Rollback("Audit");

        Assert.Equal("Failed", result.Status);
        Assert.Null(result.Version);
        Assert.Contains("disabled", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.True(stateStore.TryLoad("Audit", out var record, out var loadError));
        Assert.Null(loadError);
        Assert.NotNull(record);
        Assert.Equal("2.0.0", record!.Version);
        Assert.Equal(string.Empty, record.CurrentEnabledVersion);
        Assert.Equal(["1.0.0", "2.0.0"], record.InstalledVersions);
    }

    [Fact]
    public void Rollback_ReturnsFailed_WhenPluginIsDisabled()
    {
        var installRoot = CreateInstallRoot();
        var stateStore = new InstalledPluginStateStore(installRoot);
        var installer = new UnifiedPluginPackageInstaller(stateStore);
        var operations = new UnifiedPluginPackageOperations(stateStore);

        Assert.Equal("Installed", installer.Install(_testData.CreateValidPackage("Audit", "1.0.0"), installRoot).Status);
        Assert.Equal("Installed", installer.Install(_testData.CreateValidPackage("Audit", "2.0.0"), installRoot).Status);
        Assert.Equal("Enabled", operations.Enable("Audit", "2.0.0").Status);
        Assert.Equal("Disabled", operations.Disable("Audit").Status);

        var result = operations.Rollback("Audit");

        Assert.Equal("Failed", result.Status);
        Assert.Null(result.Version);
        Assert.Contains("disabled", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.True(stateStore.TryLoad("Audit", out var record, out var loadError));
        Assert.Null(loadError);
        Assert.NotNull(record);
        Assert.Equal("2.0.0", record!.Version);
        Assert.Null(record.CurrentEnabledVersion);
        Assert.Equal(["1.0.0", "2.0.0"], record.InstalledVersions);
    }

    [Fact]
    public void Enable_ReturnsFailed_WhenRequestedVersionIsNotInstalled()
    {
        var installRoot = CreateInstallRoot();
        var stateStore = new InstalledPluginStateStore(installRoot);
        var installer = new UnifiedPluginPackageInstaller(stateStore);
        var operations = new UnifiedPluginPackageOperations(stateStore);

        Assert.Equal("Installed", installer.Install(_testData.CreateValidPackage("Audit", "1.0.0"), installRoot).Status);

        var result = operations.Enable("Audit", "2.0.0");

        Assert.Equal("Failed", result.Status);
        Assert.Contains("not installed", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Rollback_ReturnsFailed_WhenNoPreviousInstalledVersionExists()
    {
        var installRoot = CreateInstallRoot();
        var stateStore = new InstalledPluginStateStore(installRoot);
        var installer = new UnifiedPluginPackageInstaller(stateStore);
        var operations = new UnifiedPluginPackageOperations(stateStore);

        Assert.Equal("Installed", installer.Install(_testData.CreateValidPackage("Audit", "1.0.0"), installRoot).Status);
        Assert.Equal("Enabled", operations.Enable("Audit", "1.0.0").Status);

        var result = operations.Rollback("Audit");

        Assert.Equal("Failed", result.Status);
        Assert.Contains("previous", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        _testData.Dispose();

        foreach (var installRoot in _installRoots)
        {
            if (Directory.Exists(installRoot))
            {
                Directory.Delete(installRoot, recursive: true);
            }
        }
    }

    private string CreateInstallRoot()
    {
        var installRoot = Path.Combine(Path.GetTempPath(), $"plugin_lifecycle_{Guid.NewGuid():N}");
        Directory.CreateDirectory(installRoot);
        _installRoots.Add(installRoot);
        return installRoot;
    }
}
