namespace ICP.Platform.Kernel.PluginDiscovery;

/// <summary>
/// 插件兼容性验证器
/// </summary>
public class PluginCompatibilityValidator
{
    private readonly HostVersionChecker _versionChecker;
    private readonly PluginDisablePolicy _disablePolicy;
    private readonly PluginHashValidator _hashValidator;

    public PluginCompatibilityValidator(string hostVersion, IEnumerable<string>? disabledPluginIds = null)
    {
        _versionChecker = new HostVersionChecker(hostVersion);
        _disablePolicy = new PluginDisablePolicy(disabledPluginIds);
        _hashValidator = new PluginHashValidator();
    }

    public ValidationResult Validate(PluginManifest manifest, string? entryAssemblyPath = null)
    {
        var errors = new List<string>();
        var errorType = PluginScanErrorType.None;

        // 检查插件是否被禁用
        if (_disablePolicy.IsDisabled(manifest.ModuleId))
        {
            errors.Add($"Plugin '{manifest.ModuleId}' is disabled by policy");
            errorType = PluginScanErrorType.PluginDisabled;
            return new ValidationResult
            {
                IsValid = false,
                Errors = errors,
                ErrorType = errorType
            };
        }

        // 验证主机版本兼容性
        if (manifest.HostVersion != null)
        {
            if (!_versionChecker.IsCompatible(manifest.HostVersion))
            {
                errors.Add($"Plugin requires host version {manifest.HostVersion.Min ?? "any"} to {manifest.HostVersion.Max ?? "any"}, but current host version is incompatible");
                errorType = PluginScanErrorType.IncompatibleHostVersion;
            }
        }

        // 验证入口程序集 SHA-256（如果清单中声明了）
        if (!string.IsNullOrWhiteSpace(manifest.EntryAssemblySha256) && !string.IsNullOrWhiteSpace(entryAssemblyPath))
        {
            if (!_hashValidator.ValidateHash(manifest.EntryAssemblySha256, entryAssemblyPath))
            {
                var actualHash = _hashValidator.ComputeSha256(entryAssemblyPath);
                errors.Add($"Entry assembly SHA-256 mismatch: expected '{manifest.EntryAssemblySha256}', but got '{actualHash}'");
                errorType = PluginScanErrorType.EntryAssemblyHashMismatch;
            }
        }

        return new ValidationResult
        {
            IsValid = errors.Count == 0,
            Errors = errors,
            ErrorType = errorType
        };
    }
}

/// <summary>
/// 验证结果
/// </summary>
public class ValidationResult
{
    public bool IsValid { get; set; }
    public IReadOnlyList<string> Errors { get; set; } = Array.Empty<string>();
    public PluginScanErrorType ErrorType { get; set; }
}
