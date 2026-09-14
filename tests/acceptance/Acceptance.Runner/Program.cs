using ICP.Platform.Acceptance;

// 模块闸门使用 dotnet test；独立测量入口分别拥有 production cold-start 或浏览器观察。
if (args is ["--observe-feature-scale"]) return await FeatureScaleBrowserObserver.RunConsoleAsync();
try
{
    var values = ParseValues(args);
    if (!values.TryGetValue("--production-cold-start-mode", out var modeText))
    {
        throw new ArgumentException("缺少 --production-cold-start-mode。");
    }

    var options = ParseProductionColdStartArguments(values, modeText);
    var outcome = await new ProductionColdStartAcceptanceRunner().RunAsync(
        options.RepositoryRoot,
        options.OutputRoot,
        options.Mode,
        options.BaselineResultPath);

    Console.WriteLine($"PRODUCTION_COLD_START_RESULT: {outcome.ResultPath}");
    return outcome.ExitCode;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"ACC_HARNESS_INVALID: {exception.Message}");
    return 2;
}

// 严格的 --name value 参数形状，重复键由最后一次显式输入覆盖。
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

// 显式区分 baseline/candidate，并让 candidate 带入冻结 baseline result。
static ProductionColdStartRunnerOptions ParseProductionColdStartArguments(
    IReadOnlyDictionary<string, string> values,
    string modeText)
{
    if (!values.TryGetValue("--repository-root", out var repositoryRoot)
        || !values.TryGetValue("--output-root", out var outputRoot))
    {
        throw new ArgumentException("缺少 --repository-root 或 --output-root。");
    }
    if (!Enum.TryParse<ProductionColdStartMode>(modeText, ignoreCase: true, out var mode))
    {
        throw new ArgumentException("--production-cold-start-mode 只能是 Baseline、Candidate 或 Qualification。");
    }

    var baselineResult = values.GetValueOrDefault("--baseline-result");
    return new ProductionColdStartRunnerOptions(
        Path.GetFullPath(repositoryRoot),
        Path.GetFullPath(outputRoot),
        mode,
        string.IsNullOrWhiteSpace(baselineResult) ? null : Path.GetFullPath(baselineResult));
}

internal sealed record ProductionColdStartRunnerOptions(
    string RepositoryRoot,
    string OutputRoot,
    ProductionColdStartMode Mode,
    string? BaselineResultPath);
