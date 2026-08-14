using System.Text.Json;
using System.Text.Json.Serialization;

namespace ICP.Platform.Acceptance;

public enum AcceptanceGateKind
{
    IdentityFoundation,
    Personnel,
    Plugin
}

public enum AcceptanceGateSelection
{
    IdentityFoundation,
    Personnel,
    Plugin,
    All
}

public enum AcceptanceEventType
{
    RunStarted,
    ScenarioCompleted,
    EvidenceRecorded,
    TreeVerified,
    SecretScanCompleted,
    OwnershipVerified,
    CleanupCompleted,
    GateNotImplemented
}

public enum AcceptanceObservationOutcome
{
    Succeeded,
    Failed
}

public enum AcceptanceVerdict
{
    Incomplete,
    Failed,
    Passed
}

public static class AcceptanceFailureCodes
{
    public const string GateNotImplemented = "ACC_GATE_NOT_IMPLEMENTED";
    public const string RequiredEventMissing = "ACC_REQUIRED_EVENT_MISSING";
    public const string EvidenceIncomplete = "ACC_EVIDENCE_INCOMPLETE";
    public const string CleanupIncomplete = "ACC_CLEANUP_INCOMPLETE";
    public const string ObserverFailed = "ACC_OBSERVER_FAILED";
    public const string OwnershipMismatch = "ACC_OWNERSHIP_MISMATCH";
    public const string TreeChanged = "ACC_TREE_CHANGED";
    public const string SecretDetected = "ACC_SECRET_DETECTED";
    public const string CleanupFailed = "ACC_CLEANUP_FAILED";
    public const string EvidenceUnreadable = "ACC_EVIDENCE_UNREADABLE";
    public const string PersonnelPreflightFailed = "ACC_PERSONNEL_PREFLIGHT_FAILED";
    public const string PersonnelApiFailed = "ACC_PERSONNEL_API_FAILED";
    public const string PersonnelBrowserFailed = "ACC_PERSONNEL_BROWSER_FAILED";
    public const string PersonnelRestartFailed = "ACC_PERSONNEL_RESTART_FAILED";
    public const string IdentityFoundationPreflightFailed = "ACC_IDENTITY_FOUNDATION_PREFLIGHT_FAILED";
    public const string IdentityFoundationApiFailed = "ACC_IDENTITY_FOUNDATION_API_FAILED";
    public const string IdentityFoundationBrowserFailed = "ACC_IDENTITY_FOUNDATION_BROWSER_FAILED";
    public const string IdentityFoundationRestartFailed = "ACC_IDENTITY_FOUNDATION_RESTART_FAILED";
    public const string IdentityFoundationSecurityFailed = "ACC_IDENTITY_FOUNDATION_SECURITY_FAILED";
    public const string IdentityFoundationPersistenceFailed = "ACC_IDENTITY_FOUNDATION_PERSISTENCE_FAILED";
    public const string ProductionColdStartSampleCount = "ACC_PRODUCTION_COLD_START_SAMPLE_COUNT";
    public const string ProductionColdStartScenarioSkipped = "ACC_PRODUCTION_COLD_START_SCENARIO_SKIPPED";
    public const string ProductionColdStartCacheNotCleared = "ACC_PRODUCTION_COLD_START_CACHE_NOT_CLEARED";
    public const string ProductionColdStartThrottleMismatch = "ACC_PRODUCTION_COLD_START_THROTTLE_MISMATCH";
    public const string ProductionColdStartRequestEvidenceMissing = "ACC_PRODUCTION_COLD_START_REQUEST_EVIDENCE_MISSING";
    public const string ProductionColdStartLongTaskEvidenceMissing = "ACC_PRODUCTION_COLD_START_LONG_TASK_EVIDENCE_MISSING";
    public const string ProductionColdStartServiceWorkerEnabled = "ACC_PRODUCTION_COLD_START_SERVICE_WORKER_ENABLED";
    public const string ProductionColdStartViewportMismatch = "ACC_PRODUCTION_COLD_START_VIEWPORT_MISMATCH";
    public const string ProductionColdStartBudgetExceeded = "ACC_PRODUCTION_COLD_START_BUDGET_EXCEEDED";
    public const string ProductionColdStartBaselineMissing = "ACC_PRODUCTION_COLD_START_BASELINE_MISSING";
    public const string ProductionColdStartTargetCapabilityRequested = "ACC_PRODUCTION_COLD_START_TARGET_CAPABILITY_REQUESTED";
    public const string ProductionColdStartBuildFailed = "ACC_PRODUCTION_COLD_START_BUILD_FAILED";
    public const string ProductionColdStartBrowserFailed = "ACC_PRODUCTION_COLD_START_BROWSER_FAILED";
}

public sealed record AcceptanceGateDefinition(
    AcceptanceGateKind Gate,
    bool IsImplemented,
    IReadOnlyList<string> RequiredScenarios);

public sealed record AcceptanceObservation(
    string SchemaVersion,
    long Sequence,
    DateTimeOffset OccurredAtUtc,
    AcceptanceGateKind Gate,
    AcceptanceEventType Type,
    AcceptanceObservationOutcome Outcome,
    string? Scenario,
    string? EvidenceRef,
    string? FailureCode);

public sealed record AcceptanceScenarioResult(
    string Status,
    string? FailureCode,
    string? EvidenceRef);

public sealed record AcceptanceGateResult(
    AcceptanceVerdict Verdict,
    string? FailureCode,
    IReadOnlyDictionary<string, AcceptanceScenarioResult> Scenarios,
    string CleanupStatus);

public sealed record AcceptanceReductionResult(
    AcceptanceVerdict Verdict,
    string? FailureCode,
    IReadOnlyDictionary<string, AcceptanceGateResult> Gates);

public sealed record AcceptanceCleanupResult(string Status, string? FailureCode);

public sealed record AcceptanceRunResult(
    string SchemaVersion,
    string RunId,
    IReadOnlyList<string> RequestedGates,
    string Commit,
    string TreeHash,
    string? PlatformApiRelease,
    string? PlatformApiDigest,
    IReadOnlyDictionary<string, string> ToolVersions,
    string? DatabaseFingerprint,
    IReadOnlyList<string> ProcessIdentities,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    AcceptanceVerdict Verdict,
    string? FailureCode,
    IReadOnlyDictionary<string, AcceptanceGateResult> Gates,
    IReadOnlyList<string> ArtifactRefs,
    AcceptanceCleanupResult Cleanup);

public sealed record AcceptanceRunOutcome(int ExitCode, string ResultPath);

public static class AcceptanceJson
{
    public static JsonSerializerOptions Options { get; } = CreateOptions();
    public static JsonSerializerOptions CompactOptions { get; } = CreateOptions(writeIndented: false);

    private static JsonSerializerOptions CreateOptions(bool writeIndented = true)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = writeIndented
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}
