using System.Security.Cryptography;

namespace ICP.Platform.Acceptance;

public sealed class AcceptanceSkeletonRunner
{
    private static readonly AcceptanceGateDefinition[] Definitions =
    [
        new(
            AcceptanceGateKind.IdentityFoundation,
            IsImplemented: true,
            RequiredScenarios:
            [
                IdentityFoundationAcceptanceRunner.PreflightScenario,
                IdentityFoundationAcceptanceRunner.ApiScenario,
                IdentityFoundationAcceptanceRunner.BrowserScenario,
                IdentityFoundationAcceptanceRunner.RestartScenario,
                IdentityFoundationAcceptanceRunner.SecurityScenario,
                IdentityFoundationAcceptanceRunner.PersistenceScenario
            ]),
        new(
            AcceptanceGateKind.Personnel,
            IsImplemented: true,
            RequiredScenarios:
            [
                PersonnelAcceptanceRunner.PreflightScenario,
                PersonnelAcceptanceRunner.ApiScenario,
                PersonnelAcceptanceRunner.BrowserScenario,
                PersonnelAcceptanceRunner.RestartScenario
            ]),
        new(
            AcceptanceGateKind.ExampleRequiredField,
            IsImplemented: true,
            RequiredScenarios: ExampleRequiredFieldAcceptanceRunner.RequiredScenarios),
        new(AcceptanceGateKind.Plugin, IsImplemented: false, RequiredScenarios: [])
    ];

    public async Task<AcceptanceRunOutcome> RunAsync(
        string repositoryRoot,
        string outputRoot,
        AcceptanceGateSelection selection,
        string? requestedRunId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputRoot);

        repositoryRoot = Path.GetFullPath(repositoryRoot);
        outputRoot = Path.GetFullPath(outputRoot);
        var startedAtUtc = DateTimeOffset.UtcNow;
        var runId = requestedRunId ?? Convert.ToHexString(RandomNumberGenerator.GetBytes(12)).ToLowerInvariant();
        if (runId.Length != 24 || runId.Any(static value => value is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
        {
            throw new ArgumentException("Acceptance run id 必须是 24 位小写十六进制。", nameof(requestedRunId));
        }
        var runDirectory = Path.Combine(outputRoot, "runs", $"{startedAtUtc:yyyyMMddTHHmmssfffZ}-{runId}");
        await using var repositoryGuard = await AcceptanceRepositoryGuard.StartAsync(
            repositoryRoot,
            cancellationToken: cancellationToken);
        Directory.CreateDirectory(runDirectory);

        var requestedGates = AcceptanceReducer.GetRequestedGates(selection);
        var eventLogPath = Path.Combine(runDirectory, "events.jsonl");
        var recorder = new AcceptanceObservationRecorder(new AcceptanceEventLog(eventLogPath));
        var artifactRefs = new List<string> { "events.jsonl", "request.sanitized.json", "evidence-index.json" };
        var processIdentities = new List<string>();
        var cleanup = new AcceptanceCleanupResult("not-started", AcceptanceFailureCodes.CleanupIncomplete);
        string? databaseFingerprint = null;

        await AcceptanceInfrastructure.WriteJsonAsync(
            Path.Combine(runDirectory, "request.sanitized.json"),
            new
            {
                schemaVersion = "1.0",
                requestedGates = requestedGates.Select(AcceptanceReducer.ToGateName).ToArray(),
                containsSecrets = false
            },
            cancellationToken);

        foreach (var gate in requestedGates)
        {
            await recorder.RecordAsync(gate, AcceptanceEventType.RunStarted, cancellationToken: cancellationToken);
            if (gate is AcceptanceGateKind.IdentityFoundation)
            {
                var execution = await new IdentityFoundationAcceptanceRunner(repositoryRoot, runDirectory, runId, recorder)
                    .RunAsync(cancellationToken);
                databaseFingerprint = execution.DatabaseFingerprint;
                processIdentities.AddRange(execution.ProcessIdentities);
                artifactRefs.AddRange(execution.ArtifactRefs);
                cleanup = execution.Cleanup;
                continue;
            }
            if (gate is AcceptanceGateKind.Personnel)
            {
                var execution = await new PersonnelAcceptanceRunner(repositoryRoot, runDirectory, runId, recorder)
                    .RunAsync(cancellationToken);
                databaseFingerprint = execution.DatabaseFingerprint;
                processIdentities.AddRange(execution.ProcessIdentities);
                artifactRefs.AddRange(execution.ArtifactRefs);
                cleanup = execution.Cleanup;
                continue;
            }
            if (gate is AcceptanceGateKind.ExampleRequiredField)
            {
                var execution = await new ExampleRequiredFieldAcceptanceRunner(
                        repositoryRoot,
                        runDirectory,
                        runId,
                        recorder)
                    .RunAsync(cancellationToken);
                databaseFingerprint = execution.DatabaseFingerprint;
                processIdentities.AddRange(execution.ProcessIdentities);
                artifactRefs.AddRange(execution.ArtifactRefs);
                cleanup = execution.Cleanup;
                continue;
            }

            await recorder.RecordAsync(
                gate,
                AcceptanceEventType.GateNotImplemented,
                AcceptanceObservationOutcome.Failed,
                failureCode: AcceptanceFailureCodes.GateNotImplemented,
                cancellationToken: cancellationToken);
        }

        var repositoryMatched = await repositoryGuard.CheckAsync(cancellationToken);
        foreach (var gate in requestedGates)
        {
            await recorder.RecordAsync(
                gate,
                AcceptanceEventType.TreeVerified,
                repositoryMatched ? AcceptanceObservationOutcome.Succeeded : AcceptanceObservationOutcome.Failed,
                failureCode: repositoryMatched ? null : AcceptanceFailureCodes.TreeChanged,
                cancellationToken: cancellationToken);
        }

        var reduction = AcceptanceReducer.Reduce(selection, Definitions, recorder.Observations);
        var distinctArtifacts = artifactRefs.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        await AcceptanceInfrastructure.WriteJsonAsync(
            Path.Combine(runDirectory, "evidence-index.json"),
            new
            {
                schemaVersion = "1.0",
                evidence = distinctArtifacts.Select(path => new { path }).ToArray()
            },
            cancellationToken);

        var finalRepositoryMatched = await repositoryGuard.StopAsync(cancellationToken);
        if (repositoryMatched && !finalRepositoryMatched)
        {
            foreach (var gate in requestedGates)
            {
                await recorder.RecordAsync(
                    gate,
                    AcceptanceEventType.TreeVerified,
                    AcceptanceObservationOutcome.Failed,
                    failureCode: AcceptanceFailureCodes.TreeChanged,
                    cancellationToken: cancellationToken);
            }
            reduction = AcceptanceReducer.Reduce(selection, Definitions, recorder.Observations);
        }

        var result = new AcceptanceRunResult(
            SchemaVersion: "1.0",
            RunId: runId,
            RequestedGates: requestedGates.Select(AcceptanceReducer.ToGateName).ToArray(),
            Commit: repositoryGuard.Baseline.HeadCommit,
            TreeHash: repositoryGuard.Baseline.HeadTree,
            PlatformApiRelease: null,
            PlatformApiDigest: null,
            ToolVersions: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["dotnet"] = Environment.Version.ToString(),
                ["playwright"] = "1.61.0"
            },
            DatabaseFingerprint: databaseFingerprint,
            ProcessIdentities: processIdentities,
            StartedAtUtc: startedAtUtc,
            CompletedAtUtc: DateTimeOffset.UtcNow,
            Verdict: reduction.Verdict,
            FailureCode: reduction.FailureCode,
            Gates: reduction.Gates,
            ArtifactRefs: distinctArtifacts,
            Cleanup: cleanup);
        var resultPath = Path.Combine(runDirectory, "result.json");
        await AcceptanceInfrastructure.WriteJsonAsync(resultPath, result, cancellationToken);
        await AcceptanceInfrastructure.WriteJsonAtomicallyAsync(
            Path.Combine(outputRoot, "current-result.json"),
            result,
            cancellationToken);
        return new AcceptanceRunOutcome(result.Verdict is AcceptanceVerdict.Passed ? 0 : 1, resultPath);
    }
}
