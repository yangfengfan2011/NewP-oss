using System.IO.Compression;
using System.Text.Json;
using ICP.Platform.HostApi.PluginOperations;
using ICP.Platform.Kernel.PluginPackages;
using Xunit;

namespace ICP.Platform.Kernel.Tests;

/// <summary>
/// 上传验证流程测试：覆盖 PluginUploadService 的 zip 接收、解压、扫描与 DTO 映射。
/// 遵循 TDD：先写红测试，再实现最小生产代码。
/// </summary>
public class PluginOperationsUploadTests : IDisposable
{
    private readonly UnifiedPluginPackageTestData _testData = new();
    private readonly string _installRoot;
    private readonly InstalledPluginStateStore _stateStore;
    private readonly PluginUploadService _service;

    public PluginOperationsUploadTests()
    {
        _installRoot = Path.Combine(Path.GetTempPath(), "plugin_upload_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_installRoot);
        _stateStore = new InstalledPluginStateStore(_installRoot);
        _service = new PluginUploadService(_stateStore, _installRoot);
    }

    // ── 辅助方法 ──────────────────────────────────────────────────────────

    /// <summary>
    /// 将已有的测试目录打包成 zip 流。
    /// </summary>
    private MemoryStream CreateZipFromDirectory(string directoryPath)
    {
        var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var file in Directory.GetFiles(directoryPath, "*", SearchOption.AllDirectories))
            {
                var relativePath = Path.GetRelativePath(directoryPath, file);
                var entry = archive.CreateEntry(relativePath, CompressionLevel.Fastest);
                using var entryStream = entry.Open();
                using var fileStream = File.OpenRead(file);
                fileStream.CopyTo(entryStream);
            }
        }
        ms.Position = 0;
        return ms;
    }

    /// <summary>
    /// 创建一个无效的（非 zip 格式）流。
    /// </summary>
    private MemoryStream CreateInvalidZipStream()
    {
        var ms = new MemoryStream();
        var writer = new StreamWriter(ms);
        writer.Write("this is not a valid zip file");
        writer.Flush();
        ms.Position = 0;
        return ms;
    }

    // ── 有效 zip 上传 ─────────────────────────────────────────────────────

    [Fact]
    public async Task UploadValidZip_ReturnsIsValidTrue_WithManifestFields()
    {
        // Arrange
        var packageDir = _testData.CreateValidPackage("Audit", "1.0.0");
        var zipStream = CreateZipFromDirectory(packageDir);

        // Act
        var result = await _service.UploadAndVerifyAsync(zipStream, "audit-1.0.0.zip");

        // Assert
        Assert.True(result.IsValid);
        Assert.NotEmpty(result.UploadId);
        Assert.Equal("Audit", result.ModuleId);
        Assert.Equal("Audit", result.DisplayName);
        Assert.Equal("1.0.0", result.Version);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public async Task UploadValidZip_StoresExtractedPackageUnderUploadsDirectory()
    {
        // Arrange
        var packageDir = _testData.CreateValidPackage("Reports", "2.0.0");
        var zipStream = CreateZipFromDirectory(packageDir);

        // Act
        var result = await _service.UploadAndVerifyAsync(zipStream, "reports.zip");

        // Assert
        var uploadDir = Path.Combine(_installRoot, "uploads", result.UploadId);
        Assert.True(Directory.Exists(uploadDir), "上传目录应当存在");
        Assert.True(File.Exists(Path.Combine(uploadDir, "plugin.json")), "解压后应包含 plugin.json");
    }

    [Fact]
    public async Task UploadValidZip_GeneratesUniqueUploadId_EachTime()
    {
        // Arrange
        var packageDir = _testData.CreateValidPackage("Audit", "1.0.0");
        var zipStream1 = CreateZipFromDirectory(packageDir);
        var zipStream2 = CreateZipFromDirectory(packageDir);

        // Act
        var result1 = await _service.UploadAndVerifyAsync(zipStream1, "a.zip");
        var result2 = await _service.UploadAndVerifyAsync(zipStream2, "a.zip");

        // Assert
        Assert.NotEqual(result1.UploadId, result2.UploadId);
    }

    // ── 无效 zip 流 ───────────────────────────────────────────────────────

    [Fact]
    public async Task UploadInvalidZip_ReturnsIsValidFalse_WithErrors()
    {
        // Arrange
        var zipStream = CreateInvalidZipStream();

        // Act
        var result = await _service.UploadAndVerifyAsync(zipStream, "bad.zip");

        // Assert
        Assert.False(result.IsValid);
        Assert.NotEmpty(result.Errors);
        Assert.Empty(result.ModuleId);
        Assert.Empty(result.Version);
    }

    // ── 缺少 plugin.json 的 zip ───────────────────────────────────────────

    [Fact]
    public async Task UploadZipMissingPluginJson_ReturnsIsValidFalse_WithManifestError()
    {
        // Arrange
        var packageDir = _testData.CreatePackageMissingPluginJson();
        var zipStream = CreateZipFromDirectory(packageDir);

        // Act
        var result = await _service.UploadAndVerifyAsync(zipStream, "nomanifest.zip");

        // Assert
        Assert.False(result.IsValid);
        Assert.NotEmpty(result.Errors);
        Assert.Contains(result.Errors, e => e.Contains("plugin.json", StringComparison.OrdinalIgnoreCase));
    }

    // ── plugin.json 语法错误 ──────────────────────────────────────────────

    [Fact]
    public async Task UploadZipWithInvalidPluginJson_ReturnsIsValidFalse_WithParseError()
    {
        // Arrange
        var packageDir = _testData.CreatePackageWithInvalidPluginJson();
        var zipStream = CreateZipFromDirectory(packageDir);

        // Act
        var result = await _service.UploadAndVerifyAsync(zipStream, "badjson.zip");

        // Assert
        Assert.False(result.IsValid);
        Assert.NotEmpty(result.Errors);
    }

    // ── 空流 ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task UploadEmptyStream_ReturnsIsValidFalse()
    {
        // Arrange
        var emptyStream = new MemoryStream();

        // Act
        var result = await _service.UploadAndVerifyAsync(emptyStream, "empty.zip");

        // Assert
        Assert.False(result.IsValid);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public async Task UploadZipWithCaseInsensitiveDuplicatePath_IsRejectedBeforeExtraction()
    {
        using var zipStream = new MemoryStream();
        using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (var writer = new StreamWriter(archive.CreateEntry("plugin.json").Open()))
            {
                writer.Write("{}");
            }

            using (var writer = new StreamWriter(archive.CreateEntry("Plugin.json").Open()))
            {
                writer.Write("{}");
            }
        }
        zipStream.Position = 0;

        var result = await _service.UploadAndVerifyAsync(zipStream, "collision.zip");

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("大小写", StringComparison.Ordinal));
    }

    // ── 后端清单损坏 ──────────────────────────────────────────────────────

    [Fact]
    public async Task UploadZipWithCorruptBackendManifest_ReturnsIsValidFalse()
    {
        // Arrange
        var packageDir = _testData.CreatePackageWithInvalidBackendManifest();
        var zipStream = CreateZipFromDirectory(packageDir);

        // Act
        var result = await _service.UploadAndVerifyAsync(zipStream, "corrupt-backend.zip");

        // Assert
        Assert.False(result.IsValid);
        Assert.NotEmpty(result.Errors);
    }

    // ── zip 原始文件持久化 ────────────────────────────────────────────────

    [Fact]
    public async Task UploadValidZip_SavesOriginalZipAsPackageZip()
    {
        // Arrange
        var packageDir = _testData.CreateValidPackage("Audit", "1.0.0");
        var zipStream = CreateZipFromDirectory(packageDir);

        // Act
        var result = await _service.UploadAndVerifyAsync(zipStream, "audit.zip");

        // Assert：验证 package.zip 文件存在于上传目录
        var packageZipPath = Path.Combine(_installRoot, "uploads", result.UploadId, "package.zip");
        Assert.True(File.Exists(packageZipPath), "原始 zip 应保存为 package.zip");
    }

    [Fact]
    public async Task UploadInvalidZip_DoesNotSavePackageZip()
    {
        // Arrange
        var zipStream = CreateInvalidZipStream();

        // Act
        var result = await _service.UploadAndVerifyAsync(zipStream, "bad.zip");

        // Assert：验证失败时不保存 zip
        var packageZipPath = Path.Combine(_installRoot, "uploads", result.UploadId, "package.zip");
        Assert.False(File.Exists(packageZipPath), "无效 zip 不应保存 package.zip");
    }

    // ── 上传记录持久化 ───────────────────────────────────────────────────

    [Fact]
    public async Task UploadValidZip_PersistsUploadRecordAsJson()
    {
        // Arrange
        var packageDir = _testData.CreateValidPackage("Reports", "3.0.0");
        var zipStream = CreateZipFromDirectory(packageDir);

        // Act
        var result = await _service.UploadAndVerifyAsync(zipStream, "reports.zip");

        // Assert：upload-record.json 应存在于上传目录
        var recordPath = Path.Combine(_installRoot, "uploads", result.UploadId, "upload-record.json");
        Assert.True(File.Exists(recordPath), "上传记录 JSON 应持久化到 upload-record.json");
    }

    [Fact]
    public async Task GetUploadInfo_ReadsFromPersistedRecord_NotRescan()
    {
        // Arrange：上传后，删除解压内容但保留 record 文件和 zip，证明 GetUploadInfo 不依赖 re-scan
        var packageDir = _testData.CreateValidPackage("Audit", "1.0.0");
        var zipStream = CreateZipFromDirectory(packageDir);
        var result = await _service.UploadAndVerifyAsync(zipStream, "audit.zip");
        var uploadDir = Path.Combine(_installRoot, "uploads", result.UploadId);

        // 删除解压后的文件（plugin.json、backend/、frontend/ 等），但保留 upload-record.json 和 package.zip
        foreach (var file in Directory.GetFiles(uploadDir))
        {
            var name = Path.GetFileName(file);
            if (name is not "upload-record.json" and not "package.zip")
                File.Delete(file);
        }
        foreach (var dir in Directory.GetDirectories(uploadDir))
        {
            Directory.Delete(dir, recursive: true);
        }

        // Act
        var info = _service.GetUploadInfo(result.UploadId);

        // Assert：即使解压内容已删除，GetUploadInfo 仍能从持久化记录中读取
        Assert.NotNull(info);
        Assert.Equal("Audit", info!.ModuleId);
        Assert.Equal("1.0.0", info.Version);
        Assert.True(info.IsValid);
    }

    // ── GetUploadInfo 查询 ────────────────────────────────────────────────

    [Fact]
    public async Task GetUploadInfo_ReturnsResult_AfterUpload()
    {
        // Arrange
        var packageDir = _testData.CreateValidPackage("Audit", "1.0.0");
        var zipStream = CreateZipFromDirectory(packageDir);
        var uploaded = await _service.UploadAndVerifyAsync(zipStream, "audit.zip");

        // Act
        var info = _service.GetUploadInfo(uploaded.UploadId);

        // Assert
        Assert.NotNull(info);
        Assert.Equal(uploaded.UploadId, info!.UploadId);
        Assert.Equal("Audit", info.ModuleId);
        Assert.Equal("1.0.0", info.Version);
        Assert.True(info.IsValid);
    }

    [Fact]
    public void GetUploadInfo_ReturnsNull_WhenUploadIdNotExists()
    {
        // Act
        var info = _service.GetUploadInfo("non-existent-upload-id");

        // Assert
        Assert.Null(info);
    }

    // ── Dispose ───────────────────────────────────────────────────────────

    public void Dispose()
    {
        _testData.Dispose();
        if (Directory.Exists(_installRoot))
            Directory.Delete(_installRoot, recursive: true);
    }
}
