using System.IO.Compression;
using System.Text.Json;
using ICP.Platform.Kernel.PluginPackages;
using ICP.Platform.Plugin.Contracts;
using ICP.Platform.PluginPlatform.StandardData;

namespace ICP.Platform.HostApi.PluginOperations;

/// <summary>
/// 插件上传验证服务：接收 zip 流，解压到受控的 uploads 目录，
/// 使用 UnifiedPluginPackageScanner 校验包结构，返回验证结果 DTO。
/// </summary>
public class PluginUploadService
{
    private readonly InstalledPluginStateStore _stateStore;
    private readonly string _installRoot;
    private readonly UnifiedPluginPackageScanner _scanner;
    private readonly StandardDataPackageInspector _standardDataInspector;

    public PluginUploadService(InstalledPluginStateStore stateStore, string installRoot)
        : this(
            stateStore,
            installRoot,
            new UnifiedPluginPackageScanner(),
            new StandardDataPackageInspector(CreateHostCatalog()))
    {
    }

    internal PluginUploadService(
        InstalledPluginStateStore stateStore,
        string installRoot,
        UnifiedPluginPackageScanner scanner)
        : this(stateStore, installRoot, scanner, new StandardDataPackageInspector(CreateHostCatalog()))
    {
    }

    internal PluginUploadService(
        InstalledPluginStateStore stateStore,
        string installRoot,
        UnifiedPluginPackageScanner scanner,
        StandardDataPackageInspector standardDataInspector)
    {
        _stateStore = stateStore;
        _installRoot = Path.GetFullPath(installRoot);
        _scanner = scanner;
        _standardDataInspector = standardDataInspector;
    }

    /// <summary>
    /// 接收 zip 流并执行验证：保存原始 zip → 解压 → 扫描 → 持久化记录 → 返回 DTO。
    /// zip 内容会解压到 &lt;install-root&gt;/uploads/&lt;uploadId&gt;/ 下，
    /// 原始 zip 保存为 package.zip，验证记录保存为 upload-record.json。
    /// </summary>
    public async Task<PluginUploadVerifyResultDto> UploadAndVerifyAsync(Stream zipStream, string fileName)
    {
        var uploadId = Guid.NewGuid().ToString("N");
        var uploadDir = Path.Combine(_installRoot, "uploads", uploadId);

        try
        {
            Directory.CreateDirectory(uploadDir);

            // 保存原始 zip 到 package.zip
            var packageZipPath = Path.Combine(uploadDir, "package.zip");
            using (var fileStream = new FileStream(packageZipPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, useAsync: true))
            {
                await zipStream.CopyToAsync(fileStream);
            }

            // 从已保存的 zip 文件中解压
            using var savedZipStream = new FileStream(packageZipPath, FileMode.Open, FileAccess.Read, FileShare.Read, 8192, useAsync: true);
            await ExtractZipToDirectoryAsync(savedZipStream, uploadDir);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or ArgumentException)
        {
            // 解压失败：清理临时目录
            CleanupDirectory(uploadDir);
            return new PluginUploadVerifyResultDto
            {
                UploadId = uploadId,
                IsValid = false,
                Errors = new[] { $"无法解析上传的 zip 文件：{ex.Message}" }
            };
        }

        if (IsStandardDataPackage(uploadDir))
        {
            var inspection = _standardDataInspector.Inspect(uploadDir);
            if (!inspection.IsSuccess)
            {
                var invalidResult = new PluginUploadVerifyResultDto
                {
                    UploadId = uploadId,
                    IsValid = false,
                    Errors = inspection.Diagnostics
                        .Select(static diagnostic => $"{diagnostic.Code}: {diagnostic.Message}")
                        .ToArray()
                };
                await PersistUploadRecordAsync(uploadDir, invalidResult);
                return invalidResult;
            }

            var standardManifest = inspection.Manifest!;
            var standardResult = new PluginUploadVerifyResultDto
            {
                UploadId = uploadId,
                ModuleId = standardManifest.ModuleId,
                DisplayName = standardManifest.DisplayName,
                Version = standardManifest.Version,
                IsValid = true,
                Errors = Array.Empty<string>()
            };
            await PersistUploadRecordAsync(uploadDir, standardResult);
            return standardResult;
        }

        // 非 standard-data 包继续使用旧扫描器完成一轮兼容读取。
        var scanResult = _scanner.ScanPackage(uploadDir);

        if (!scanResult.IsSuccess || scanResult.Manifest is null)
        {
            var errorResult = new PluginUploadVerifyResultDto
            {
                UploadId = uploadId,
                IsValid = false,
                Errors = new[] { scanResult.ErrorMessage }
            };
            // 验证失败时也持久化记录
            await PersistUploadRecordAsync(uploadDir, errorResult);
            return errorResult;
        }

        var result = new PluginUploadVerifyResultDto
        {
            UploadId = uploadId,
            ModuleId = scanResult.Manifest.ModuleId,
            DisplayName = scanResult.Manifest.DisplayName,
            Version = scanResult.Manifest.Version,
            IsValid = true,
            Errors = Array.Empty<string>()
        };

        // 持久化上传记录
        await PersistUploadRecordAsync(uploadDir, result);

        return result;
    }

    /// <summary>
    /// 根据 uploadId 查询已上传且验证过的包信息。
    /// 读取持久化的 upload-record.json，无需重新扫描。
    /// 如果 uploadId 不存在或已被清理，返回 null。
    /// </summary>
    public PluginUploadVerifyResultDto? GetUploadInfo(string uploadId)
    {
        if (string.IsNullOrWhiteSpace(uploadId))
        {
            return null;
        }

        var uploadDir = Path.Combine(_installRoot, "uploads", uploadId);
        var recordPath = Path.Combine(uploadDir, "upload-record.json");
        if (!File.Exists(recordPath))
        {
            return null;
        }

        var json = File.ReadAllText(recordPath);
        var record = JsonSerializer.Deserialize<PluginUploadVerifyResultDto>(json);
        return record;
    }

    /// <summary>
    /// 异步解压 zip 流到指定目录，同时防御 zip-slip 攻击。
    /// </summary>
    private static async Task ExtractZipToDirectoryAsync(Stream zipStream, string destinationDirectory)
    {
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);
        var normalizedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in archive.Entries)
        {
            var normalizedPath = entry.FullName
                .Replace('\\', '/')
                .TrimEnd('/');
            if (normalizedPath.Length > 0 && !normalizedPaths.Add(normalizedPath))
            {
                throw new InvalidDataException($"zip 文件中包含大小写折叠路径冲突：{entry.FullName}");
            }
        }

        foreach (var entry in archive.Entries)
        {
            // 跳过目录条目
            if (string.IsNullOrEmpty(entry.FullName))
            {
                continue;
            }

            var destinationPath = Path.GetFullPath(Path.Combine(destinationDirectory, entry.FullName));

            // 防御 zip-slip：解压路径必须在目标目录内
            if (!destinationPath.StartsWith(destinationDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(destinationPath, destinationDirectory, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"zip 文件中包含不安全的路径条目：{entry.FullName}");
            }

            // 如果是目录（以 / 或 \ 结尾），创建目录
            if (entry.FullName.EndsWith("/") || entry.FullName.EndsWith("\\"))
            {
                Directory.CreateDirectory(destinationPath);
                continue;
            }

            // 确保父目录存在
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);

            using var entryStream = entry.Open();
            using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, useAsync: true);
            await entryStream.CopyToAsync(fileStream);
        }
    }

    /// <summary>
    /// 将上传验证结果持久化为 upload-record.json，供后续 GetUploadInfo 读取。
    /// </summary>
    private static async Task PersistUploadRecordAsync(string uploadDir, PluginUploadVerifyResultDto result)
    {
        var recordPath = Path.Combine(uploadDir, "upload-record.json");
        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(recordPath, json);
    }

    private static void CleanupDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // 清理失败时不抛异常，让 GC 或定时任务后续处理
        }
    }

    private static bool IsStandardDataPackage(string packageRoot)
    {
        try
        {
            var manifestPath = Path.Combine(packageRoot, "plugin.json");
            using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
            return document.RootElement.TryGetProperty("profile", out var profile)
                && string.Equals(
                    profile.GetString(),
                    PlatformApiReleaseIdentity.StandardDataProfile,
                    StringComparison.Ordinal);
        }
        catch (Exception exception) when (exception is IOException or JsonException)
        {
            return false;
        }
    }

    private static PlatformApiHostCatalog CreateHostCatalog() =>
        new(
            PlatformApiReleaseIdentity.Version,
            [PlatformApiReleaseIdentity.StandardDataProfile],
            [new PlatformApiReleaseReference(
                PlatformApiReleaseIdentity.Version,
                PlatformApiReleaseIdentity.ReleaseDigest)]);
}
