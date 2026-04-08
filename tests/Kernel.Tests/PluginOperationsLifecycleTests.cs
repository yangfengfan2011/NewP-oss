using ICP.Platform.HostApi.PluginOperations;
using ICP.Platform.Kernel.PluginPackages;
using System.IO.Compression;
using System.Text.Json;
using Xunit;

namespace ICP.Platform.Kernel.Tests;

/// <summary>
/// 服务层生命周期操作测试：验证 PluginOperationsLifecycleService 的 install / enable / disable / rollback。
/// install 必须从已验证的 upload 区域读取，且不自动启用版本。
/// enable / disable / rollback 必须委托 UnifiedPluginPackageOperations 并刷新 RuntimeViewMaterializer。
/// </summary>
public class PluginOperationsLifecycleTests : IDisposable
{
    private readonly UnifiedPluginPackageTestData _testData = new();
    private readonly string _installRoot;
    private readonly InstalledPluginStateStore _stateStore;
    private readonly UnifiedPluginPackageInstaller _installer;
    private readonly UnifiedPluginPackageOperations _operations;
    private readonly PluginUploadService _uploadService;
    private readonly RuntimeViewMaterializer _materializer;
    private readonly PluginOperationsLifecycleService _service;

    public PluginOperationsLifecycleTests()
    {
        _installRoot = Path.Combine(Path.GetTempPath(), "plugin_lifecycle_svc_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_installRoot);
        _stateStore = new InstalledPluginStateStore(_installRoot);
        _installer = new UnifiedPluginPackageInstaller(_stateStore);
        _operations = new UnifiedPluginPackageOperations(_stateStore);
        _uploadService = new PluginUploadService(_stateStore, _installRoot);
        _materializer = new RuntimeViewMaterializer(_stateStore);
        _service = new PluginOperationsLifecycleService(
            _stateStore, _installer, _operations, _materializer, _uploadService, _installRoot);
    }

    // ── install 测试 ─────────────────────────────────────────────────────

    [Fact]
    public async Task Install_FromVerifiedUpload_CreatesPackageAndReturnsSuccess()
    {
        var uploadId = await CreateVerifiedUploadAsync("Audit", "1.0.0");

        var result = _service.Install(uploadId);

        Assert.Equal("Installed", result.Status);
        Assert.Equal("Audit", result.ModuleId);
        Assert.Equal("1.0.0", result.Version);

        // 包目录应已创建
        var packageDir = Path.Combine(_installRoot, "packages", "Audit", "1.0.0");
        Assert.True(Directory.Exists(packageDir));
    }

    [Fact]
    public void Install_WithInvalidUploadId_ReturnsFailed()
    {
        var result = _service.Install("nonexistent-upload-id");

        Assert.Equal("Failed", result.Status);
        Assert.Contains("找不到", result.Message);
    }

    [Fact]
    public void Install_WithInvalidUpload_ReturnsFailed()
    {
        // 创建一个无效的 upload 记录（直接写入）
        var badUploadId = "bad-upload-001";
        var uploadDir = Path.Combine(_installRoot, "uploads", badUploadId);
        Directory.CreateDirectory(uploadDir);
        File.WriteAllText(Path.Combine(uploadDir, "upload-record.json"), JsonSerializer.Serialize(
            new PluginUploadVerifyResultDto
            {
                UploadId = badUploadId,
                IsValid = false,
                Errors = new[] { "包结构校验失败" }
            }));

        var result = _service.Install(badUploadId);

        Assert.Equal("Failed", result.Status);
        Assert.Contains("未通过验证", result.Message);
    }

    [Fact]
    public async Task Install_DoesNotAutoEnable()
    {
        var uploadId = await CreateVerifiedUploadAsync("Audit", "1.0.0");

        var result = _service.Install(uploadId);

        Assert.Equal("Installed", result.Status);
        Assert.True(_stateStore.TryLoad("Audit", out var record, out _));
        Assert.NotNull(record);
        Assert.Null(record!.CurrentEnabledVersion);
    }

    // ── enable 测试 ──────────────────────────────────────────────────────

    [Fact]
    public void Enable_ReturnsEnabledStatus_AndRefreshesMaterializer()
    {
        InstallDirectly("Audit", "1.0.0");

        var result = _service.Enable("Audit", "1.0.0");

        Assert.Equal("Enabled", result.Status);
        Assert.Equal("Audit", result.ModuleId);
        Assert.Equal("1.0.0", result.Version);

        Assert.True(_stateStore.TryLoad("Audit", out var record, out _));
        Assert.Equal("1.0.0", record!.CurrentEnabledVersion);

        // runtime 目录应存在（Materializer 被调用过）
        Assert.True(Directory.Exists(Path.Combine(_installRoot, "runtime")));
    }

    [Fact]
    public void Enable_ReturnsFailed_WhenNotInstalled()
    {
        var result = _service.Enable("NonExistent", "1.0.0");

        Assert.Equal("Failed", result.Status);
    }

    // ── disable 测试 ─────────────────────────────────────────────────────

    [Fact]
    public void Disable_ReturnsDisabledStatus_AndRefreshesMaterializer()
    {
        InstallDirectly("Audit", "1.0.0");
        _operations.Enable("Audit", "1.0.0");

        var result = _service.Disable("Audit");

        Assert.Equal("Disabled", result.Status);
        Assert.Equal("Audit", result.ModuleId);

        Assert.True(_stateStore.TryLoad("Audit", out var record, out _));
        Assert.Null(record!.CurrentEnabledVersion);
    }

    [Fact]
    public void Disable_ReturnsFailed_WhenNotInstalled()
    {
        var result = _service.Disable("NonExistent");

        Assert.Equal("Failed", result.Status);
    }

    // ── rollback 测试 ────────────────────────────────────────────────────

    [Fact]
    public void Rollback_ReturnsEnabledWithPreviousVersion_AndRefreshesMaterializer()
    {
        InstallDirectly("Audit", "1.0.0");
        InstallDirectly("Audit", "2.0.0");
        _operations.Enable("Audit", "2.0.0");

        var result = _service.Rollback("Audit");

        Assert.Equal("Enabled", result.Status);
        Assert.Equal("Audit", result.ModuleId);
        Assert.Equal("1.0.0", result.Version);

        Assert.True(_stateStore.TryLoad("Audit", out var record, out _));
        Assert.Equal("1.0.0", record!.CurrentEnabledVersion);
    }

    [Fact]
    public void Rollback_ReturnsFailed_WhenNotInstalled()
    {
        var result = _service.Rollback("NonExistent");

        Assert.Equal("Failed", result.Status);
    }

    [Fact]
    public void Rollback_ReturnsFailed_WhenNoPreviousVersion()
    {
        InstallDirectly("Audit", "1.0.0");
        _operations.Enable("Audit", "1.0.0");

        var result = _service.Rollback("Audit");

        Assert.Equal("Failed", result.Status);
    }

    // ── 辅助方法 ─────────────────────────────────────────────────────────

    /// <summary>
    /// 从测试数据创建合法包 → 上传验证 → 返回 uploadId。
    /// </summary>
    private async Task<string> CreateVerifiedUploadAsync(string moduleId, string version)
    {
        var packageRoot = _testData.CreateValidPackage(moduleId, version);
        var zipPath = Path.Combine(Path.GetTempPath(), $"upload_{Guid.NewGuid():N}.zip");
        ZipFile.CreateFromDirectory(packageRoot, zipPath);

        await using var zipStream = File.OpenRead(zipPath);
        var verifyResult = await _uploadService.UploadAndVerifyAsync(zipStream, $"{moduleId}-{version}.zip");

        Assert.True(verifyResult.IsValid);
        return verifyResult.UploadId;
    }

    /// <summary>
    /// 直接安装包到 packages 目录（跳过 upload 流程），用于 enable/disable/rollback 测试的前置条件。
    /// </summary>
    private void InstallDirectly(string moduleId, string version)
    {
        var packageRoot = _testData.CreateValidPackage(moduleId, version);
        var installResult = _installer.Install(packageRoot, _installRoot);
        Assert.Equal("Installed", installResult.Status);
    }

    public void Dispose()
    {
        _testData.Dispose();
        if (Directory.Exists(_installRoot))
            Directory.Delete(_installRoot, recursive: true);
    }
}
