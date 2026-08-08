using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ICP.Platform.Acceptance;

public sealed class AcceptanceSkeletonRunner
{
    private static readonly AcceptanceGateDefinition[] Definitions =
    [
        new(AcceptanceGateKind.Personnel, IsImplemented: false, RequiredScenarios: []),
        new(AcceptanceGateKind.Plugin, IsImplemented: false, RequiredScenarios: [])
    ];

    public async Task<AcceptanceRunOutcome> RunAsync(
        string repositoryRoot,
        string outputRoot,
        AcceptanceGateSelection selection,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputRoot);

        var startedAtUtc = DateTimeOffset.UtcNow;
        var runId = Convert.ToHexString(RandomNumberGenerator.GetBytes(12)).ToLowerInvariant();
        var runDirectory = Path.Combine(
            Path.GetFullPath(outputRoot),
            "runs",
            $"{startedAtUtc:yyyyMMddTHHmmssfffZ}-{runId}");
        Directory.CreateDirectory(runDirectory);

        var requestedGates = AcceptanceReducer.GetRequestedGates(selection);
        var eventLogPath = Path.Combine(runDirectory, "events.jsonl");
        var eventLog = new AcceptanceEventLog(eventLogPath);
        var observations = new List<AcceptanceObservation>();
        long sequence = 0;

        foreach (var gate in requestedGates)
        {
            var started = Observation(++sequence, gate, AcceptanceEventType.RunStarted, null);
            observations.Add(started);
            await eventLog.AppendAsync(started, cancellationToken);

            var unavailable = Observation(
                ++sequence,
                gate,
                AcceptanceEventType.GateNotImplemented,
                AcceptanceFailureCodes.GateNotImplemented);
            observations.Add(unavailable);
            await eventLog.AppendAsync(unavailable, cancellationToken);
        }

        var reduction = AcceptanceReducer.Reduce(selection, Definitions, observations);
        var commit = await RunGitAsync(repositoryRoot, ["rev-parse", "HEAD"], cancellationToken);
        var status = await RunGitAsync(repositoryRoot, ["status", "--porcelain"], cancellationToken);
        var treeHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(status))).ToLowerInvariant();
        var completedAtUtc = DateTimeOffset.UtcNow;
        var result = new AcceptanceRunResult(
            SchemaVersion: "1.0",
            RunId: runId,
            RequestedGates: requestedGates.Select(AcceptanceReducer.ToGateName).ToArray(),
            Commit: commit,
            TreeHash: treeHash,
            PlatformApiRelease: null,
            PlatformApiDigest: null,
            ToolVersions: new Dictionary<string, string>(StringComparer.Ordinal),
            DatabaseFingerprint: null,
            ProcessIdentities: [],
            StartedAtUtc: startedAtUtc,
            CompletedAtUtc: completedAtUtc,
            Verdict: reduction.Verdict,
            FailureCode: reduction.FailureCode,
            Gates: reduction.Gates,
            ArtifactRefs: ["events.jsonl", "request.sanitized.json", "evidence-index.json"],
            Cleanup: new AcceptanceCleanupResult("not-started", AcceptanceFailureCodes.GateNotImplemented));

        await WriteJsonAsync(
            Path.Combine(runDirectory, "request.sanitized.json"),
            new
            {
                schemaVersion = "1.0",
                requestedGates = result.RequestedGates,
                containsSecrets = false
            },
            cancellationToken);
        await WriteJsonAsync(
            Path.Combine(runDirectory, "evidence-index.json"),
            new { schemaVersion = "1.0", evidence = Array.Empty<object>() },
            cancellationToken);

        var resultPath = Path.Combine(runDirectory, "result.json");
        await WriteJsonAsync(resultPath, result, cancellationToken);
        await WriteJsonAtomicallyAsync(
            Path.Combine(Path.GetFullPath(outputRoot), "current-result.json"),
            result,
            cancellationToken);

        return new AcceptanceRunOutcome(result.Verdict is AcceptanceVerdict.Passed ? 0 : 1, resultPath);
    }

    private static AcceptanceObservation Observation(
        long sequence,
        AcceptanceGateKind gate,
        AcceptanceEventType type,
        string? failureCode) =>
        new(
            SchemaVersion: "1.0",
            Sequence: sequence,
            OccurredAtUtc: DateTimeOffset.UtcNow,
            Gate: gate,
            Type: type,
            Outcome: type is AcceptanceEventType.GateNotImplemented
                ? AcceptanceObservationOutcome.Failed
                : AcceptanceObservationOutcome.Succeeded,
            Scenario: null,
            EvidenceRef: null,
            FailureCode: failureCode);

    private static async Task<string> RunGitAsync(
        string repositoryRoot,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = repositoryRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("无法启动 git。");
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var output = await outputTask;
        var error = await errorTask;
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"git {arguments[0]} 失败：{error.Trim()}");
        }

        return output.Trim();
    }

    private static async Task WriteJsonAsync<T>(
        string path,
        T value,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException("JSON 文件必须具有父目录。");
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(
            path,
            JsonSerializer.Serialize(value, AcceptanceJson.Options),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            cancellationToken);
    }

    private static async Task WriteJsonAtomicallyAsync<T>(
        string path,
        T value,
        CancellationToken cancellationToken)
    {
        var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        await WriteJsonAsync(temporaryPath, value, cancellationToken);
        File.Move(temporaryPath, path, overwrite: true);
    }
}
