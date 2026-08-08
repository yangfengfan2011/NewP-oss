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
    ModulePlanProvenance Provenance);

/// <summary>
/// 运行时事实的 authoring 来源。
/// </summary>
public sealed record ModulePlanProvenance(
    string SourceKind,
    string SourcePath,
    string JsonPointer);

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
    ModulePlanProvenance Provenance);

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
