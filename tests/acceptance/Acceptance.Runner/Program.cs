using ICP.Platform.Acceptance;

try
{
    var values = ParseValues(args);
    if (values.TryGetValue("--production-cold-start-mode", out var modeText))
    {
        var options = ParseProductionColdStartArguments(values, modeText);
        var outcome = await new ProductionColdStartAcceptanceRunner().RunAsync(
            options.RepositoryRoot,
            options.OutputRoot,
            options.Mode,
            options.BaselineResultPath);

        Console.WriteLine($"PRODUCTION_COLD_START_RESULT: {outcome.ResultPath}");
        return outcome.ExitCode;
    }

    var acceptanceOptions = ParseAcceptanceArguments(values);
    var acceptanceOutcome = await new AcceptanceSkeletonRunner().RunAsync(
        acceptanceOptions.RepositoryRoot,
        acceptanceOptions.OutputRoot,
        acceptanceOptions.Gate);

    Console.WriteLine($"ACCEPTANCE_RESULT: {acceptanceOutcome.ResultPath}");
    return acceptanceOutcome.ExitCode;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"ACC_HARNESS_INVALID: {exception.Message}");
    return 2;
}

// 两个 runner 共享严格的 --name value 参数形状，重复键由最后一次显式输入覆盖。
static Dictionary<string, string> ParseValues(string[] arguments)
{
    var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    for (var index = 0; index < arguments.Length; index += 2)
    {
        if (index + 1 >= arguments.Length || !arguments[index].StartsWith("--", StringComparison.Ordinal))
        {
            throw new ArgumentException("参数必须使用 --name value 形式。");
        }

        values[arguments[index]] = arguments[index + 1];
    }

    return values;
}

// 现有 Acceptance gate 参数保持不变。
static RunnerOptions ParseAcceptanceArguments(IReadOnlyDictionary<string, string> values)
{
    var (repositoryRoot, outputRoot) = ReadCommonPaths(values);

    var gateText = values.GetValueOrDefault("--gate", "All");
    if (!Enum.TryParse<AcceptanceGateSelection>(gateText, ignoreCase: true, out var gate))
    {
        throw new ArgumentException("--gate 只能是 IdentityFoundation、Personnel、Plugin 或 All。");
    }

    return new RunnerOptions(Path.GetFullPath(repositoryRoot), Path.GetFullPath(outputRoot), gate);
}

// production cold-start 入口显式区分 baseline/candidate，并让 candidate 带入冻结 baseline result。
static ProductionColdStartRunnerOptions ParseProductionColdStartArguments(
    IReadOnlyDictionary<string, string> values,
    string modeText)
{
    var (repositoryRoot, outputRoot) = ReadCommonPaths(values);
    if (!Enum.TryParse<ProductionColdStartMode>(modeText, ignoreCase: true, out var mode))
    {
        throw new ArgumentException("--production-cold-start-mode 只能是 Baseline 或 Candidate。");
    }
    var baselineResult = values.GetValueOrDefault("--baseline-result");
    return new ProductionColdStartRunnerOptions(
        repositoryRoot,
        outputRoot,
        mode,
        string.IsNullOrWhiteSpace(baselineResult) ? null : Path.GetFullPath(baselineResult));
}

// 两种 runner 都必须固定显式 repository/output 根目录。
static (string RepositoryRoot, string OutputRoot) ReadCommonPaths(
    IReadOnlyDictionary<string, string> values)
{
    if (!values.TryGetValue("--repository-root", out var repositoryRoot)
        || !values.TryGetValue("--output-root", out var outputRoot))
    {
        throw new ArgumentException("缺少 --repository-root 或 --output-root。");
    }
    return (Path.GetFullPath(repositoryRoot), Path.GetFullPath(outputRoot));
}

internal sealed record RunnerOptions(
    string RepositoryRoot,
    string OutputRoot,
    AcceptanceGateSelection Gate);

internal sealed record ProductionColdStartRunnerOptions(
    string RepositoryRoot,
    string OutputRoot,
    ProductionColdStartMode Mode,
    string? BaselineResultPath);
