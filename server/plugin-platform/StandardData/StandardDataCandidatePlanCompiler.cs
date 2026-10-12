using System.Text.Json;
using System.Text.Json.Nodes;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Kernel.PluginDiscovery;
using ICP.Platform.Kernel.PluginPackages;
using ICP.Platform.Plugin.Contracts;
using ICP.Platform.PluginPlatform.Activation;
using ICP.Platform.PluginPlatform.Lifecycle;
using ICP.Platform.PluginPlatform.ComponentBundles;

namespace ICP.Platform.PluginPlatform.StandardData;

/// <summary>
/// 对 desired 中的 standard-data 包与内建 capsules 使用同一个 Canonical Module Plan 编译器。
/// </summary>
public sealed class StandardDataCandidatePlanCompiler : IPluginCandidatePlanCompiler
{
    private readonly TrustedBuiltInModuleSource? _builtInModuleSource;
    private readonly StandardDataPackageInspector _inspector;
    private readonly IReadOnlySet<string> _excludedBuiltInModuleIds;
    private readonly string? _installRoot;
    private readonly bool _externalPluginsEnabled;
    private readonly ModuleCapsuleCandidateReader _candidateReader = new();
    private readonly CanonicalModulePlanCompiler _compiler = new();

    public StandardDataCandidatePlanCompiler(
        StandardDataPackageInspector inspector)
        : this(builtInModuleSource: null, inspector, excludedBuiltInModuleIds: null)
    {
    }

    // Host 同时提供可信发行根、单一库存根与第三方消费开关，普通构造入口不授信。
    internal StandardDataCandidatePlanCompiler(
        TrustedBuiltInModuleSource? builtInModuleSource,
        StandardDataPackageInspector inspector,
        IEnumerable<string>? excludedBuiltInModuleIds = null, string? installRoot = null, bool externalPluginsEnabled = true)
    {
        _builtInModuleSource = builtInModuleSource;
        _inspector = inspector;
        _installRoot = installRoot;
        _externalPluginsEnabled = externalPluginsEnabled;
        _excludedBuiltInModuleIds = (excludedBuiltInModuleIds ?? [])
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    /// <inheritdoc/>
    public CanonicalModulePlan Compile(
        IReadOnlyList<InstalledPluginInventoryItem> inventory,
        ResolvedPluginLifecycleState state)
    {
        var candidates = new List<ModuleCapsuleCandidate>();
        var builtInCandidates = _builtInModuleSource is null ? [] : _candidateReader.ReadTrustedBuiltIn(_builtInModuleSource);
        foreach (var desired in state.DesiredModules
                     .OrderBy(static item => item.ModuleId, StringComparer.Ordinal))
        {
            var installed = inventory.SingleOrDefault(item =>
                string.Equals(item.ModuleId, desired.ModuleId, StringComparison.OrdinalIgnoreCase));
            if (installed is null && builtInCandidates.SingleOrDefault(candidate => candidate.ModuleId == desired.ModuleId && candidate.Version == desired.Version) is { } builtIn)
            {
                candidates.Add(builtIn with { Enabled = desired.Enabled });
                continue;
            }
            var packageRoot = installed?.FindPackageRoot(desired.Version);
            if (installed is null || string.IsNullOrWhiteSpace(packageRoot))
            {
                throw new PluginActivationException(
                    PlatformApiDiagnosticCodes.PackageSchemaUnsupported,
                    databaseAhead: false,
                    new InvalidDataException(
                        $"Desired plugin '{desired.ModuleId}@{desired.Version}' is not installed with a package root."));
            }

            var native = _installRoot is not null && _builtInModuleSource is not null
                ? NativeComponentInventory.ReadCandidate(_installRoot, desired.ModuleId, desired.Version, packageRoot, _builtInModuleSource)
                : null;
            if (native is not null)
            {
                candidates.Add(AssertDesiredIdentity(desired, native) with { Enabled = desired.Enabled });
                continue;
            }
            if (!_externalPluginsEnabled) continue;
            var profile = PluginPackageProfileReader.Read(packageRoot);
            if (profile is null)
            {
                var legacyCandidate = AssertDesiredIdentity(
                    desired,
                    _candidateReader.ReadInstalled(
                    [
                        new InstalledPluginRecord
                        {
                            ModuleId = installed.ModuleId,
                            Version = desired.Version,
                            PackageRoot = packageRoot,
                            CurrentEnabledVersion = installed.LegacyCurrentEnabledVersion,
                            IsDisabled = false,
                            InstalledVersions = installed.InstalledVersions.ToList()
                        }
                    ]).Single());
                candidates.Add(legacyCandidate with { Enabled = desired.Enabled });
                continue;
            }

            if (!string.Equals(profile, PlatformApiReleaseIdentity.StandardDataProfile, StringComparison.Ordinal))
            {
                throw new PluginActivationException(
                    PlatformApiDiagnosticCodes.PluginProfileUnsupported,
                    databaseAhead: false,
                    new InvalidDataException($"Plugin profile '{profile}' is not supported."));
            }

            var inspection = _inspector.Inspect(packageRoot);
            if (!inspection.IsSuccess)
            {
                throw new PluginActivationException(
                    inspection.Diagnostics[0].Code,
                    databaseAhead: false,
                    new InvalidDataException(inspection.Diagnostics[0].Message));
            }

            var manifest = inspection.Manifest!;
            if (!string.Equals(manifest.ModuleId, installed.ModuleId, StringComparison.Ordinal)
                || !string.Equals(manifest.Version, desired.Version, StringComparison.Ordinal))
            {
                throw new PluginActivationException(
                    PlatformApiDiagnosticCodes.PlatformApiAttestationMismatch,
                    databaseAhead: false,
                    new InvalidDataException(
                        $"Desired identity '{desired.ModuleId}@{desired.Version}' does not match package '{manifest.ModuleId}@{manifest.Version}'."));
            }

            candidates.Add(BuildCandidate(packageRoot, inspection) with { Enabled = desired.Enabled });
        }

        // 只有实际消费的选择遮蔽发行默认项；关闭普通外部后不能撤去同 ID 的基础成员。
        // 保留精确大小写匹配，大小写折叠冲突继续由 Canonical 编译器拒绝。
        var selectedCandidateModuleIds = candidates.Select(candidate => candidate.ModuleId).ToHashSet(StringComparer.Ordinal);
        candidates.AddRange(builtInCandidates
            .Where(candidate => !_excludedBuiltInModuleIds.Contains(candidate.ModuleId))
            .Where(candidate => !selectedCandidateModuleIds.Contains(candidate.ModuleId)));

        var compilation = _compiler.Compile("plugin-activation", candidates, _builtInModuleSource?.PlatformCompositionModuleId);
        if (!compilation.IsSuccess || compilation.Plan is null)
        {
            var first = compilation.Diagnostics[0];
            throw new PluginActivationException(
                first.Code,
                databaseAhead: false,
                new InvalidDataException(first.Message));
        }

        return compilation.Plan;
    }

    private static ModuleCapsuleCandidate AssertDesiredIdentity(
        PluginDesiredModuleState desired,
        ModuleCapsuleCandidate candidate)
    {
        if (!string.Equals(candidate.ModuleId, desired.ModuleId, StringComparison.Ordinal)
            || !string.Equals(candidate.Version, desired.Version, StringComparison.Ordinal))
        {
            throw new PluginActivationException(
                PlatformApiDiagnosticCodes.PlatformApiAttestationMismatch,
                databaseAhead: false,
                new InvalidDataException(
                    $"Desired identity '{desired.ModuleId}@{desired.Version}' does not match legacy package '{candidate.ModuleId}@{candidate.Version}'."));
        }

        return candidate;
    }

    // 候选先复用正式页面投影展开目录，后续 runtime 写入相同输入的结果。
    private static ModuleCapsuleCandidate BuildCandidate(
        string packageRoot,
        StandardDataPackageInspectionResult inspection)
    {
        var manifest = inspection.Manifest!;
        var projection = inspection.Projection!;
        var documents = StandardDataMetadataProjection.Project(manifest.ModuleId,
            JsonNode.Parse(File.ReadAllText(Path.Combine(packageRoot, manifest.Artifacts.Blueprint)))!.AsObject(),
            JsonNode.Parse(File.ReadAllText(Path.Combine(packageRoot, manifest.Artifacts.StandardProjection)))!.AsObject(),
            StandardDataMetadataProjection.ReadDocuments(Path.Combine(packageRoot, "metadata")));
        var features = StandardFeatureMetadataCompiler.Read(manifest.ModuleId,
            documents.Values.Select(document => JsonSerializer.SerializeToElement(document)));
        var frontendBindings = projection.Resources
            .Select(resource => $"{manifest.ModuleId}.{resource.ResourceKey}")
            .OrderBy(static key => key, StringComparer.Ordinal)
            .ToArray();
        var migrationBundles = manifest.Persistence.Migrations
            .OrderBy(static item => item.Key, StringComparer.Ordinal)
            .Select(static item => new ModuleMigrationBundleReference(
                NormalizeProvider(item.Key),
                item.Value.Assembly,
                item.Value.Manifest))
            .ToArray();

        return new ModuleCapsuleCandidate(
            manifest.ModuleId,
            manifest.Version,
            manifest.DisplayName,
            manifest.Description,
            Path.GetFullPath(packageRoot),
            BackendEntry: null,
            BackendBehaviorKey: null,
            FrontendEntry: manifest.Artifacts.StandardProjection,
            FrontendBehaviorKey: PlatformApiReleaseIdentity.StandardDataProfile,
            FrontendBindingMode: "standard-data",
            Dependencies: inspection.Blueprint!.Dependencies ?? [],
            Routes: projection.Resources
                .SelectMany(static resource => new[] { resource.ApiRoute, resource.PageRoute })
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static route => route, StringComparer.Ordinal)
                .ToArray(),
            FrontendBindings: frontendBindings,
            FrontendBindingReferences: frontendBindings,
            ModelKeys: documents.Values
                .Where(static document => document["type"]?.GetValue<string>() == "model")
                .SelectMany(static document => document.Select(property => property.Key)
                    .Where(static key => key is not ("schemaVersion" or "type")))
                .OrderBy(static key => key, StringComparer.Ordinal)
                .ToArray(),
            Provenance: new ModulePlanProvenance(
                "installed-package",
                Path.Combine(Path.GetFullPath(packageRoot), "plugin.json"),
                "/"),
            Persistence: new ModulePersistenceContribution(
                manifest.Persistence.Models,
                migrationBundles))
        {
            Features = features,
            CompositionMembers = inspection.Blueprint!.Composition?.Members ?? []
        };
    }

    private static string NormalizeProvider(string provider) =>
        provider.ToLowerInvariant() switch
        {
            "sqlserver" => "SqlServer",
            "oracle" => "Oracle",
            "mysql" => "MySql",
            _ => provider
        };
}

/// <summary>
/// 在候选计划编译、连接数据库或执行 DDL 前检查 desired 包。
/// </summary>
public sealed class StandardDataPackagePreflight : IPluginPackagePreflight
{
    private readonly StandardDataPackageInspector _inspector;
    private readonly UnifiedPluginPackageScanner _legacyScanner = new();
    private readonly HostVersionChecker? _hostVersionChecker;
    private readonly TrustedBuiltInModuleSource? _builtInModuleSource;
    private readonly string? _installRoot;
    private readonly bool _externalPluginsEnabled;

    // 普通包 preflight 不具有内建信任签发能力。
    public StandardDataPackagePreflight(
        StandardDataPackageInspector inspector,
        string? hostVersion = null)
        : this(inspector, hostVersion, builtInModuleSource: null)
    {
    }

    // Host 签发的内建发布物也可受 desired 控制；普通包路径不能提升为内建可信来源。
    internal StandardDataPackagePreflight(StandardDataPackageInspector inspector, string? hostVersion, TrustedBuiltInModuleSource? builtInModuleSource,
        string? installRoot = null, bool externalPluginsEnabled = true)
    {
        _inspector = inspector;
        _builtInModuleSource = builtInModuleSource;
        _installRoot = installRoot;
        _externalPluginsEnabled = externalPluginsEnabled;
        _hostVersionChecker = string.IsNullOrWhiteSpace(hostVersion)
            ? null
            : new HostVersionChecker(hostVersion);
    }

    /// <inheritdoc/>
    public void Validate(
        IReadOnlyList<InstalledPluginInventoryItem> inventory,
        ResolvedPluginLifecycleState state)
    {
        var builtIns = _builtInModuleSource is null ? [] : new ModuleCapsuleCandidateReader().ReadTrustedBuiltIn(_builtInModuleSource);
        foreach (var desired in state.DesiredModules.Where(static item => item.Enabled))
        {
            var installed = inventory.SingleOrDefault(item =>
                string.Equals(item.ModuleId, desired.ModuleId, StringComparison.OrdinalIgnoreCase));
            if (installed is null && builtIns.Any(candidate => candidate.ModuleId == desired.ModuleId && candidate.Version == desired.Version)) continue;
            var packageRoot = installed?.FindPackageRoot(desired.Version);
            if (installed is null || string.IsNullOrWhiteSpace(packageRoot))
            {
                throw new PluginActivationException(
                    PlatformApiDiagnosticCodes.PackageSchemaUnsupported,
                    databaseAhead: false,
                    new InvalidDataException(
                        $"Desired plugin '{desired.ModuleId}@{desired.Version}' is not installed with a package root."));
            }

            if (_installRoot is not null && _builtInModuleSource is not null
                && NativeComponentInventory.ReadCandidate(_installRoot, desired.ModuleId, desired.Version, packageRoot, _builtInModuleSource) is not null) continue;
            if (!_externalPluginsEnabled) continue;
            var profile = PluginPackageProfileReader.Read(packageRoot);
            if (profile is null)
            {
                ValidateLegacyPackage(desired, packageRoot);
                continue;
            }

            if (!string.Equals(profile, PlatformApiReleaseIdentity.StandardDataProfile, StringComparison.Ordinal))
            {
                throw new PluginActivationException(
                    PlatformApiDiagnosticCodes.PluginProfileUnsupported,
                    databaseAhead: false,
                    new InvalidDataException($"Plugin profile '{profile}' is not supported."));
            }

            var result = _inspector.Inspect(packageRoot);
            if (!result.IsSuccess)
            {
                throw new PluginActivationException(
                    result.Diagnostics[0].Code,
                    databaseAhead: false,
                    new InvalidDataException(result.Diagnostics[0].Message));
            }

            if (!string.Equals(result.Manifest!.ModuleId, desired.ModuleId, StringComparison.Ordinal)
                || !string.Equals(result.Manifest.Version, desired.Version, StringComparison.Ordinal))
            {
                throw new PluginActivationException(
                    PlatformApiDiagnosticCodes.PlatformApiAttestationMismatch,
                    databaseAhead: false,
                    new InvalidDataException(
                        $"Desired identity '{desired.ModuleId}@{desired.Version}' does not match the package manifest."));
            }
        }
    }

    private void ValidateLegacyPackage(PluginDesiredModuleState desired, string packageRoot)
    {
        var scan = _legacyScanner.ScanPackage(packageRoot);
        if (!scan.IsSuccess || scan.Manifest is null)
        {
            throw new PluginActivationException(
                PlatformApiDiagnosticCodes.PackageSchemaUnsupported,
                databaseAhead: false,
                new InvalidDataException(scan.ErrorMessage));
        }

        if (!string.Equals(scan.Manifest.ModuleId, desired.ModuleId, StringComparison.Ordinal)
            || !string.Equals(scan.Manifest.Version, desired.Version, StringComparison.Ordinal))
        {
            throw new PluginActivationException(
                PlatformApiDiagnosticCodes.PlatformApiAttestationMismatch,
                databaseAhead: false,
                new InvalidDataException(
                    $"Desired identity '{desired.ModuleId}@{desired.Version}' does not match the legacy package manifest."));
        }

        if (_hostVersionChecker is not null
            && !_hostVersionChecker.IsCompatible(new HostVersionRequirement
            {
                Min = scan.Manifest.HostVersion.Min,
                Max = scan.Manifest.HostVersion.Max
            }))
        {
            throw new PluginActivationException(
                PlatformApiDiagnosticCodes.PlatformApiIncompatible,
                databaseAhead: false,
                new InvalidDataException(
                    $"Legacy plugin '{desired.ModuleId}@{desired.Version}' is incompatible with the current Host version."));
        }
    }
}

internal static class PluginPackageProfileReader
{
    public static string? Read(string packageRoot)
    {
        var manifestPath = Path.Combine(packageRoot, "plugin.json");
        using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
        return document.RootElement.TryGetProperty("profile", out var profile)
            ? profile.GetString()
            : null;
    }
}
