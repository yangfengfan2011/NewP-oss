namespace ICP.Platform.Kernel.PluginDiscovery;

public class PluginDependencyValidator
{
    public IReadOnlyDictionary<string, PluginScanResult> Validate(IReadOnlyList<PluginScanResult> results)
    {
        var successfulResults = results
            .Where(result => result.IsSuccess && result.Manifest is not null)
            .ToDictionary(
                result => result.Manifest!.ModuleId,
                result => result,
                StringComparer.OrdinalIgnoreCase);

        var cycleFailures = DetectCircularDependencyFailures(successfulResults);
        var dependencyFailures = DetectUnsatisfiedDependencyFailures(successfulResults, cycleFailures.Keys);

        return cycleFailures
            .Concat(dependencyFailures)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
    }

    private static Dictionary<string, PluginScanResult> DetectCircularDependencyFailures(
        IReadOnlyDictionary<string, PluginScanResult> successfulResults)
    {
        var failures = new Dictionary<string, PluginScanResult>(StringComparer.OrdinalIgnoreCase);
        var visitStates = new Dictionary<string, DependencyVisitState>(StringComparer.OrdinalIgnoreCase);
        var stack = new List<string>();

        foreach (var moduleId in successfulResults.Keys)
        {
            Visit(moduleId, successfulResults, visitStates, stack, failures);
        }

        return failures;
    }

    private static void Visit(
        string moduleId,
        IReadOnlyDictionary<string, PluginScanResult> successfulResults,
        Dictionary<string, DependencyVisitState> visitStates,
        List<string> stack,
        Dictionary<string, PluginScanResult> failures)
    {
        if (failures.ContainsKey(moduleId))
        {
            return;
        }

        if (visitStates.TryGetValue(moduleId, out var visitState))
        {
            if (visitState == DependencyVisitState.Visited)
            {
                return;
            }

            if (visitState == DependencyVisitState.Visiting)
            {
                RegisterCircularDependency(moduleId, successfulResults, stack, failures);
                return;
            }
        }

        visitStates[moduleId] = DependencyVisitState.Visiting;
        stack.Add(moduleId);

        var manifest = successfulResults[moduleId].Manifest!;
        foreach (var dependencyId in manifest.Dependencies.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!successfulResults.ContainsKey(dependencyId))
            {
                continue;
            }

            Visit(dependencyId, successfulResults, visitStates, stack, failures);
        }

        stack.RemoveAt(stack.Count - 1);
        visitStates[moduleId] = DependencyVisitState.Visited;
    }

    private static void RegisterCircularDependency(
        string moduleId,
        IReadOnlyDictionary<string, PluginScanResult> successfulResults,
        List<string> stack,
        Dictionary<string, PluginScanResult> failures)
    {
        var cycleStartIndex = stack.FindIndex(item => string.Equals(item, moduleId, StringComparison.OrdinalIgnoreCase));
        if (cycleStartIndex < 0)
        {
            return;
        }

        var cyclePath = stack.Skip(cycleStartIndex).ToList();
        cyclePath.Add(moduleId);
        var detailMessage = $"Circular dependency detected: {string.Join(" -> ", cyclePath)}";

        foreach (var cycleModuleId in cyclePath.Take(cyclePath.Count - 1).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (failures.ContainsKey(cycleModuleId))
            {
                continue;
            }

            var result = successfulResults[cycleModuleId];
            failures[cycleModuleId] = new PluginScanResult
            {
                IsSuccess = false,
                Manifest = result.Manifest,
                ErrorType = PluginScanErrorType.CircularDependency,
                ErrorMessage = detailMessage,
                PluginDirectory = result.PluginDirectory,
                ResolvedEntryAssemblyPath = result.ResolvedEntryAssemblyPath
            };
        }
    }

    private static Dictionary<string, PluginScanResult> DetectUnsatisfiedDependencyFailures(
        IReadOnlyDictionary<string, PluginScanResult> successfulResults,
        IEnumerable<string> excludedModuleIds)
    {
        var failures = new Dictionary<string, PluginScanResult>(StringComparer.OrdinalIgnoreCase);
        var candidateModuleIds = new HashSet<string>(successfulResults.Keys, StringComparer.OrdinalIgnoreCase);

        foreach (var excludedModuleId in excludedModuleIds)
        {
            candidateModuleIds.Remove(excludedModuleId);
        }

        var changed = true;
        while (changed)
        {
            changed = false;

            foreach (var moduleId in candidateModuleIds.ToList())
            {
                var result = successfulResults[moduleId];
                var missingDependencies = result.Manifest!.Dependencies
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Where(dependencyId => !candidateModuleIds.Contains(dependencyId))
                    .ToList();

                if (missingDependencies.Count == 0)
                {
                    continue;
                }

                failures[moduleId] = new PluginScanResult
                {
                    IsSuccess = false,
                    Manifest = result.Manifest,
                    ErrorType = PluginScanErrorType.DependencyNotSatisfied,
                    ErrorMessage = $"Missing dependencies: {string.Join(", ", missingDependencies)}",
                    PluginDirectory = result.PluginDirectory,
                    ResolvedEntryAssemblyPath = result.ResolvedEntryAssemblyPath
                };

                candidateModuleIds.Remove(moduleId);
                changed = true;
            }
        }

        return failures;
    }

    private enum DependencyVisitState
    {
        Visiting,
        Visited
    }
}
