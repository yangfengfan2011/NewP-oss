using System.IO.Compression;
using ICP.Platform.HostApi.PluginOperations;
using ICP.Platform.Kernel.PluginPackages;
using ICP.Platform.Plugin.Contracts;
using ICP.Platform.PluginPlatform.Lifecycle;
using ICP.Platform.PluginPlatform.StandardData;
using Xunit;

namespace ICP.Platform.Kernel.Tests;

public sealed class PluginOperationsLifecycleTests : IDisposable
{
    private readonly UnifiedPluginPackageTestData _testData = new();
    private readonly string _installRoot = Path.Combine(
        Path.GetTempPath(),
        "plugin_lifecycle_svc_" + Guid.NewGuid().ToString("N"));
    private readonly InstalledPluginStateStore _stateStore;
    private readonly PluginUploadService _uploadService;
    private readonly PluginOperationsLifecycleService _service;
    private readonly PluginLifecycleManager _lifecycleManager;

    public PluginOperationsLifecycleTests()
    {
        Directory.CreateDirectory(_installRoot);
        _stateStore = new InstalledPluginStateStore(_installRoot);
        _uploadService = new PluginUploadService(_stateStore, _installRoot);
        _lifecycleManager = new PluginLifecycleManager(new PluginLifecycleStateStore(_installRoot, new PluginLifecycleTestDeployment()));
        _service = new PluginOperationsLifecycleService(
            _stateStore,
            new UnifiedPluginPackageInstaller(_stateStore),
            new StandardDataPackageInstaller(_installRoot, _stateStore, new StandardDataPackageInspector(
                new PlatformApiHostCatalog(PlatformApiReleaseIdentity.Version,
                    [PlatformApiReleaseIdentity.StandardDataProfile],
                    [new PlatformApiReleaseReference(PlatformApiReleaseIdentity.Version, PlatformApiReleaseIdentity.ReleaseDigest)]))),
            _lifecycleManager,
            _uploadService,
            _installRoot);
    }

    [Fact]
    public async Task Install_FromVerifiedUpload_AddsInventoryWithoutChangingActiveState()
    {
        var uploadId = await CreateVerifiedUploadAsync("Audit", "1.0.0");

        var result = _service.Install(uploadId);

        Assert.Equal("Installed", result.Status);
        Assert.True(_stateStore.TryLoad("Audit", out var record, out var loadError), loadError);
        Assert.NotNull(record);
        Assert.Null(record!.CurrentEnabledVersion);
        Assert.False(Directory.Exists(Path.Combine(_installRoot, "runtime")));
    }

    [Fact]
    public void Enable_UpdatesDesiredOnly_AndReturnsPendingRestart()
    {
        SeedInstalled("Audit", ["1.0.0"]);

        var result = _service.Enable("Audit", "1.0.0");

        Assert.Equal("PendingRestart", result.Status);
        Assert.Contains("重启", result.Message, StringComparison.Ordinal);
        Assert.True(_stateStore.TryLoad("Audit", out var record, out var loadError), loadError);
        Assert.Null(record!.CurrentEnabledVersion);
        var state = _lifecycleManager.Resolve(InstalledPluginInventoryAdapter.Create(_stateStore));
        var desired = Assert.Single(state.DesiredModules);
        Assert.True(desired.Enabled);
        Assert.Equal("1.0.0", desired.Version);
        Assert.Null(state.ActiveSnapshot);
    }

    [Fact]
    public void Disable_DoesNotRemoveCurrentActiveSnapshotUntilRestartCommit()
    {
        SeedInstalled("Audit", ["1.0.0"], legacyEnabledVersion: "1.0.0");

        var result = _service.Disable("Audit");

        Assert.Equal("PendingRestart", result.Status);
        var state = _lifecycleManager.Resolve(InstalledPluginInventoryAdapter.Create(_stateStore));
        Assert.False(Assert.Single(state.DesiredModules).Enabled);
        Assert.Equal("1.0.0", Assert.Single(state.ActiveSnapshot!.Modules).Version);
    }

    [Fact]
    public void Rollback_ChangesDesiredToLastKnownGoodWithoutChangingActive()
    {
        SeedInstalled("Audit", ["1.0.0", "2.0.0"], legacyEnabledVersion: "1.0.0");
        Assert.Equal("PendingRestart", _service.Enable("Audit", "2.0.0").Status);
        var inventory = InstalledPluginInventoryAdapter.Create(_stateStore);
        _lifecycleManager.CommitActive(
            inventory,
            Snapshot("snapshot-v2", new PluginActiveModule("Audit", "2.0.0", "installed-package")));

        var result = _service.Rollback("Audit");

        Assert.Equal("PendingRestart", result.Status);
        Assert.Equal("1.0.0", result.Version);
        var state = _lifecycleManager.Resolve(inventory);
        Assert.Equal("2.0.0", Assert.Single(state.ActiveSnapshot!.Modules).Version);
        Assert.Equal("1.0.0", Assert.Single(state.DesiredModules).Version);
    }

    [Fact]
    public void Uninstall_BlocksUntilDisabledSnapshotHasBeenCommitted()
    {
        SeedInstalled("Audit", ["1.0.0"], legacyEnabledVersion: "1.0.0");
        Assert.Equal("PendingRestart", _service.Disable("Audit").Status);

        var blocked = _service.Uninstall("Audit");

        Assert.Equal("Failed", blocked.Status);
        Assert.Contains("成功重启", blocked.Message, StringComparison.Ordinal);
        var inventory = InstalledPluginInventoryAdapter.Create(_stateStore);
        _lifecycleManager.CommitActive(inventory, Snapshot("snapshot-disabled"));

        var result = _service.Uninstall("Audit");

        Assert.Equal("Uninstalled", result.Status);
        Assert.True(_stateStore.TryLoad("Audit", out var record, out var loadError), loadError);
        Assert.Null(record);
    }

    [Fact]
    public void Commands_ReturnFailedForMissingModuleOrVersion()
    {
        SeedInstalled("Audit", ["1.0.0"]);

        Assert.Equal("Failed", _service.Enable("Missing", "1.0.0").Status);
        Assert.Equal("Failed", _service.Enable("Audit", "2.0.0").Status);
        Assert.Equal("Failed", _service.Disable("Missing").Status);
        Assert.Equal("Failed", _service.Rollback("Missing").Status);
    }

    public void Dispose()
    {
        _testData.Dispose();
        if (Directory.Exists(_installRoot))
        {
            Directory.Delete(_installRoot, recursive: true);
        }
    }

    private async Task<string> CreateVerifiedUploadAsync(string moduleId, string version)
    {
        var packageRoot = _testData.CreateValidPackage(moduleId, version);
        await using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var file in Directory.GetFiles(packageRoot, "*", SearchOption.AllDirectories))
            {
                var entry = archive.CreateEntry(Path.GetRelativePath(packageRoot, file));
                await using var entryStream = entry.Open();
                await using var fileStream = File.OpenRead(file);
                await fileStream.CopyToAsync(entryStream);
            }
        }

        stream.Position = 0;
        var result = await _uploadService.UploadAndVerifyAsync(stream, $"{moduleId}.zip");
        Assert.True(result.IsValid, string.Join(Environment.NewLine, result.Errors));
        return result.UploadId;
    }

    private void SeedInstalled(
        string moduleId,
        IReadOnlyList<string> versions,
        string? legacyEnabledVersion = null)
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

    private static PluginActiveSnapshot Snapshot(string id, params PluginActiveModule[] modules) =>
        new(id, "plan-" + id, "persistence-" + id, modules);
}
