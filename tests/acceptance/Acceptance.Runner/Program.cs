using ICP.Platform.Acceptance;

try
{
    var options = ParseArguments(args);
    var runner = new AcceptanceSkeletonRunner();
    var outcome = await runner.RunAsync(
        options.RepositoryRoot,
        options.OutputRoot,
        options.Gate);

    Console.WriteLine($"ACCEPTANCE_RESULT: {outcome.ResultPath}");
    return outcome.ExitCode;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"ACC_HARNESS_INVALID: {exception.Message}");
    return 2;
}

static RunnerOptions ParseArguments(string[] arguments)
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

    if (!values.TryGetValue("--repository-root", out var repositoryRoot) ||
        !values.TryGetValue("--output-root", out var outputRoot))
    {
        throw new ArgumentException("缺少 --repository-root 或 --output-root。");
    }

    var gateText = values.GetValueOrDefault("--gate", "All");
    if (!Enum.TryParse<AcceptanceGateSelection>(gateText, ignoreCase: true, out var gate))
    {
        throw new ArgumentException("--gate 只能是 Personnel、Plugin 或 All。");
    }

    return new RunnerOptions(Path.GetFullPath(repositoryRoot), Path.GetFullPath(outputRoot), gate);
}

internal sealed record RunnerOptions(
    string RepositoryRoot,
    string OutputRoot,
    AcceptanceGateSelection Gate);
