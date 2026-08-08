using ICP.Platform.Acceptance;
using Xunit;

namespace ICP.Platform.Acceptance.Tests;

public class AcceptanceReducerTests
{
    [Fact]
    public void Reduce_WhenRequestedGateIsNotImplemented_CannotBecomePass()
    {
        var definition = new AcceptanceGateDefinition(
            AcceptanceGateKind.Personnel,
            IsImplemented: false,
            RequiredScenarios: ["personnel.create"]);
        var events = CompleteEvents(AcceptanceGateKind.Personnel, "personnel.create");

        var result = AcceptanceReducer.Reduce(
            AcceptanceGateSelection.Personnel,
            [definition],
            events);

        Assert.Equal(AcceptanceVerdict.Failed, result.Verdict);
        Assert.Equal(AcceptanceFailureCodes.GateNotImplemented, result.FailureCode);
        Assert.Equal(AcceptanceFailureCodes.GateNotImplemented, result.Gates["personnel"].FailureCode);
    }

    [Fact]
    public void Reduce_WhenRequiredScenarioEventIsMissing_ReturnsIncomplete()
    {
        var definition = ImplementedPersonnelGate();

        var result = AcceptanceReducer.Reduce(
            AcceptanceGateSelection.Personnel,
            [definition],
            [Observation(1, AcceptanceEventType.RunStarted),
             Observation(2, AcceptanceEventType.CleanupCompleted)]);

        Assert.Equal(AcceptanceVerdict.Incomplete, result.Verdict);
        Assert.Equal(AcceptanceFailureCodes.RequiredEventMissing, result.FailureCode);
    }

    [Fact]
    public void Reduce_WhenEvidenceIsMissing_ReturnsIncomplete()
    {
        var definition = ImplementedPersonnelGate();

        var result = AcceptanceReducer.Reduce(
            AcceptanceGateSelection.Personnel,
            [definition],
            [Observation(1, AcceptanceEventType.RunStarted),
             Observation(2, AcceptanceEventType.ScenarioCompleted, "personnel.create"),
             Observation(3, AcceptanceEventType.CleanupCompleted)]);

        Assert.Equal(AcceptanceVerdict.Incomplete, result.Verdict);
        Assert.Equal(AcceptanceFailureCodes.EvidenceIncomplete, result.FailureCode);
    }

    [Fact]
    public void Reduce_WhenCleanupIsMissing_ReturnsIncomplete()
    {
        var definition = ImplementedPersonnelGate();

        var result = AcceptanceReducer.Reduce(
            AcceptanceGateSelection.Personnel,
            [definition],
            [Observation(1, AcceptanceEventType.RunStarted),
             Observation(2, AcceptanceEventType.ScenarioCompleted, "personnel.create"),
             Observation(3, AcceptanceEventType.EvidenceRecorded, "personnel.create", "personnel/api/create.json")]);

        Assert.Equal(AcceptanceVerdict.Incomplete, result.Verdict);
        Assert.Equal(AcceptanceFailureCodes.CleanupIncomplete, result.FailureCode);
    }

    [Fact]
    public void Reduce_WhenAllRequiredObservationsExist_ReturnsPass()
    {
        var definition = ImplementedPersonnelGate();

        var result = AcceptanceReducer.Reduce(
            AcceptanceGateSelection.Personnel,
            [definition],
            CompleteEvents(AcceptanceGateKind.Personnel, "personnel.create"));

        Assert.Equal(AcceptanceVerdict.Passed, result.Verdict);
        Assert.Null(result.FailureCode);
    }

    private static AcceptanceGateDefinition ImplementedPersonnelGate() =>
        new(AcceptanceGateKind.Personnel, IsImplemented: true, RequiredScenarios: ["personnel.create"]);

    private static AcceptanceObservation[] CompleteEvents(AcceptanceGateKind gate, string scenario) =>
    [
        Observation(1, AcceptanceEventType.RunStarted, gate: gate),
        Observation(2, AcceptanceEventType.ScenarioCompleted, scenario, gate: gate),
        Observation(3, AcceptanceEventType.EvidenceRecorded, scenario, "personnel/api/create.json", gate),
        Observation(4, AcceptanceEventType.CleanupCompleted, gate: gate)
    ];

    private static AcceptanceObservation Observation(
        long sequence,
        AcceptanceEventType type,
        string? scenario = null,
        string? evidenceRef = null,
        AcceptanceGateKind gate = AcceptanceGateKind.Personnel) =>
        new(
            SchemaVersion: "1.0",
            Sequence: sequence,
            OccurredAtUtc: DateTimeOffset.Parse("2026-08-08T00:00:00Z"),
            Gate: gate,
            Type: type,
            Outcome: AcceptanceObservationOutcome.Succeeded,
            Scenario: scenario,
            EvidenceRef: evidenceRef,
            FailureCode: null);
}
