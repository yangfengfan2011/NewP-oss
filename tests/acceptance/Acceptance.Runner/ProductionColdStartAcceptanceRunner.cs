using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ICP.Platform.Acceptance;

public sealed record ProductionColdStartRunOutcome(int ExitCode, string ResultPath);

public sealed record ProductionColdStartRunResult(
    string SchemaVersion,
    string RunId,
    ProductionColdStartMode Mode,
    string Commit,
    string TreeHash,
    IReadOnlyDictionary<string, string> ToolVersions,
    IReadOnlyDictionary<string, object> Environment,
    string ReproductionCommand,
    string? BuildGraphArtifact,
    string BuildGraphVerdict,
    string? BuildGraphFailureCode,
    string RawSamplesArtifact,
    int ExpectedSamples,
    int CapturedSamples,
    string? DatabaseFingerprint,
    IReadOnlyList<string> ProcessIdentities,
    IReadOnlyList<string> ArtifactRefs,
    string? BrowserDiagnostic,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    AcceptanceVerdict Verdict,
    string? FailureCode,
    ProductionColdStartReductionResult? ColdStart,
    AcceptanceCleanupResult Cleanup);

public sealed class ProductionColdStartAcceptanceRunner
{
    /// <summary>
    /// 固定当前 commit/tree，生成生产构建图，并在结构允许时运行四场景真实 cold-start 度量。
    /// </summary>
    public async Task<ProductionColdStartRunOutcome> RunAsync(
        string repositoryRoot,
        string outputRoot,
        ProductionColdStartMode mode,
        string? baselineResultPath = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputRoot);
        if (mode is ProductionColdStartMode.Candidate && string.IsNullOrWhiteSpace(baselineResultPath))
        {
            throw new ArgumentException("Candidate mode requires --baseline-result from the same frozen environment.");
        }

        repositoryRoot = Path.GetFullPath(repositoryRoot);
        outputRoot = Path.GetFullPath(outputRoot);
        baselineResultPath = string.IsNullOrWhiteSpace(baselineResultPath)
            ? null
            : Path.GetFullPath(baselineResultPath);
        var startedAtUtc = DateTimeOffset.UtcNow;
        var runId = Convert.ToHexString(RandomNumberGenerator.GetBytes(12)).ToLowerInvariant();
        var runDirectory = Path.Combine(outputRoot, "runs", $"{startedAtUtc:yyyyMMddTHHmmssfffZ}-{runId}");
        Directory.CreateDirectory(runDirectory);
        await using var repositoryGuard = await AcceptanceRepositoryGuard.StartAsync(
            repositoryRoot,
            cancellationToken: cancellationToken);

        var artifacts = new List<string>();
        var processIdentities = new List<string>();
        var rawSamplesPath = Path.Combine(runDirectory, "raw-samples.json");
        var buildGraphPath = Path.Combine(runDirectory, "build-graph.json");
        var cleanup = new AcceptanceCleanupResult("completed", null);
        var samples = Array.Empty<ProductionColdStartSample>();
        ProductionColdStartReductionResult? reduction = null;
        PersonnelProductionColdStartExecution? execution = null;
        string buildGraphVerdict = "not-run";
        string? buildGraphFailureCode = null;
        string? terminalFailureCode = null;
        string? browserDiagnostic = null;
        var chromiumVersion = "unavailable";
        var repositoryMatched = false;
        var settings = LoadSettings(Path.Combine(
            repositoryRoot,
            "scripts",
            "performance",
            "production-cold-start.config.json"));

        await AcceptanceInfrastructure.WriteJsonAsync(
            Path.Combine(runDirectory, "request.sanitized.json"),
            new
            {
                schemaVersion = "1.0",
                mode,
                baselineResult = baselineResultPath is null ? null : Path.GetFileName(baselineResultPath),
                samplesPerScenario = settings.SamplesPerScenario,
                throttle = settings.Throttle,
                containsSecrets = false
            },
            cancellationToken);
        artifacts.Add("request.sanitized.json");
        await AcceptanceInfrastructure.WriteJsonAsync(
            rawSamplesPath,
            new { schemaVersion = "1.0", containsSecrets = false, samples },
            cancellationToken);
        artifacts.Add("raw-samples.json");

        try
        {
            var build = await RunShellBuildAsync(repositoryRoot, runDirectory, cancellationToken);
            artifacts.Add("shell-build.stdout.log");
            artifacts.Add("shell-build.stderr.log");
            if (build.ExitCode != 0)
            {
                terminalFailureCode = AcceptanceFailureCodes.ProductionColdStartBuildFailed;
            }
            else
            {
                var analyzer = await RunBuildGraphAnalyzerAsync(
                    repositoryRoot,
                    buildGraphPath,
                    mode,
                    cancellationToken);
                artifacts.Add("build-graph.json");
                using var buildGraph = JsonDocument.Parse(await File.ReadAllTextAsync(buildGraphPath, cancellationToken));
                buildGraphVerdict = buildGraph.RootElement.GetProperty("verdict").GetString() ?? "failed";
                buildGraphFailureCode = ReadOptionalString(buildGraph.RootElement, "failureCode");
                if (analyzer.ExitCode != 0)
                {
                    terminalFailureCode = buildGraphFailureCode ?? "BUILD_GRAPH_INPUT_INVALID";
                }
                else
                {
                    var targetCapabilityAssets = buildGraph.RootElement
                        .GetProperty("targetCapabilityAssets")
                        .EnumerateArray()
                        .Select(static item => item.GetString())
                        .Where(static item => !string.IsNullOrWhiteSpace(item))
                        .Select(static item => item!)
                        .ToArray();
                    var eventLogPath = Path.Combine(runDirectory, "events.jsonl");
                    var recorder = new AcceptanceObservationRecorder(new AcceptanceEventLog(eventLogPath));
                    execution = await new PersonnelAcceptanceRunner(repositoryRoot, runDirectory, runId, recorder)
                        .RunProductionColdStartAsync(
                            Path.Combine(repositoryRoot, "frontend", "packages", "shell", "dist"),
                            settings.SamplesPerScenario,
                            settings.Throttle,
                            targetCapabilityAssets,
                            rawSamplesPath,
                            cancellationToken);
                    samples = execution.Samples.ToArray();
                    cleanup = execution.Cleanup;
                    browserDiagnostic = execution.FailureDiagnostic;
                    chromiumVersion = execution.ChromiumVersion;
                    processIdentities.AddRange(execution.ProcessIdentities);
                    artifacts.AddRange(execution.ArtifactRefs);
                    artifacts.Add("events.jsonl");
                    await AcceptanceInfrastructure.WriteJsonAtomicallyAsync(
                        rawSamplesPath,
                        new { schemaVersion = "1.0", containsSecrets = false, samples },
                        cancellationToken);
                    var baselineSummaries = mode is ProductionColdStartMode.Candidate
                        ? LoadBaselineSummaries(baselineResultPath!)
                        : null;
                    reduction = ProductionColdStartReducer.Reduce(new ProductionColdStartReductionRequest(
                        mode,
                        settings.SamplesPerScenario,
                        settings.Throttle,
                        samples,
                        targetCapabilityAssets,
                        settings.Budgets,
                        baselineSummaries,
                        cleanup));
                    terminalFailureCode = reduction.FailureCode;
                }
            }
        }
        catch (Exception exception)
        {
            terminalFailureCode ??= AcceptanceFailureCodes.ProductionColdStartBrowserFailed;
            browserDiagnostic = exception.Message;
            await AcceptanceInfrastructure.WriteJsonAsync(
                Path.Combine(runDirectory, "harness-failure.json"),
                new
                {
                    status = "failed",
                    failureCode = terminalFailureCode,
                    diagnostic = browserDiagnostic
                },
                CancellationToken.None);
            artifacts.Add("harness-failure.json");
        }
        finally
        {
            repositoryMatched = await repositoryGuard.StopAsync(CancellationToken.None);
        }

        if (!repositoryMatched)
        {
            terminalFailureCode = AcceptanceFailureCodes.TreeChanged;
        }
        var verdict = terminalFailureCode is null ? AcceptanceVerdict.Passed : AcceptanceVerdict.Failed;
        var toolVersions = await ReadToolVersionsAsync(repositoryRoot, chromiumVersion, CancellationToken.None);
        var reproductionCommand = BuildReproductionCommand(mode, baselineResultPath);
        var distinctArtifacts = artifacts
            .Where(path => File.Exists(Path.Combine(runDirectory, path.Replace('/', Path.DirectorySeparatorChar))))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var result = new ProductionColdStartRunResult(
            SchemaVersion: "1.0",
            RunId: runId,
            Mode: mode,
            Commit: repositoryGuard.Baseline.HeadCommit,
            TreeHash: repositoryGuard.Baseline.HeadTree,
            ToolVersions: toolVersions,
            Environment: new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["operatingSystem"] = RuntimeInformation.OSDescription,
                ["processArchitecture"] = RuntimeInformation.ProcessArchitecture.ToString(),
                ["processorCount"] = Environment.ProcessorCount,
                ["viewport"] = "1440x900",
                ["network"] = "10Mbps-down/5Mbps-up/40ms-rtt",
                ["cpuSlowdown"] = "4x"
            },
            ReproductionCommand: reproductionCommand,
            BuildGraphArtifact: File.Exists(buildGraphPath) ? "build-graph.json" : null,
            BuildGraphVerdict: buildGraphVerdict,
            BuildGraphFailureCode: buildGraphFailureCode,
            RawSamplesArtifact: "raw-samples.json",
            ExpectedSamples: settings.SamplesPerScenario * ProductionColdStartScenarios.All.Count,
            CapturedSamples: samples.Length,
            DatabaseFingerprint: execution?.DatabaseFingerprint,
            ProcessIdentities: processIdentities.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
            ArtifactRefs: distinctArtifacts,
            BrowserDiagnostic: browserDiagnostic,
            StartedAtUtc: startedAtUtc,
            CompletedAtUtc: DateTimeOffset.UtcNow,
            Verdict: verdict,
            FailureCode: terminalFailureCode,
            ColdStart: reduction,
            Cleanup: cleanup);
        var resultPath = Path.Combine(runDirectory, "result.json");
        await AcceptanceInfrastructure.WriteJsonAsync(resultPath, result, CancellationToken.None);
        await AcceptanceInfrastructure.WriteJsonAtomicallyAsync(
            Path.Combine(outputRoot, "current-result.json"),
            result,
            CancellationToken.None);
        return new ProductionColdStartRunOutcome(
            result.Verdict is AcceptanceVerdict.Passed ? 0 : 1,
            resultPath);
    }

    // Shell build 固定从 frontend workspace 执行，并保留完整 Vite stdout/warning 证据。
    private static async Task<AcceptanceProcessResult> RunShellBuildAsync(
        string repositoryRoot,
        string runDirectory,
        CancellationToken cancellationToken)
    {
        var result = await AcceptanceInfrastructure.RunProcessAsync(
            ResolveCorepackPath(),
            ["pnpm", "--dir", "frontend", "--filter", "@icp-platform/shell", "build"],
            repositoryRoot,
            cancellationToken: cancellationToken);
        await File.WriteAllTextAsync(
            Path.Combine(runDirectory, "shell-build.stdout.log"),
            result.StandardOutput,
            new UTF8Encoding(false),
            cancellationToken);
        await File.WriteAllTextAsync(
            Path.Combine(runDirectory, "shell-build.stderr.log"),
            result.StandardError,
            new UTF8Encoding(false),
            cancellationToken);
        return result;
    }

    // baseline 记录现状违规且继续采样；candidate 的非零结果在进入浏览器前直接成为稳定门禁失败。
    private static Task<AcceptanceProcessResult> RunBuildGraphAnalyzerAsync(
        string repositoryRoot,
        string outputPath,
        ProductionColdStartMode mode,
        CancellationToken cancellationToken) =>
        AcceptanceInfrastructure.RunProcessAsync(
            "node",
            [
                Path.Combine(repositoryRoot, "scripts", "performance", "analyze-shell-build.mjs"),
                "--manifest", Path.Combine(repositoryRoot, "frontend", "packages", "shell", "dist", ".vite", "manifest.json"),
                "--config", Path.Combine(repositoryRoot, "scripts", "performance", "shell-build-graph.config.json"),
                "--assets-root", Path.Combine(repositoryRoot, "frontend", "packages", "shell", "dist"),
                "--mode", mode.ToString().ToLowerInvariant(),
                "--output", outputPath
            ],
            repositoryRoot,
            cancellationToken: cancellationToken);

    // 配置解析同时验证冻结的 20 次、viewport 与 throttle，避免命令行悄然改变可比环境。
    private static ProductionColdStartSettings LoadSettings(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        if (root.GetProperty("schemaVersion").GetString() != "1.0")
        {
            throw new InvalidOperationException("Production cold-start config schema must be 1.0.");
        }
        var samples = root.GetProperty("samplesPerScenario").GetInt32();
        var viewport = root.GetProperty("viewport");
        if (samples != 20
            || viewport.GetProperty("width").GetInt32() != 1440
            || viewport.GetProperty("height").GetInt32() != 900)
        {
            throw new InvalidOperationException("Production cold-start config must keep 20 samples at 1440x900.");
        }
        var throttleElement = root.GetProperty("throttle");
        var throttle = new ProductionColdStartThrottleProfile(
            throttleElement.GetProperty("downloadBytesPerSecond").GetInt32(),
            throttleElement.GetProperty("uploadBytesPerSecond").GetInt32(),
            throttleElement.GetProperty("latencyMs").GetInt32(),
            throttleElement.GetProperty("cpuSlowdownRate").GetInt32());
        if (throttle != ProductionColdStartThrottleProfile.Required)
        {
            throw new InvalidOperationException("Production cold-start throttle differs from the frozen #58 profile.");
        }
        var budgets = root.GetProperty("budgets")
            .EnumerateObject()
            .ToDictionary(
                static property => property.Name,
                static property => new ProductionColdStartBudget(
                    property.Value.GetProperty("maxP50Ms").GetInt64(),
                    property.Value.GetProperty("maxP95Ms").GetInt64(),
                    property.Value.TryGetProperty("minimumImprovementRatio", out var ratio)
                        ? ratio.GetDecimal()
                        : null),
                StringComparer.Ordinal);
        return new ProductionColdStartSettings(samples, throttle, budgets);
    }

    // candidate 必须从 baseline result 的机器摘要读取同场景 p50/p95，不接受人工转录数字。
    private static IReadOnlyDictionary<string, ProductionColdStartScenarioSummary> LoadBaselineSummaries(
        string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        if (!string.Equals(root.GetProperty("mode").GetString(), "baseline", StringComparison.OrdinalIgnoreCase)
            || !root.TryGetProperty("coldStart", out var coldStart)
            || coldStart.ValueKind is JsonValueKind.Null)
        {
            throw new InvalidOperationException("Candidate baseline result does not contain a baseline coldStart summary.");
        }
        return coldStart.GetProperty("scenarios")
            .EnumerateObject()
            .ToDictionary(
                static property => property.Name,
                static property => new ProductionColdStartScenarioSummary(
                    property.Value.GetProperty("sampleCount").GetInt32(),
                    property.Value.GetProperty("interactiveP50Ms").GetInt64(),
                    property.Value.GetProperty("interactiveP95Ms").GetInt64(),
                    property.Value.GetProperty("jsTransferP50Bytes").GetInt64(),
                    property.Value.GetProperty("jsTransferP95Bytes").GetInt64(),
                    property.Value.GetProperty("longTaskP50Ms").GetInt64(),
                    property.Value.GetProperty("longTaskP95Ms").GetInt64()),
                StringComparer.Ordinal);
    }

    // 工具版本全部来自本次执行环境；不可用工具以显式 unavailable 表示。
    private static async Task<IReadOnlyDictionary<string, string>> ReadToolVersionsAsync(
        string repositoryRoot,
        string chromiumVersion,
        CancellationToken cancellationToken)
    {
        var node = await ReadProcessVersionAsync("node", ["--version"], repositoryRoot, cancellationToken);
        var pnpm = await ReadProcessVersionAsync(
            ResolveCorepackPath(),
            ["pnpm", "--version"],
            repositoryRoot,
            cancellationToken);
        using var vitePackage = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            repositoryRoot,
            "frontend",
            "node_modules",
            "vite",
            "package.json")));
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["dotnet"] = Environment.Version.ToString(),
            ["node"] = node,
            ["pnpm"] = pnpm,
            ["vite"] = vitePackage.RootElement.GetProperty("version").GetString() ?? "unavailable",
            ["chromium"] = chromiumVersion,
            ["playwright"] = "1.61.0"
        };
    }

    // 版本命令失败不会覆盖主要门禁结果，但会在 schema 中留下 unavailable。
    private static async Task<string> ReadProcessVersionAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await AcceptanceInfrastructure.RunProcessAsync(
                fileName,
                arguments,
                workingDirectory,
                cancellationToken: cancellationToken);
            return result.ExitCode == 0 ? result.StandardOutput.Trim() : "unavailable";
        }
        catch
        {
            return "unavailable";
        }
    }

    // Windows 下 ProcessStartInfo 需要 corepack.cmd 的绝对路径；其他平台直接使用 PATH 命令。
    private static string ResolveCorepackPath()
    {
        if (!OperatingSystem.IsWindows())
        {
            return "corepack";
        }
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                     .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var candidate = Path.Combine(directory, "corepack.cmd");
            if (File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }
        throw new InvalidOperationException("corepack.cmd was not found on PATH.");
    }

    // result 内保存可直接复制的精确 wrapper 命令；candidate 同时固定 baseline 证据路径。
    private static string BuildReproductionCommand(ProductionColdStartMode mode, string? baselineResultPath)
    {
        var command = $"pwsh.exe -NoLogo -NoProfile -File .\\scripts\\accept-production-cold-start.ps1 -Mode {mode}";
        return baselineResultPath is null
            ? command
            : $"{command} -BaselineResult \"{baselineResultPath}\"";
    }

    // Json null 与缺失字段都统一返回 null，避免 baseline report 的显式 null 解析异常。
    private static string? ReadOptionalString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) && property.ValueKind is JsonValueKind.String
            ? property.GetString()
            : null;

    private sealed record ProductionColdStartSettings(
        int SamplesPerScenario,
        ProductionColdStartThrottleProfile Throttle,
        IReadOnlyDictionary<string, ProductionColdStartBudget> Budgets);
}
