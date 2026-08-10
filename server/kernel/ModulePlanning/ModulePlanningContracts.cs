using System.Security.Cryptography;
using System.Text.Json;

namespace ICP.Platform.Kernel.ModulePlanning;

/// <summary>
/// 模块胶囊进入统一编译器前的候选事实。
/// </summary>
public sealed record ModuleCapsuleCandidate(
    string ModuleId,
    string Version,
    string DisplayName,
    string? Description,
    string PackageRoot,
    string? BackendEntry,
    string? BackendBehaviorKey,
    string? FrontendEntry,
    string? FrontendBehaviorKey,
    string FrontendBindingMode,
    IReadOnlyList<string> Dependencies,
    IReadOnlyList<string> Routes,
    IReadOnlyList<string> FrontendBindings,
    IReadOnlyList<string> FrontendBindingReferences,
    IReadOnlyList<string> PermissionCodes,
    IReadOnlyList<string> PermissionReferences,
    IReadOnlyList<string> ModelKeys,
    ModulePlanProvenance Provenance,
    ModulePersistenceContribution? Persistence = null);

/// <summary>
/// 模块胶囊对持久化编译器公开的声明式工件引用。
/// </summary>
public sealed record ModulePersistenceContribution(
    IReadOnlyList<string> ModelPaths,
    IReadOnlyList<ModuleMigrationBundleReference> MigrationBundles);

/// <summary>
/// 一个 provider-specific migration bundle 的包内引用。
/// </summary>
public sealed record ModuleMigrationBundleReference(
    string Provider,
    string AssemblyPath,
    string ManifestPath);

/// <summary>
/// 运行时事实的 authoring 来源。
/// </summary>
public sealed class ModulePlanProvenance
{
    private readonly BuiltInTrust? _builtInTrust;

    /// <summary>
    /// 创建可公开描述的 authoring 来源。公开构造的来源不携带内建根信任。
    /// </summary>
    public ModulePlanProvenance(string SourceKind, string SourcePath, string JsonPointer)
        : this(SourceKind, SourcePath, JsonPointer, builtInTrust: null)
    {
    }

    private ModulePlanProvenance(
        string sourceKind,
        string sourcePath,
        string jsonPointer,
        BuiltInTrust? builtInTrust)
    {
        SourceKind = sourceKind;
        SourcePath = sourcePath;
        JsonPointer = jsonPointer;
        _builtInTrust = builtInTrust;
    }

    public string SourceKind { get; }

    public string SourcePath { get; }

    public string JsonPointer { get; }

    internal static ModulePlanProvenance CreateTrustedBuiltInRoot(
        string moduleId,
        string packageRoot,
        string sourcePath,
        string jsonPointer) =>
        new(
            "built-in",
            Path.GetFullPath(sourcePath),
            jsonPointer,
            new BuiltInTrust(
                moduleId,
                Path.GetFullPath(packageRoot),
                CanonicalFingerprint: null));

    internal ModulePlanProvenance BindToCanonicalCandidate(ModuleCapsuleCandidate candidate)
    {
        // root capability 只可消费一次，并绑定完整规范化模块事实；计划公开后即使复用
        // 同一个 provenance 实例修改外层 record，fingerprint 也不再匹配。
        if (_builtInTrust is not { CanonicalFingerprint: null } trust
            || !string.Equals(SourceKind, "built-in", StringComparison.Ordinal)
            || !string.Equals(trust.ModuleId, candidate.ModuleId, StringComparison.Ordinal)
            || !PathsEqual(trust.PackageRoot, candidate.PackageRoot))
        {
            return this;
        }

        return new ModulePlanProvenance(
            SourceKind,
            SourcePath,
            JsonPointer,
            trust with { CanonicalFingerprint = ComputeCanonicalFingerprint(candidate) });
    }

    internal bool AuthenticatesBuiltIn(CanonicalModulePlanModule module) =>
        _builtInTrust is { CanonicalFingerprint: not null } trust
        && string.Equals(SourceKind, "built-in", StringComparison.Ordinal)
        && string.Equals(trust.ModuleId, module.ModuleId, StringComparison.Ordinal)
        && PathsEqual(trust.PackageRoot, module.PackageRoot)
        && string.Equals(
            trust.CanonicalFingerprint,
            ComputeCanonicalFingerprint(module),
            StringComparison.Ordinal);

    private static string ComputeCanonicalFingerprint(ModuleCapsuleCandidate candidate) =>
        ComputeCanonicalFingerprint(
            candidate.ModuleId,
            candidate.Version,
            candidate.DisplayName,
            candidate.Description,
            candidate.PackageRoot,
            candidate.BackendEntry,
            candidate.BackendBehaviorKey,
            candidate.FrontendEntry,
            candidate.FrontendBehaviorKey,
            candidate.FrontendBindingMode,
            candidate.Dependencies,
            candidate.Routes,
            candidate.FrontendBindings,
            candidate.FrontendBindingReferences,
            candidate.PermissionCodes,
            candidate.PermissionReferences,
            candidate.ModelKeys,
            candidate.Persistence);

    private static string ComputeCanonicalFingerprint(CanonicalModulePlanModule module) =>
        ComputeCanonicalFingerprint(
            module.ModuleId,
            module.Version,
            module.DisplayName,
            module.Description,
            module.PackageRoot,
            module.BackendEntry,
            module.BackendBehaviorKey,
            module.FrontendEntry,
            module.FrontendBehaviorKey,
            module.FrontendBindingMode,
            module.Dependencies,
            module.Routes,
            module.FrontendBindings,
            module.FrontendBindingReferences,
            module.PermissionCodes,
            module.PermissionReferences,
            module.ModelKeys,
            module.Persistence);

    private static string ComputeCanonicalFingerprint(
        string moduleId,
        string version,
        string displayName,
        string? description,
        string packageRoot,
        string? backendEntry,
        string? backendBehaviorKey,
        string? frontendEntry,
        string? frontendBehaviorKey,
        string frontendBindingMode,
        IReadOnlyList<string> dependencies,
        IReadOnlyList<string> routes,
        IReadOnlyList<string> frontendBindings,
        IReadOnlyList<string> frontendBindingReferences,
        IReadOnlyList<string> permissionCodes,
        IReadOnlyList<string> permissionReferences,
        IReadOnlyList<string> modelKeys,
        ModulePersistenceContribution? persistence)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new
        {
            moduleId,
            version,
            displayName,
            description,
            packageRoot = Path.GetFullPath(packageRoot),
            backendEntry,
            backendBehaviorKey,
            frontendEntry,
            frontendBehaviorKey,
            frontendBindingMode,
            dependencies = Sort(dependencies),
            routes = Sort(routes),
            frontendBindings = Sort(frontendBindings),
            frontendBindingReferences = Sort(frontendBindingReferences),
            permissionCodes = Sort(permissionCodes),
            permissionReferences = Sort(permissionReferences),
            modelKeys = Sort(modelKeys),
            persistence = persistence is null
                ? null
                : new
                {
                    modelPaths = Sort(persistence.ModelPaths),
                    migrationBundles = persistence.MigrationBundles
                        .OrderBy(static bundle => bundle.Provider, StringComparer.Ordinal)
                        .ThenBy(static bundle => bundle.AssemblyPath, StringComparer.Ordinal)
                        .ThenBy(static bundle => bundle.ManifestPath, StringComparer.Ordinal)
                        .ToArray()
                }
        });
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }

    private static string[] Sort(IReadOnlyList<string> values) =>
        values.OrderBy(static value => value, StringComparer.Ordinal).ToArray();

    private static bool PathsEqual(string left, string right) =>
        string.Equals(
            Path.GetFullPath(left),
            Path.GetFullPath(right),
            StringComparison.OrdinalIgnoreCase);

    private sealed record BuiltInTrust(
        string ModuleId,
        string PackageRoot,
        string? CanonicalFingerprint);
}

/// <summary>
/// Canonical Module Plan 中单个模块的确定投影。
/// </summary>
public sealed record CanonicalModulePlanModule(
    string ModuleId,
    string Version,
    string DisplayName,
    string? Description,
    string PackageRoot,
    string? BackendEntry,
    string? BackendBehaviorKey,
    string? FrontendEntry,
    string? FrontendBehaviorKey,
    string FrontendBindingMode,
    IReadOnlyList<string> Dependencies,
    IReadOnlyList<string> Routes,
    IReadOnlyList<string> FrontendBindings,
    IReadOnlyList<string> FrontendBindingReferences,
    IReadOnlyList<string> PermissionCodes,
    IReadOnlyList<string> PermissionReferences,
    IReadOnlyList<string> ModelKeys,
    ModulePlanProvenance Provenance,
    ModulePersistenceContribution? Persistence = null);

/// <summary>
/// 编译成功后唯一可供 runtime materializer 消费的计划。
/// </summary>
public sealed record CanonicalModulePlan(
    string CandidateId,
    string CompilerContractVersion,
    string PlanHash,
    string SnapshotId,
    byte[] CanonicalBytes,
    IReadOnlyList<CanonicalModulePlanModule> Modules);

/// <summary>
/// 模块计划编译失败的稳定诊断。
/// </summary>
public sealed record ModulePlanDiagnostic(
    string CandidateId,
    string ModuleId,
    string Version,
    string CanonicalKey,
    string SourcePath,
    string JsonPointer,
    string Stage,
    string Code,
    string Message,
    string? ConflictingOwner = null);

/// <summary>
/// 编译结果。失败时不会返回部分计划。
/// </summary>
public sealed record ModulePlanCompilationResult(
    CanonicalModulePlan? Plan,
    IReadOnlyList<ModulePlanDiagnostic> Diagnostics)
{
    public bool IsSuccess => Plan is not null && Diagnostics.Count == 0;
}
