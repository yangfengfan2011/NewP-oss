using ICP.Platform.HostApi.PluginOperations;
using ICP.Platform.Kernel.PluginPackages;
using Xunit;

namespace ICP.Platform.Kernel.Tests;

/// <summary>
/// Service-layer coverage for <see cref="PluginOperationsAdminService"/>.
/// Exercises list retrieval, single-module retrieval, missing-module behaviour,
/// and CanEnable / CanDisable / CanRollback semantics directly against the service
/// without going through the HTTP pipeline.
/// </summary>
public class PluginOperationsAdminServiceTests : IDisposable
{
    private readonly UnifiedPluginPackageTestData _testData = new();
    private readonly string _installRoot;
    private readonly InstalledPluginStateStore _stateStore;
    private readonly PluginOperationsAdminService _service;

    public PluginOperationsAdminServiceTests()
    {
        _installRoot = Path.Combine(Path.GetTempPath(), "plugin_admin_svc_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_installRoot);
        _stateStore = new InstalledPluginStateStore(_installRoot);
        _service = new PluginOperationsAdminService(_stateStore);
    }

    // ── Helpers ────────────────────────────────────────────────────────

    /// <summary>
    /// Installs a package, optionally enables a version, and returns the saved record.
    /// </summary>
    private InstalledPluginRecord InstallModule(
        string moduleId,
        string version = "1.0.0",
        string? enabledVersion = null,
        IEnumerable<string>? prependVersions = null,
        IEnumerable<string>? appendVersions = null)
    {
        var sourceRoot = _testData.CreateValidPackage(moduleId, version);
        var installer = new UnifiedPluginPackageInstaller(_stateStore);
        var result = installer.Install(sourceRoot, _installRoot);
        Assert.Equal("Installed", result.Status);

        Assert.True(_stateStore.TryLoad(moduleId, out var record, out var loadError), loadError);
        Assert.NotNull(record);

        if (prependVersions is not null)
        {
            var toPrepend = new List<string>();
            foreach (var v in prependVersions)
            {
                if (!record!.InstalledVersions.Contains(v, StringComparer.OrdinalIgnoreCase))
                    toPrepend.Add(v);
            }
            for (int i = toPrepend.Count - 1; i >= 0; i--)
                record!.InstalledVersions.Insert(0, toPrepend[i]);
        }

        if (appendVersions is not null)
        {
            foreach (var v in appendVersions)
            {
                if (!record!.InstalledVersions.Contains(v, StringComparer.OrdinalIgnoreCase))
                    record.InstalledVersions.Add(v);
            }
        }

        if (enabledVersion is not null)
        {
            record!.CurrentEnabledVersion = enabledVersion;
        }

        Assert.True(_stateStore.TrySave(record!, out var saveError), saveError);
        return record!;
    }

    // ── List retrieval ─────────────────────────────────────────────────

    [Fact]
    public void GetAllModules_ReturnsEmpty_WhenNoModulesInstalled()
    {
        var result = _service.GetAllModules();

        Assert.Empty(result);
    }

    [Fact]
    public void GetAllModules_ReturnsInstalledModules()
    {
        InstallModule("Audit", "1.0.0");
        InstallModule("Reports", "2.0.0");

        var result = _service.GetAllModules();

        Assert.Equal(2, result.Count);
        Assert.Contains(result, m => m.ModuleId == "Audit");
        Assert.Contains(result, m => m.ModuleId == "Reports");
    }

    [Fact]
    public void GetAllModules_MapsFieldsCorrectly()
    {
        InstallModule("Audit", "1.0.0", enabledVersion: "1.0.0");

        var dto = _service.GetAllModules().Single();

        Assert.Equal("Audit", dto.ModuleId);
        Assert.Equal("Audit", dto.DisplayName);
        Assert.Equal("Enabled", dto.Status);
        Assert.Equal("1.0.0", dto.CurrentEnabledVersion);
        Assert.Null(dto.LastOperation);
        Assert.Null(dto.LastError);
    }

    // ── Single-module retrieval ────────────────────────────────────────

    [Fact]
    public void GetModule_ReturnsNull_WhenModuleDoesNotExist()
    {
        var result = _service.GetModule("NonExistent");

        Assert.Null(result);
    }

    [Fact]
    public void GetModule_ReturnsDto_WhenModuleExists()
    {
        InstallModule("Audit", "1.0.0", enabledVersion: "1.0.0");

        var dto = _service.GetModule("Audit");

        Assert.NotNull(dto);
        Assert.Equal("Audit", dto!.ModuleId);
        Assert.Equal("Enabled", dto.Status);
    }

    [Fact]
    public void GetModule_ReturnsInstalledStatus_WhenNoVersionEnabled()
    {
        InstallModule("Audit", "1.0.0");

        var dto = _service.GetModule("Audit");

        Assert.NotNull(dto);
        Assert.Equal("Installed", dto!.Status);
        Assert.Null(dto.CurrentEnabledVersion);
    }

    [Fact]
    public void GetModule_ReturnsDisabledStatus_WhenModuleWasExplicitlyDisabled()
    {
        var record = InstallModule("Audit", "1.0.0");
        record.IsDisabled = true;

        Assert.True(_stateStore.TrySave(record, out var saveError), saveError);

        var dto = _service.GetModule("Audit");

        Assert.NotNull(dto);
        Assert.Equal("Disabled", dto!.Status);
        Assert.Null(dto.CurrentEnabledVersion);
        Assert.True(dto.CanEnable);
        Assert.False(dto.CanDisable);
    }

    [Fact]
    public void GetModule_ExposesCanUninstall_WhenModuleIsNotEnabled()
    {
        InstallModule("Audit", "1.0.0");

        var dto = _service.GetModule("Audit");

        Assert.NotNull(dto);
        var property = dto!.GetType().GetProperty("CanUninstall");
        Assert.NotNull(property);
        Assert.True(Assert.IsType<bool>(property!.GetValue(dto)));
    }

    [Fact]
    public void GetModule_CanUninstall_IsFalse_WhenModuleIsEnabled()
    {
        InstallModule("Audit", "1.0.0", enabledVersion: "1.0.0");

        var dto = _service.GetModule("Audit");

        Assert.NotNull(dto);
        var property = dto!.GetType().GetProperty("CanUninstall");
        Assert.NotNull(property);
        Assert.False(Assert.IsType<bool>(property!.GetValue(dto)));
    }

    // ── CanEnable semantics ────────────────────────────────────────────

    [Fact]
    public void CanEnable_IsTrue_WhenVersionsInstalled()
    {
        InstallModule("Audit", "1.0.0");

        var dto = _service.GetModule("Audit")!;

        Assert.True(dto.CanEnable);
    }

    [Fact]
    public void CanEnable_IsTrue_EvenWhenAlreadyEnabled()
    {
        InstallModule("Audit", "1.0.0", enabledVersion: "1.0.0");

        var dto = _service.GetModule("Audit")!;

        // CanEnable is based on having at least one installed version, not on current state.
        Assert.True(dto.CanEnable);
    }

    // ── CanDisable semantics ───────────────────────────────────────────

    [Fact]
    public void CanDisable_IsTrue_WhenVersionEnabled()
    {
        InstallModule("Audit", "1.0.0", enabledVersion: "1.0.0");

        var dto = _service.GetModule("Audit")!;

        Assert.True(dto.CanDisable);
    }

    [Fact]
    public void CanDisable_IsFalse_WhenNoVersionEnabled()
    {
        InstallModule("Audit", "1.0.0");

        var dto = _service.GetModule("Audit")!;

        Assert.False(dto.CanDisable);
    }

    // ── CanRollback semantics ──────────────────────────────────────────

    [Fact]
    public void CanRollback_IsFalse_WhenOnlyOneVersionInstalled()
    {
        InstallModule("Audit", "1.0.0", enabledVersion: "1.0.0");

        var dto = _service.GetModule("Audit")!;

        Assert.False(dto.CanRollback);
    }

    [Fact]
    public void CanRollback_IsFalse_WhenNotEnabled()
    {
        InstallModule("Audit", "1.0.0", prependVersions: new[] { "0.9.0" });

        var dto = _service.GetModule("Audit")!;

        Assert.False(dto.CanRollback);
    }

    [Fact]
    public void CanRollback_IsTrue_WhenEnabledVersionIsNotFirstInstalled()
    {
        // Install record with two versions, enabled on the second one.
        InstallModule("Audit", "1.0.0", enabledVersion: "1.0.0", prependVersions: new[] { "0.9.0" });

        var dto = _service.GetModule("Audit")!;

        Assert.True(dto.CanRollback);
    }

    [Fact]
    public void CanRollback_IsFalse_WhenEnabledVersionIsFirstInList()
    {
        // Enabled version is at index 0 — nothing to roll back to.
        InstallModule("Audit", "1.0.0", enabledVersion: "1.0.0", appendVersions: new[] { "2.0.0" });

        var dto = _service.GetModule("Audit")!;

        Assert.False(dto.CanRollback);
    }

    // ── InstalledVersions mapping ──────────────────────────────────────

    [Fact]
    public void InstalledVersions_AreDistinct_CaseInsensitive()
    {
        InstallModule("Audit", "1.0.0", enabledVersion: "1.0.0", appendVersions: new[] { "1.0.0" });

        var dto = _service.GetModule("Audit")!;

        // Even though "1.0.0" might appear twice in the raw record, the DTO should deduplicate.
        Assert.Single(dto.InstalledVersions);
    }

    // ── Dispose ────────────────────────────────────────────────────────

    public void Dispose()
    {
        _testData.Dispose();
        if (Directory.Exists(_installRoot))
            Directory.Delete(_installRoot, recursive: true);
    }
}
