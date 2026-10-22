namespace ICP.Platform.Kernel.PluginDiscovery;

/// <summary>
/// 对扫描成功的插件结果做依赖层面的二次校验。
/// 这里不负责发现插件本身，只负责把“结构可读”进一步收敛为“依赖上可装配”。
/// </summary>
public class PluginDependencyValidator
{
    public IReadOnlyDictionary<string, PluginScanResult> Validate(IReadOnlyList<PluginScanResult> results)
    {
        // 只有已经通过基础扫描的结果才进入依赖校验；
        // 结构错误和依赖错误分层处理，便于调用方保留原始失败信号。
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
        // 只有当前批次里真实存在的依赖才参与环检测；
        // 缺失依赖留给后面的 unsatisfied 阶段统一处理，避免两类失败信号互相覆盖。
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

        // 环上的每个模块都要显式标记失败，而不是只标记当前回访点；
        // 否则调用方只能看到“某一环节点坏了”，无法知道整条闭环都不可装配。
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

        // 缺失依赖要迭代收缩候选集，因为 A 依赖 B、B 又因缺失 C 被淘汰时，
        // A 也应在下一轮变成 dependency-not-satisfied。
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
