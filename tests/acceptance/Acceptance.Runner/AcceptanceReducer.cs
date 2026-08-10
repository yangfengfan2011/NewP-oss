namespace ICP.Platform.Acceptance;

public static class AcceptanceReducer
{
    public static AcceptanceReductionResult Reduce(
        AcceptanceGateSelection selection,
        IReadOnlyCollection<AcceptanceGateDefinition> definitions,
        IReadOnlyCollection<AcceptanceObservation> observations)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(observations);

        var gateResults = new Dictionary<string, AcceptanceGateResult>(StringComparer.Ordinal);

        foreach (var gate in GetRequestedGates(selection))
        {
            var definition = definitions.SingleOrDefault(candidate => candidate.Gate == gate);
            var gateResult = ReduceGate(gate, definition, observations.Where(item => item.Gate == gate));
            gateResults.Add(ToGateName(gate), gateResult);
        }

        var failed = gateResults.Values.FirstOrDefault(result => result.Verdict is AcceptanceVerdict.Failed);
        if (failed is not null)
        {
            return new AcceptanceReductionResult(AcceptanceVerdict.Failed, failed.FailureCode, gateResults);
        }

        var incomplete = gateResults.Values.FirstOrDefault(result => result.Verdict is AcceptanceVerdict.Incomplete);
        if (incomplete is not null)
        {
            return new AcceptanceReductionResult(AcceptanceVerdict.Incomplete, incomplete.FailureCode, gateResults);
        }

        return new AcceptanceReductionResult(AcceptanceVerdict.Passed, null, gateResults);
    }

    public static IReadOnlyList<AcceptanceGateKind> GetRequestedGates(AcceptanceGateSelection selection) =>
        selection switch
        {
            AcceptanceGateSelection.IdentityFoundation => [AcceptanceGateKind.IdentityFoundation],
            AcceptanceGateSelection.Personnel => [AcceptanceGateKind.Personnel],
            AcceptanceGateSelection.Plugin => [AcceptanceGateKind.Plugin],
            AcceptanceGateSelection.All => [AcceptanceGateKind.IdentityFoundation, AcceptanceGateKind.Personnel, AcceptanceGateKind.Plugin],
            _ => throw new ArgumentOutOfRangeException(nameof(selection), selection, "未知验收闸门。")
        };

    public static string ToGateName(AcceptanceGateKind gate) =>
        gate switch
        {
            AcceptanceGateKind.IdentityFoundation => "identity-foundation",
            AcceptanceGateKind.Personnel => "personnel",
            AcceptanceGateKind.Plugin => "plugin",
            _ => throw new ArgumentOutOfRangeException(nameof(gate), gate, "未知验收闸门。")
        };

    private static AcceptanceGateResult ReduceGate(
        AcceptanceGateKind gate,
        AcceptanceGateDefinition? definition,
        IEnumerable<AcceptanceObservation> gateObservations)
    {
        if (definition is null || !definition.IsImplemented)
        {
            return new AcceptanceGateResult(
                AcceptanceVerdict.Failed,
                AcceptanceFailureCodes.GateNotImplemented,
                new Dictionary<string, AcceptanceScenarioResult>(StringComparer.Ordinal),
                "not-started");
        }

        var ordered = gateObservations.OrderBy(item => item.Sequence).ToArray();
        var observerFailure = ordered.FirstOrDefault(item => item.Outcome is AcceptanceObservationOutcome.Failed);
        if (observerFailure is not null)
        {
            return new AcceptanceGateResult(
                AcceptanceVerdict.Failed,
                observerFailure.FailureCode ?? AcceptanceFailureCodes.ObserverFailed,
                new Dictionary<string, AcceptanceScenarioResult>(StringComparer.Ordinal),
                "failed");
        }

        var scenarios = new Dictionary<string, AcceptanceScenarioResult>(StringComparer.Ordinal);
        foreach (var scenario in definition.RequiredScenarios)
        {
            var completed = ordered.LastOrDefault(item =>
                item.Type is AcceptanceEventType.ScenarioCompleted &&
                string.Equals(item.Scenario, scenario, StringComparison.Ordinal));
            if (completed is null)
            {
                scenarios[scenario] = new AcceptanceScenarioResult(
                    "incomplete",
                    AcceptanceFailureCodes.RequiredEventMissing,
                    null);
                return Incomplete(AcceptanceFailureCodes.RequiredEventMissing, scenarios, ordered);
            }

            var evidence = ordered.LastOrDefault(item =>
                item.Type is AcceptanceEventType.EvidenceRecorded &&
                string.Equals(item.Scenario, scenario, StringComparison.Ordinal) &&
                !string.IsNullOrWhiteSpace(item.EvidenceRef));
            if (evidence is null)
            {
                scenarios[scenario] = new AcceptanceScenarioResult(
                    "incomplete",
                    AcceptanceFailureCodes.EvidenceIncomplete,
                    null);
                return Incomplete(AcceptanceFailureCodes.EvidenceIncomplete, scenarios, ordered);
            }

            scenarios[scenario] = new AcceptanceScenarioResult("passed", null, evidence.EvidenceRef);
        }

        var cleanup = ordered.LastOrDefault(item => item.Type is AcceptanceEventType.CleanupCompleted);
        if (cleanup is null)
        {
            return Incomplete(AcceptanceFailureCodes.CleanupIncomplete, scenarios, ordered);
        }

        foreach (var requiredType in new[]
                 {
                     AcceptanceEventType.RunStarted,
                     AcceptanceEventType.TreeVerified,
                     AcceptanceEventType.SecretScanCompleted,
                     AcceptanceEventType.OwnershipVerified
                 })
        {
            if (!ordered.Any(item => item.Type == requiredType))
            {
                return Incomplete(AcceptanceFailureCodes.RequiredEventMissing, scenarios, ordered);
            }
        }

        return new AcceptanceGateResult(AcceptanceVerdict.Passed, null, scenarios, "completed");
    }

    private static AcceptanceGateResult Incomplete(
        string failureCode,
        IReadOnlyDictionary<string, AcceptanceScenarioResult> scenarios,
        IReadOnlyCollection<AcceptanceObservation> observations) =>
        new(
            AcceptanceVerdict.Incomplete,
            failureCode,
            scenarios,
            observations.Any(item => item.Type is AcceptanceEventType.CleanupCompleted)
                ? "completed"
                : "incomplete");
}
