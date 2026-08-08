using ICP.Platform.HostApi.PluginOperations;
using ICP.Platform.Kernel.PluginPackages;
using ICP.Platform.PluginPlatform.Lifecycle;
using Xunit;

namespace ICP.Platform.Kernel.Tests;

public sealed class PluginOperationsAdminServiceTests : IDisposable
{
    private readonly string _installRoot = Path.Combine(
        Path.GetTempPath(),
        "plugin_admin_svc_" + Guid.NewGuid().ToString("N"));
    private readonly InstalledPluginStateStore _stateStore;
    private readonly PluginLifecycleManager _lifecycleManager;
    private readonly PluginOperationsAdminService _service;

    public PluginOperationsAdminServiceTests()
    {
        Directory.CreateDirectory(_installRoot);
        _stateStore = new InstalledPluginStateStore(_installRoot);
        _lifecycleManager = new PluginLifecycleManager(new PluginLifecycleStateStore(_installRoot));
        _service = new PluginOperationsAdminService(_stateStore, _lifecycleManager);
    }

    [Fact]
    public void GetAllModules_ReturnsEmpty_WhenNoModulesInstalled()
    {
        Assert.Empty(_service.GetAllModules());
    }

    [Fact]
    public void LegacyEnabledRecord_IsExposedAsDesiredActiveAndLastKnownGood()
    {
        Seed("Audit", ["1.0.0"], legacyEnabledVersion: "1.0.0");

        var status = Assert.Single(_service.GetAllModules());

        Assert.Equal("Enabled", status.Status);
        Assert.Equal("1.0.0", status.DesiredVersion);
        Assert.True(status.DesiredEnabled);
        Assert.Equal("1.0.0", status.ActiveVersion);
        Assert.Equal("1.0.0", status.LastKnownGoodVersion);
        Assert.Equal(status.ActiveVersion, status.CurrentEnabledVersion);
        Assert.False(status.RestartRequired);
        Assert.True(status.CanDisable);
        Assert.False(status.CanUninstall);
    }

    [Fact]
    public void EnableDesiredWithoutActive_IsPendingRestart()
    {
        Seed("Audit", ["1.0.0"]);
        _lifecycleManager.RequestEnable(
            InstalledPluginInventoryAdapter.Create(_stateStore),
            "Audit",
            "1.0.0");

        var status = _service.GetModule("Audit")!;

        Assert.Equal("PendingRestart", status.Status);
        Assert.True(status.DesiredEnabled);
        Assert.Null(status.ActiveVersion);
        Assert.True(status.RestartRequired);
    }

    [Fact]
    public void DisableDesired_KeepsCompatibilityFieldBoundToActiveUntilRestart()
    {
        Seed("Audit", ["1.0.0"], legacyEnabledVersion: "1.0.0");
        _lifecycleManager.RequestDisable(InstalledPluginInventoryAdapter.Create(_stateStore), "Audit");

        var status = _service.GetModule("Audit")!;

        Assert.Equal("PendingRestart", status.Status);
        Assert.False(status.DesiredEnabled);
        Assert.Equal("1.0.0", status.ActiveVersion);
        Assert.Equal("1.0.0", status.CurrentEnabledVersion);
        Assert.True(status.RestartRequired);
        Assert.False(status.CanUninstall);
    }

    [Fact]
    public void DisabledCommittedSnapshot_AllowsUninstallAndRetainsLastKnownGood()
    {
        Seed("Audit", ["1.0.0"], legacyEnabledVersion: "1.0.0");
        var inventory = InstalledPluginInventoryAdapter.Create(_stateStore);
        _lifecycleManager.RequestDisable(inventory, "Audit");
        _lifecycleManager.CommitActive(
            inventory,
            new PluginActiveSnapshot("disabled", "plan", "persistence", []));

        var status = _service.GetModule("Audit")!;

        Assert.Equal("Disabled", status.Status);
        Assert.Null(status.ActiveVersion);
        Assert.Equal("1.0.0", status.LastKnownGoodVersion);
        Assert.False(status.RestartRequired);
        Assert.True(status.CanUninstall);
        Assert.True(status.CanRollback);
    }

    [Fact]
    public void GetModule_ReturnsNull_WhenModuleDoesNotExist()
    {
        Assert.Null(_service.GetModule("Missing"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_installRoot))
        {
            Directory.Delete(_installRoot, recursive: true);
        }
    }

    private void Seed(string moduleId, IReadOnlyList<string> versions, string? legacyEnabledVersion = null)
    {
        foreach (var version in versions)
        {
            Directory.CreateDirectory(Path.Combine(_installRoot, "packages", moduleId, version));
        }

        Assert.True(_stateStore.TrySave(new InstalledPluginRecord
        {
            ModuleId = moduleId,
            Version = versions[^1],
            Status = InstalledPluginLifecycleState.Installed,
            PackageRoot = Path.Combine(_installRoot, "packages", moduleId, versions[^1]),
            CurrentEnabledVersion = legacyEnabledVersion,
            IsDisabled = legacyEnabledVersion is null,
            InstalledVersions = versions.ToList()
        }, out var saveError), saveError);
    }
}
