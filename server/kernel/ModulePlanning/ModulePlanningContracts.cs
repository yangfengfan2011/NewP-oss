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
    IReadOnlyList<string> ModelKeys,
    ModulePlanProvenance Provenance,
    ModulePersistenceContribution? Persistence = null)
{
    public IReadOnlyList<FeatureDeclaration> Features { get; init; } = [];
}

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
    private readonly BuiltInRootTrust? _builtInRootTrust;
    private readonly BoundBuiltInTrust? _boundBuiltInTrust;

    /// <summary>
    /// 创建可公开描述的 authoring 来源。公开构造的来源不携带内建根信任。
    /// </summary>
    public ModulePlanProvenance(string SourceKind, string SourcePath, string JsonPointer)
        : this(SourceKind, SourcePath, JsonPointer, builtInRootTrust: null, boundBuiltInTrust: null)
    {
    }

    private ModulePlanProvenance(
        string sourceKind,
        string sourcePath,
        string jsonPointer,
        BuiltInRootTrust? builtInRootTrust,
        BoundBuiltInTrust? boundBuiltInTrust)
    {
        SourceKind = sourceKind;
        SourcePath = sourcePath;
        JsonPointer = jsonPointer;
        _builtInRootTrust = builtInRootTrust;
        _boundBuiltInTrust = boundBuiltInTrust;
    }

    public string SourceKind { get; }

    public string SourcePath { get; }

    public string JsonPointer { get; }

    internal static ModulePlanProvenance CreateTrustedBuiltInRoot(
        string moduleId,
        string packageRoot,
        TrustedBuiltInModuleSource trustedSource,
        string sourcePath,
        string jsonPointer) =>
        CreateTrustedBuiltInRoot(
            moduleId,
            packageRoot,
            trustedSource,
            trustedSource.CaptureModuleSnapshot(packageRoot),
            sourcePath,
            jsonPointer);

    internal static ModulePlanProvenance CreateTrustedBuiltInRoot(
        string moduleId,
        string packageRoot,
        TrustedBuiltInModuleSource trustedSource,
        VerifiedModuleArtifactSnapshot snapshot,
        string sourcePath,
        string jsonPointer)
    {
        ArgumentNullException.ThrowIfNull(trustedSource);
        trustedSource.ValidateModuleTree(packageRoot);
        return new ModulePlanProvenance(
            "built-in",
            Path.GetFullPath(sourcePath),
            jsonPointer,
            new BuiltInRootTrust(
                moduleId,
                Path.GetFullPath(packageRoot),
                trustedSource,
                snapshot),
            boundBuiltInTrust: null);
    }

    internal ModulePlanProvenance BindToCanonicalCandidate(ModuleCapsuleCandidate candidate)
    {
        // root capability 只可消费一次，并绑定完整规范化模块事实；计划公开后即使复用
        // 同一个 provenance 实例修改外层 record，fingerprint 也不再匹配。
        if (_builtInRootTrust is not { } trust
            || !string.Equals(SourceKind, "built-in", StringComparison.Ordinal)
            || !string.Equals(trust.ModuleId, candidate.ModuleId, StringComparison.Ordinal)
            || !PathsEqual(trust.PackageRoot, candidate.PackageRoot))
        {
            return this;
        }

        var fingerprint = ComputeBoundFingerprint(
            ComputeCanonicalFingerprint(candidate),
            trust.Snapshot.ContentDigest);
        if (!trust.TryConsume())
        {
            return this;
        }

        return new ModulePlanProvenance(
            SourceKind,
            SourcePath,
            JsonPointer,
            builtInRootTrust: null,
            new BoundBuiltInTrust(
                trust.ModuleId,
                trust.PackageRoot,
                fingerprint,
                trust.Source,
                trust.Snapshot));
    }

    internal bool AuthenticatesBuiltIn(CanonicalModulePlanModule module) =>
        _boundBuiltInTrust is { } trust
        && string.Equals(SourceKind, "built-in", StringComparison.Ordinal)
        && string.Equals(trust.ModuleId, module.ModuleId, StringComparison.Ordinal)
        && PathsEqual(trust.PackageRoot, module.PackageRoot)
            && string.Equals(
                trust.CanonicalFingerprint,
                ComputeBoundFingerprint(
                    ComputeCanonicalFingerprint(module),
                    trust.Snapshot.ContentDigest),
                StringComparison.Ordinal);

    internal TrustedBuiltInModuleSource? GetAuthenticatedBuiltInSource(CanonicalModulePlanModule module) =>
        AuthenticatesBuiltIn(module) ? _boundBuiltInTrust?.Source : null;

    internal VerifiedModuleArtifactSnapshot? GetAuthenticatedBuiltInSnapshot(CanonicalModulePlanModule module) =>
        AuthenticatesBuiltIn(module) ? _boundBuiltInTrust?.Snapshot : null;

    internal string? GetAuthenticatedBuiltInArtifactDigest(CanonicalModulePlanModule module) =>
        GetAuthenticatedBuiltInSnapshot(module)?.ContentDigest;

    private static string ComputeBoundFingerprint(string canonicalFingerprint, string artifactDigest) =>
        Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        {
            canonicalFingerprint,
            artifactDigest
        })));

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
            candidate.ModelKeys,
            candidate.Persistence, candidate.Features);

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
            module.ModelKeys,
            module.Persistence, module.Features);

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
        IReadOnlyList<string> modelKeys,
        ModulePersistenceContribution? persistence, IReadOnlyList<FeatureDeclaration> features)
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
            modelKeys = Sort(modelKeys),
            features = features.OrderBy(node => node.Key, StringComparer.Ordinal).ToArray(),
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

    private sealed class BuiltInRootTrust(
        string moduleId,
        string packageRoot,
        TrustedBuiltInModuleSource source,
        VerifiedModuleArtifactSnapshot snapshot)
    {
        private int _consumed;

        internal string ModuleId { get; } = moduleId;

        internal string PackageRoot { get; } = packageRoot;

        internal TrustedBuiltInModuleSource Source { get; } = source;

        internal VerifiedModuleArtifactSnapshot Snapshot { get; } = snapshot;

        // 绑定是 capability 的线性化点；并发编译同一 root 时只能有一个胜者。
        internal bool TryConsume() => Interlocked.CompareExchange(ref _consumed, 1, 0) == 0;
    }

    private sealed record BoundBuiltInTrust(
        string ModuleId,
        string PackageRoot,
        string CanonicalFingerprint,
        TrustedBuiltInModuleSource Source,
        VerifiedModuleArtifactSnapshot Snapshot);
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
    IReadOnlyList<string> ModelKeys,
    ModulePlanProvenance Provenance,
    ModulePersistenceContribution? Persistence = null)
{
    public IReadOnlyList<FeatureDeclaration> Features { get; init; } = [];
}

/// <summary>
/// 编译成功后唯一可供 runtime materializer 消费的计划。
/// </summary>
public sealed record CanonicalModulePlan(
    string CandidateId,
    string CompilerContractVersion,
    string PlanHash,
    string SnapshotId,
    byte[] CanonicalBytes,
    IReadOnlyList<CanonicalModulePlanModule> Modules)
{
    /// <summary>由已验证声明一次派生的完整目录，所有运行时投影共享该结果。</summary>
    public IReadOnlyList<FeaturePoint> FeatureCatalog { get; } = FeatureCatalogCompiler.Compile(Modules.SelectMany(module => module.Features));
}

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
