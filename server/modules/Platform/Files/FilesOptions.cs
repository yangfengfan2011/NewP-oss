using Microsoft.Extensions.Options;
using Microsoft.Extensions.Configuration;
using ICP.Platform.Modules.Files.Storage;

namespace ICP.Platform.Modules.Files;

/// <summary>Files 自有存储和工作量边界，业务格式规则由用途 owner 提供。</summary>
public sealed class FilesOptions
{
    public string StorageRoot { get; set; } = "";
    public long MaxUploadSizeBytes { get; set; } = 104857600;
    public int UploadLeaseHours { get; set; } = 24;
    public int CleanupIntervalMinutes { get; set; } = 5;
    public int CleanupBatchSize { get; set; } = 100;
    public int MaxReadBatchSize { get; set; } = 128;
}

/// <summary>禁止隐式发行物路径，启动时校验全部 Files 配置。</summary>
public sealed class FilesOptionsValidator : IValidateOptions<FilesOptions>
{
    private readonly TimeProvider _clock;
    private readonly IConfiguration? _configuration;

    /// <summary>以 Files 同一服务端 UTC 时钟核对到期时间的底层可表示性。</summary>
    public FilesOptionsValidator(TimeProvider? clock = null, IConfiguration? configuration = null)
    { _clock = clock ?? TimeProvider.System; _configuration = configuration; }

    /// <inheritdoc/>
    public ValidateOptionsResult Validate(string? name, FilesOptions options)
    {
        var failures = new List<string>();
        try
        {
            var root = LocalStorageProvider.ValidateRootPath(options.StorageRoot);
            if (HostPublicRoots().Any(publicRoot => IsWithin(root, publicRoot) || IsWithin(root, ResolvePhysicalDirectory(publicRoot))))
                failures.Add("Files:StorageRoot cannot overlap the Host public static content roots.");
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
        { failures.Add("Files:StorageRoot must be an explicit absolute private path without filesystem links."); }
        if (options.MaxUploadSizeBytes <= 0 || options.MaxUploadSizeBytes > long.MaxValue - 131072)
            failures.Add("Files:MaxUploadSizeBytes must be positive and leave room for the request overhead.");
        if (options.UploadLeaseHours <= 0) failures.Add("Files:UploadLeaseHours must be positive.");
        else
        {
            try { _clock.GetUtcNow().UtcDateTime.AddHours(options.UploadLeaseHours); }
            catch (ArgumentOutOfRangeException) { failures.Add("Files:UploadLeaseHours exceeds the DateTime range."); }
        }
        if (options.CleanupIntervalMinutes <= 0 || TimeSpan.FromMinutes(options.CleanupIntervalMinutes).TotalMilliseconds >= uint.MaxValue)
            failures.Add("Files:CleanupIntervalMinutes must fit the PeriodicTimer interval range.");
        if (options.CleanupBatchSize <= 0) failures.Add("Files:CleanupBatchSize must be positive.");
        if (options.MaxReadBatchSize is < 1 or > 1024) failures.Add("Files:MaxReadBatchSize must be between 1 and 1024.");
        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    // 只在配置边界读取当前 Host 的通用发行配置，Files 核心生命周期不消费这些路径。
    private IEnumerable<string> HostPublicRoots()
    {
        var configured = _configuration?["Platform:PluginInstallRoot"] ?? "plugins";
        var installRoot = Path.IsPathRooted(configured) ? configured : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, configured));
        yield return Path.GetFullPath(Path.Combine(installRoot, "runtime", "modules"));
        yield return Path.GetFullPath(Path.Combine(installRoot, "public"));
    }

    // 使用完整目录段比较，避免误拒含 public/wwwroot 字样的正常私有目录。
    private static bool IsWithin(string root, string publicRoot)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var normalized = publicRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return root.Equals(normalized, comparison) || root.StartsWith(normalized + Path.DirectorySeparatorChar, comparison);
    }

    // 公开根可能经自身或祖先链接指向私有位置，必须只读核对实际目标而不是字面目录。
    private static string ResolvePhysicalDirectory(string path)
    {
        var full = Path.GetFullPath(path);
        var volume = Path.GetPathRoot(full)!;
        var current = volume;
        foreach (var segment in full[volume.Length..].Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            var directory = new DirectoryInfo(current);
            if (directory.LinkTarget is not null)
                current = Path.GetFullPath((directory.ResolveLinkTarget(returnFinalTarget: true) ?? throw new IOException("Static content link target is unavailable.")).FullName);
        }
        return current;
    }
}
