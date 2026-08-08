namespace ICP.Platform.Acceptance;

internal sealed class AcceptanceObservationRecorder(AcceptanceEventLog eventLog)
{
    private readonly List<AcceptanceObservation> _observations = [];
    private long _sequence;

    public IReadOnlyList<AcceptanceObservation> Observations => _observations;

    public async Task RecordAsync(
        AcceptanceGateKind gate,
        AcceptanceEventType type,
        AcceptanceObservationOutcome outcome = AcceptanceObservationOutcome.Succeeded,
        string? scenario = null,
        string? evidenceRef = null,
        string? failureCode = null,
        CancellationToken cancellationToken = default)
    {
        var observation = new AcceptanceObservation(
            SchemaVersion: "1.0",
            Sequence: ++_sequence,
            OccurredAtUtc: DateTimeOffset.UtcNow,
            Gate: gate,
            Type: type,
            Outcome: outcome,
            Scenario: scenario,
            EvidenceRef: evidenceRef,
            FailureCode: failureCode);
        _observations.Add(observation);
        await eventLog.AppendAsync(observation, cancellationToken);
    }
}
