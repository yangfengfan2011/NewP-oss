using System.Text.Json;
using ICP.Platform.Acceptance;
using Xunit;

namespace ICP.Platform.Acceptance.Tests;

public class AcceptanceEventLogTests
{
    [Fact]
    public async Task AppendAsync_PreservesExistingJsonLinesAndAppendsNextEvent()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"newp-acceptance-events-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "events.jsonl");

        try
        {
            var store = new AcceptanceEventLog(path);
            await store.AppendAsync(Observation(1, AcceptanceEventType.RunStarted));
            var firstWrite = await File.ReadAllTextAsync(path);

            await store.AppendAsync(Observation(2, AcceptanceEventType.GateNotImplemented));

            var finalText = await File.ReadAllTextAsync(path);
            var lines = await File.ReadAllLinesAsync(path);

            Assert.StartsWith(firstWrite, finalText, StringComparison.Ordinal);
            Assert.Equal(2, lines.Length);
            Assert.Equal(1, JsonDocument.Parse(lines[0]).RootElement.GetProperty("sequence").GetInt64());
            Assert.Equal(2, JsonDocument.Parse(lines[1]).RootElement.GetProperty("sequence").GetInt64());
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static AcceptanceObservation Observation(long sequence, AcceptanceEventType type) =>
        new(
            SchemaVersion: "1.0",
            Sequence: sequence,
            OccurredAtUtc: DateTimeOffset.Parse("2026-08-08T00:00:00Z"),
            Gate: AcceptanceGateKind.Personnel,
            Type: type,
            Outcome: AcceptanceObservationOutcome.Succeeded,
            Scenario: null,
            EvidenceRef: null,
            FailureCode: type is AcceptanceEventType.GateNotImplemented
                ? AcceptanceFailureCodes.GateNotImplemented
                : null);
}
