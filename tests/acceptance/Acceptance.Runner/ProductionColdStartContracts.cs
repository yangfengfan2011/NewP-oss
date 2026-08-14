namespace ICP.Platform.Acceptance;

public enum ProductionColdStartMode
{
    Baseline,
    Candidate
}

public static class ProductionColdStartScenarios
{
    public const string AnonymousLogin = "anonymous-login";
    public const string AuthenticatedHome = "authenticated-home";
    public const string AuthenticatedPersonnel = "authenticated-personnel";
    public const string UnauthorizedPersonnel = "unauthorized-personnel";

    public static IReadOnlyList<string> All { get; } =
    [
        AnonymousLogin,
        AuthenticatedHome,
        AuthenticatedPersonnel,
        UnauthorizedPersonnel
    ];
}

public sealed record ProductionColdStartThrottleProfile(
    int DownloadBytesPerSecond,
    int UploadBytesPerSecond,
    int LatencyMs,
    int CpuSlowdownRate)
{
    public static ProductionColdStartThrottleProfile Required { get; } = new(
        DownloadBytesPerSecond: 1_250_000,
        UploadBytesPerSecond: 625_000,
        LatencyMs: 40,
        CpuSlowdownRate: 4);
}

public sealed record ProductionColdStartNavigationSample(
    double DomContentLoadedMs,
    double LoadMs,
    double ResponseEndMs);

public sealed record ProductionColdStartRequestSample(
    string Path,
    string ResourceType,
    int Status,
    long TransferBytes,
    double StartTimeMs,
    double DurationMs);

public sealed record ProductionColdStartLongTaskSample(
    double StartTimeMs,
    double DurationMs);

public sealed record ProductionColdStartSample(
    string Scenario,
    int Iteration,
    bool Skipped,
    string? SkipReason,
    bool CacheCleared,
    bool ServiceWorkersBlocked,
    int ViewportWidth,
    int ViewportHeight,
    ProductionColdStartThrottleProfile Throttle,
    long InteractiveMs,
    ProductionColdStartNavigationSample? Navigation,
    IReadOnlyList<ProductionColdStartRequestSample>? Requests,
    IReadOnlyList<ProductionColdStartLongTaskSample>? LongTasks,
    bool LongTaskObserverSucceeded,
    IReadOnlyList<string>? RequestedTargetCapabilityAssets);

public sealed record ProductionColdStartBudget(
    long MaxP50Ms,
    long MaxP95Ms,
    decimal? MinimumImprovementRatio = null);

public sealed record ProductionColdStartScenarioSummary(
    int SampleCount,
    long InteractiveP50Ms,
    long InteractiveP95Ms,
    long JsTransferP50Bytes,
    long JsTransferP95Bytes,
    long LongTaskP50Ms,
    long LongTaskP95Ms);

public sealed record ProductionColdStartViolation(
    string Code,
    string? Scenario,
    int? Iteration,
    string Diagnostic);

public sealed record ProductionColdStartReductionRequest(
    ProductionColdStartMode Mode,
    int ExpectedSamplesPerScenario,
    ProductionColdStartThrottleProfile ExpectedThrottle,
    IReadOnlyList<ProductionColdStartSample> Samples,
    IReadOnlyList<string> TargetCapabilityAssets,
    IReadOnlyDictionary<string, ProductionColdStartBudget> Budgets,
    IReadOnlyDictionary<string, ProductionColdStartScenarioSummary>? BaselineSummaries,
    AcceptanceCleanupResult Cleanup);

public sealed record ProductionColdStartReductionResult(
    string SchemaVersion,
    ProductionColdStartMode Mode,
    AcceptanceVerdict Verdict,
    string? FailureCode,
    IReadOnlyDictionary<string, ProductionColdStartScenarioSummary> Scenarios,
    IReadOnlyList<ProductionColdStartViolation> Violations,
    AcceptanceCleanupResult Cleanup);
