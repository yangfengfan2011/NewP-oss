using System.Text.Json;
using System.Text.Json.Serialization;

namespace ICP.Platform.Acceptance;

/// <summary>
/// 三个模块闸门现在是普通 xUnit 测试，失败就是异常；这里只保留
/// production cold-start 仍在使用的裁决与清理契约。
/// </summary>
public enum AcceptanceVerdict
{
    Failed,
    Passed
}

public static class AcceptanceFailureCodes
{
    public const string CleanupIncomplete = "ACC_CLEANUP_INCOMPLETE";
    public const string CleanupFailed = "ACC_CLEANUP_FAILED";
    public const string OwnershipMismatch = "ACC_OWNERSHIP_MISMATCH";
    public const string TreeChanged = "ACC_TREE_CHANGED";
    public const string SecretDetected = "ACC_SECRET_DETECTED";
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
    public const string ProductionColdStartCapabilityRecoveryFailed = "ACC_PRODUCTION_COLD_START_CAPABILITY_RECOVERY_FAILED";
    public const string ProductionColdStartBuildFailed = "ACC_PRODUCTION_COLD_START_BUILD_FAILED";
    public const string ProductionColdStartBrowserFailed = "ACC_PRODUCTION_COLD_START_BROWSER_FAILED";
}

/// <summary>
/// 清理结论。<paramref name="Diagnostic"/> 携带脱敏后的失败原因，
/// 让调用方可以直接把它抛出去，而不必再去翻证据文件。
/// </summary>
public sealed record AcceptanceCleanupResult(string Status, string? FailureCode, string? Diagnostic = null);

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
