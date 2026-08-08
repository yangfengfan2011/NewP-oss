using System.Text.Json;
using System.Text.Json.Serialization;

namespace ICP.Platform.Acceptance;

public enum AcceptanceGateKind
{
    Personnel,
    Plugin
}

public enum AcceptanceGateSelection
{
    Personnel,
    Plugin,
    All
}

public enum AcceptanceEventType
{
    RunStarted,
    ScenarioCompleted,
    EvidenceRecorded,
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
