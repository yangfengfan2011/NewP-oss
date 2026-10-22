using System.Reflection;
using System.Text.Json;
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
        Assert.True(record.IsDisabled);
    }

    [Fact]
    public void Enable_ClearsDisabledFlag()
    {
        var installRoot = CreateInstallRoot();
        var stateStore = new InstalledPluginStateStore(installRoot);
        var installer = new UnifiedPluginPackageInstaller(stateStore);
        var operations = new UnifiedPluginPackageOperations(stateStore);

        Assert.Equal("Installed", installer.Install(_testData.CreateValidPackage("Audit", "1.0.0"), installRoot).Status);
        Assert.Equal("Enabled", operations.Enable("Audit", "1.0.0").Status);
        Assert.Equal("Disabled", operations.Disable("Audit").Status);

        var result = operations.Enable("Audit", "1.0.0");

        Assert.Equal("Enabled", result.Status);
        Assert.True(stateStore.TryLoad("Audit", out var record, out var loadError));
        Assert.Null(loadError);
        Assert.NotNull(record);
        Assert.Equal("1.0.0", record!.CurrentEnabledVersion);
        Assert.False(record.IsDisabled);
    }

    [Fact]
    public void Uninstall_ReturnsUninstalled_WhenPluginIsInstalled()
    {
        var installRoot = CreateInstallRoot();
        var stateStore = new InstalledPluginStateStore(installRoot);
        var installer = new UnifiedPluginPackageInstaller(stateStore);
        var operations = new UnifiedPluginPackageOperations(stateStore);

        Assert.Equal("Installed", installer.Install(_testData.CreateValidPackage("Audit", "1.0.0"), installRoot).Status);

        var result = InvokeUninstall(operations, "Audit");

        Assert.Equal("Uninstalled", result.Status);
        Assert.Equal("Audit", result.ModuleId);
        Assert.Null(result.Version);
        Assert.True(stateStore.TryLoad("Audit", out var record, out var loadError));
        Assert.Null(loadError);
        Assert.Null(record);
        Assert.False(Directory.Exists(Path.Combine(installRoot, "packages", "Audit", "1.0.0")));
    }

    [Fact]
    public void Uninstall_ReturnsUninstalled_WhenPluginIsDisabled()
    {
        var installRoot = CreateInstallRoot();
        var stateStore = new InstalledPluginStateStore(installRoot);
        var installer = new UnifiedPluginPackageInstaller(stateStore);
        var operations = new UnifiedPluginPackageOperations(stateStore);

        Assert.Equal("Installed", installer.Install(_testData.CreateValidPackage("Audit", "1.0.0"), installRoot).Status);
        Assert.Equal("Enabled", operations.Enable("Audit", "1.0.0").Status);
        Assert.Equal("Disabled", operations.Disable("Audit").Status);

        var result = InvokeUninstall(operations, "Audit");

        Assert.Equal("Uninstalled", result.Status);
        Assert.Equal("Audit", result.ModuleId);
        Assert.Null(result.Version);
        Assert.True(stateStore.TryLoad("Audit", out var record, out var loadError));
        Assert.Null(loadError);
        Assert.Null(record);
        Assert.False(Directory.Exists(Path.Combine(installRoot, "packages", "Audit", "1.0.0")));
    }

    [Fact]
    public void Uninstall_ReturnsFailed_WhenPluginIsEnabled()
    {
        var installRoot = CreateInstallRoot();
        var stateStore = new InstalledPluginStateStore(installRoot);
        var installer = new UnifiedPluginPackageInstaller(stateStore);
        var operations = new UnifiedPluginPackageOperations(stateStore);

        Assert.Equal("Installed", installer.Install(_testData.CreateValidPackage("Audit", "1.0.0"), installRoot).Status);
        Assert.Equal("Enabled", operations.Enable("Audit", "1.0.0").Status);

        var result = InvokeUninstall(operations, "Audit");

        Assert.Equal("Failed", result.Status);
        Assert.Null(result.Version);
        Assert.Contains("enabled", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.True(stateStore.TryLoad("Audit", out var record, out var loadError));
        Assert.Null(loadError);
        Assert.NotNull(record);
        Assert.Equal("1.0.0", record!.CurrentEnabledVersion);
        Assert.False(record.IsDisabled);
        Assert.True(Directory.Exists(Path.Combine(installRoot, "packages", "Audit", "1.0.0")));
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
        Assert.Equal("Enabled", operations.Enable("Audit", "2.0.0").Status);
        Assert.Equal("Installed", installer.Install(_testData.CreateValidPackage("Audit", "3.0.0"), installRoot).Status);

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
    public void Enable_ReturnsFailed_WhenDependencyPluginIsDisabled()
    {
        var installRoot = CreateInstallRoot();
        var stateStore = new InstalledPluginStateStore(installRoot);
        var installer = new UnifiedPluginPackageInstaller(stateStore);
        var operations = new UnifiedPluginPackageOperations(stateStore);

        Assert.Equal("Installed", installer.Install(CreatePackageWithDependencies("Core", "1.0.0"), installRoot).Status);
        Assert.Equal("Installed", installer.Install(CreatePackageWithDependencies("Feature", "1.0.0", "Core"), installRoot).Status);

        var result = operations.Enable("Feature", "1.0.0");

        Assert.Equal("Failed", result.Status);
        Assert.Contains("Core", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("enabled", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.True(stateStore.TryLoad("Feature", out var record, out var loadError));
        Assert.Null(loadError);
        Assert.NotNull(record);
        Assert.Null(record!.CurrentEnabledVersion);
    }

    [Fact]
    public void Enable_ReturnsFailed_WhenDependencyPluginIsMissing()
    {
        var installRoot = CreateInstallRoot();
        var stateStore = new InstalledPluginStateStore(installRoot);
        var installer = new UnifiedPluginPackageInstaller(stateStore);
        var operations = new UnifiedPluginPackageOperations(stateStore);

        Assert.Equal("Installed", installer.Install(CreatePackageWithDependencies("Feature", "1.0.0", "MissingPlugin"), installRoot).Status);

        var result = operations.Enable("Feature", "1.0.0");

        Assert.Equal("Failed", result.Status);
        Assert.Contains("MissingPlugin", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not installed", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.True(stateStore.TryLoad("Feature", out var record, out var loadError));
        Assert.Null(loadError);
        Assert.NotNull(record);
        Assert.Null(record!.CurrentEnabledVersion);
    }

    [Fact]
    public void Enable_WhenThirdVersionIsEnabled_PrunesOldestVersionDirectory_AndKeepsEnabledAndRollbackVersions()
    {
        var installRoot = CreateInstallRoot();
        var stateStore = new InstalledPluginStateStore(installRoot);
        var installer = new UnifiedPluginPackageInstaller(stateStore);
        var operations = new UnifiedPluginPackageOperations(stateStore);

        Assert.Equal("Installed", installer.Install(_testData.CreateValidPackage("Audit", "1.0.0"), installRoot).Status);
        Assert.Equal("Installed", installer.Install(_testData.CreateValidPackage("Audit", "2.0.0"), installRoot).Status);
        Assert.Equal("Installed", installer.Install(_testData.CreateValidPackage("Audit", "3.0.0"), installRoot).Status);

        var result = operations.Enable("Audit", "3.0.0");

        Assert.Equal("Enabled", result.Status);
        Assert.Equal("Audit", result.ModuleId);
        Assert.Equal("3.0.0", result.Version);
        Assert.True(stateStore.TryLoad("Audit", out var record, out var loadError));
        Assert.Null(loadError);
        Assert.NotNull(record);
        Assert.Equal("3.0.0", record!.Version);
        Assert.Equal("3.0.0", record.CurrentEnabledVersion);
        Assert.Equal(Path.Combine(installRoot, "packages", "Audit", "3.0.0"), record.PackageRoot);
        Assert.Equal(["2.0.0", "3.0.0"], record.InstalledVersions);
        Assert.False(Directory.Exists(Path.Combine(installRoot, "packages", "Audit", "1.0.0")));
        Assert.True(Directory.Exists(Path.Combine(installRoot, "packages", "Audit", "2.0.0")));
        Assert.True(Directory.Exists(Path.Combine(installRoot, "packages", "Audit", "3.0.0")));
    }

    [Fact]
    public void Uninstall_ReturnsUninstalledStatus_WithWarningMessage_WhenOtherInstalledPluginDependsOnTarget()
    {
        var installRoot = CreateInstallRoot();
        var stateStore = new InstalledPluginStateStore(installRoot);
        var installer = new UnifiedPluginPackageInstaller(stateStore);
        var operations = new UnifiedPluginPackageOperations(stateStore);

        Assert.Equal("Installed", installer.Install(CreatePackageWithDependencies("Core", "1.0.0"), installRoot).Status);
        Assert.Equal("Installed", installer.Install(CreatePackageWithDependencies("Feature", "1.0.0", "Core"), installRoot).Status);

        var result = InvokeUninstall(operations, "Core");

        Assert.Equal("Uninstalled", result.Status);
        Assert.Contains("Feature", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Enable_PrunesVersionsOlderThanRollbackTarget_WhenEnablingLatestVersion()
    {
        var installRoot = CreateInstallRoot();
        var stateStore = new InstalledPluginStateStore(installRoot);
        var installer = new UnifiedPluginPackageInstaller(stateStore);
        var operations = new UnifiedPluginPackageOperations(stateStore);

        Assert.Equal("Installed", installer.Install(_testData.CreateValidPackage("Audit", "1.0.0"), installRoot).Status);
        Assert.Equal("Installed", installer.Install(_testData.CreateValidPackage("Audit", "2.0.0"), installRoot).Status);
        Assert.Equal("Installed", installer.Install(_testData.CreateValidPackage("Audit", "3.0.0"), installRoot).Status);

        var result = operations.Enable("Audit", "3.0.0");

        Assert.Equal("Enabled", result.Status);
        Assert.False(Directory.Exists(Path.Combine(installRoot, "packages", "Audit", "1.0.0")));
        Assert.True(Directory.Exists(Path.Combine(installRoot, "packages", "Audit", "2.0.0")));
        Assert.True(Directory.Exists(Path.Combine(installRoot, "packages", "Audit", "3.0.0")));
        Assert.True(stateStore.TryLoad("Audit", out var record, out var loadError));
        Assert.Null(loadError);
        Assert.NotNull(record);
        Assert.Equal("3.0.0", record!.CurrentEnabledVersion);
        Assert.Equal(["2.0.0", "3.0.0"], record.InstalledVersions);
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

    private static UnifiedPluginLifecycleResult InvokeUninstall(UnifiedPluginPackageOperations operations, string moduleId)
    {
        var method = typeof(UnifiedPluginPackageOperations).GetMethod(
            "Uninstall",
            BindingFlags.Instance | BindingFlags.Public,
            binder: null,
            types: [typeof(string)],
            modifiers: null);

        Assert.NotNull(method);

        return (UnifiedPluginLifecycleResult)method!.Invoke(operations, [moduleId])!;
    }

    private string CreateInstallRoot()
    {
        var installRoot = Path.Combine(Path.GetTempPath(), $"plugin_lifecycle_{Guid.NewGuid():N}");
        Directory.CreateDirectory(installRoot);
        _installRoots.Add(installRoot);
        return installRoot;
    }

    private string CreatePackageWithDependencies(string moduleId, string version, params string[] dependencies)
    {
        var packageRoot = _testData.CreateValidPackage(moduleId, version);
        var backendManifestPath = Path.Combine(packageRoot, "backend", "plugin.backend.json");
        var backendManifestJson = $$"""
        {
          "ModuleId": "{{moduleId}}",
          "DisplayName": "{{moduleId}}",
          "Version": "{{version}}",
          "Description": "Unified plugin package example",
          "EntryAssembly": "AuditModule.dll",
          "Dependencies": {{JsonSerializer.Serialize(dependencies)}},
          "HostVersion": {
            "Min": "1.0.0",
            "Max": "2.0.0"
          }
        }
        """;

        File.WriteAllText(backendManifestPath, backendManifestJson);
        return packageRoot;
    }
}
