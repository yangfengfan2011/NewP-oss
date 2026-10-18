using System.Security.Cryptography;
using System.Text.Json;

namespace ICP.Platform.Kernel.ModulePlanning;

/// <summary>前端交付方式独立于可信来源；默认保留现有来源适配，原生安装资产明确采用 URL。</summary>
public enum ModuleFrontendDelivery { SourceDefault, Url }

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
    public ModuleFrontendDelivery FrontendDelivery { get; init; }
    public IReadOnlyList<FeatureDeclaration> Features { get; init; } = [];
    public IReadOnlyList<DataActionDeclaration> DataActions { get; init; } = [];
    public IReadOnlyList<string> CompositionMembers { get; init; } = [];
    public bool Enabled { get; init; } = true;
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

    // 信任指纹覆盖动作、组合声明与自身启用选择，避免保留旧身份却替换已验证事实。
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
            candidate.Persistence, candidate.Features, candidate.DataActions, candidate.CompositionMembers, candidate.Enabled, candidate.FrontendDelivery);

    // 信任指纹覆盖动作、组合声明与自身启用选择，避免保留旧身份却替换已验证事实。
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
            module.Persistence, module.Features, module.DataActions, module.CompositionMembers, module.Enabled, module.FrontendDelivery);

    // 信任指纹覆盖动作、组合声明与自身启用选择，避免保留旧身份却替换已验证事实。
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
        ModulePersistenceContribution? persistence, IReadOnlyList<FeatureDeclaration> features, IReadOnlyList<DataActionDeclaration> dataActions,
        IReadOnlyList<string> compositionMembers, bool enabled, ModuleFrontendDelivery frontendDelivery)
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
            frontendDelivery,
            dependencies = Sort(dependencies),
            routes = Sort(routes),
            frontendBindings = Sort(frontendBindings),
            frontendBindingReferences = Sort(frontendBindingReferences),
            modelKeys = Sort(modelKeys),
            compositionMembers = Sort(compositionMembers),
            enabled,
            dataActions = dataActions.ToArray(),
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
    public ModuleFrontendDelivery FrontendDelivery { get; init; }
    public IReadOnlyList<FeatureDeclaration> Features { get; init; } = [];
    public IReadOnlyList<DataActionDeclaration> DataActions { get; init; } = [];
    public IReadOnlyList<string> CompositionMembers { get; init; } = [];
    public bool Enabled { get; init; } = true;
}

/// <summary>选定版本的组件组合投影；父子关系不改变成员身份或持久化归属。</summary>
public sealed record ModuleCompositionNode(
    string ModuleId,
    string Version,
    bool Enabled,
    string? ParentModuleId,
    IReadOnlyList<string> Members);

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
    /// <summary>同一次候选选定的完整模块声明，包含自身选择停用的组件。</summary>
    public IReadOnlyList<CanonicalModulePlanModule> SelectedModules { get; init; } = Modules;
    public IReadOnlyList<ModuleCompositionNode> CompositionGraph { get; init; } = [];
    /// <summary>平台发布物明确指定的组合角色，与组件名称约定无关。</summary>
    public string? PlatformCompositionModuleId { get; init; }
    /// <summary>从平台组合及其完整成员后代一次派生的必需基础集合。</summary>
    public IReadOnlyList<string> RequiredModuleIds { get; init; } = [];
    /// <summary>由已验证声明一次派生的完整目录，所有运行时投影共享该结果。</summary>
    public IReadOnlyList<DataActionDeclaration> DataActionCatalog { get; } = DataActionCatalogCompiler.Compile(Modules.SelectMany(module => module.DataActions));
    public IReadOnlyList<ActionDataAccessRequirement> ActionDataAccessCatalog { get; } = ActionDataAccessCompiler.Compile(
        Modules.SelectMany(module => module.Features).ToArray(), Modules.SelectMany(module => module.DataActions).ToArray());
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
