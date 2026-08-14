namespace ICP.Platform.Acceptance;

public static class ProductionColdStartReducer
{
    /// <summary>
    /// 从结构化原始样本生成确定的场景分位数，后续证据与预算校验都复用同一裁决入口。
    /// </summary>
    /// <param name="request">包含固定环境、原始样本、预算与清理终态的裁决请求。</param>
    /// <returns>可直接序列化到验收证据包的裁决结果。</returns>
    public static ProductionColdStartReductionResult Reduce(ProductionColdStartReductionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var summaries = new Dictionary<string, ProductionColdStartScenarioSummary>(StringComparer.Ordinal);
        var violations = new List<ProductionColdStartViolation>();
        foreach (var scenario in ProductionColdStartScenarios.All)
        {
            var samples = request.Samples
                .Where(sample => string.Equals(sample.Scenario, scenario, StringComparison.Ordinal))
                .OrderBy(sample => sample.Iteration)
                .ToArray();
            if (samples.Length != request.ExpectedSamplesPerScenario)
            {
                violations.Add(new ProductionColdStartViolation(
                    AcceptanceFailureCodes.ProductionColdStartSampleCount,
                    scenario,
                    null,
                    $"Expected {request.ExpectedSamplesPerScenario} samples but received {samples.Length}."));
            }
            else if (!samples.Select(sample => sample.Iteration).SequenceEqual(
                         Enumerable.Range(1, request.ExpectedSamplesPerScenario)))
            {
                violations.Add(new ProductionColdStartViolation(
                    AcceptanceFailureCodes.ProductionColdStartSampleCount,
                    scenario,
                    null,
                    $"Iterations must be the unique sequence 1..{request.ExpectedSamplesPerScenario}."));
            }
            foreach (var skipped in samples.Where(sample => sample.Skipped))
            {
                violations.Add(new ProductionColdStartViolation(
                    AcceptanceFailureCodes.ProductionColdStartScenarioSkipped,
                    scenario,
                    skipped.Iteration,
                    $"Scenario sample was skipped: {skipped.SkipReason ?? "reason unavailable"}."));
            }
            foreach (var sample in samples.Where(sample => !sample.CacheCleared))
            {
                violations.Add(new ProductionColdStartViolation(
                    AcceptanceFailureCodes.ProductionColdStartCacheNotCleared,
                    scenario,
                    sample.Iteration,
                    "Chromium HTTP cache was not cleared before navigation."));
            }
            foreach (var sample in samples.Where(sample => !sample.ServiceWorkersBlocked))
            {
                violations.Add(new ProductionColdStartViolation(
                    AcceptanceFailureCodes.ProductionColdStartServiceWorkerEnabled,
                    scenario,
                    sample.Iteration,
                    "Service workers were not blocked for the cold-start sample."));
            }
            foreach (var sample in samples.Where(sample =>
                         sample.ViewportWidth != 1440 || sample.ViewportHeight != 900))
            {
                violations.Add(new ProductionColdStartViolation(
                    AcceptanceFailureCodes.ProductionColdStartViewportMismatch,
                    scenario,
                    sample.Iteration,
                    $"Expected viewport 1440x900 but observed {sample.ViewportWidth}x{sample.ViewportHeight}."));
            }
            foreach (var sample in samples.Where(sample => sample.Throttle != request.ExpectedThrottle))
            {
                violations.Add(new ProductionColdStartViolation(
                    AcceptanceFailureCodes.ProductionColdStartThrottleMismatch,
                    scenario,
                    sample.Iteration,
                    "Network or CPU throttle profile differed from the frozen run profile."));
            }
            foreach (var sample in samples.Where(sample =>
                         sample.Navigation is null
                         || sample.Requests is null
                         || sample.Requests.Count == 0
                         || !sample.Requests.Any(item =>
                             string.Equals(item.ResourceType, "script", StringComparison.OrdinalIgnoreCase)
                             && item.TransferBytes > 0)))
            {
                violations.Add(new ProductionColdStartViolation(
                    AcceptanceFailureCodes.ProductionColdStartRequestEvidenceMissing,
                    scenario,
                    sample.Iteration,
                    "Navigation timing or transferred script request evidence was missing."));
            }
            foreach (var sample in samples.Where(sample =>
                         !sample.LongTaskObserverSucceeded || sample.LongTasks is null))
            {
                violations.Add(new ProductionColdStartViolation(
                    AcceptanceFailureCodes.ProductionColdStartLongTaskEvidenceMissing,
                    scenario,
                    sample.Iteration,
                    "Long Task observer evidence was missing or failed."));
            }
            if (string.Equals(scenario, ProductionColdStartScenarios.UnauthorizedPersonnel, StringComparison.Ordinal))
            {
                foreach (var sample in samples)
                {
                    if (sample.RequestedTargetCapabilityAssets is null)
                    {
                        violations.Add(new ProductionColdStartViolation(
                            AcceptanceFailureCodes.ProductionColdStartRequestEvidenceMissing,
                            scenario,
                            sample.Iteration,
                            "Target capability request observation was missing."));
                        continue;
                    }
                    var requestedTarget = sample.RequestedTargetCapabilityAssets.FirstOrDefault(requested =>
                        request.TargetCapabilityAssets.Contains(requested, StringComparer.OrdinalIgnoreCase));
                    if (requestedTarget is not null)
                    {
                        violations.Add(new ProductionColdStartViolation(
                            AcceptanceFailureCodes.ProductionColdStartTargetCapabilityRequested,
                            scenario,
                            sample.Iteration,
                            $"Unauthorized route requested target capability asset '{requestedTarget}'."));
                    }
                }
            }
            var interactive = samples.Select(sample => sample.InteractiveMs).Order().ToArray();
            var jsTransfer = samples
                .Select(sample => (sample.Requests ?? [])
                    .Where(item => string.Equals(item.ResourceType, "script", StringComparison.OrdinalIgnoreCase))
                    .Sum(item => item.TransferBytes))
                .Order()
                .ToArray();
            var longTasks = samples
                .Select(sample => (long)Math.Round((sample.LongTasks ?? []).Sum(item => item.DurationMs)))
                .Order()
                .ToArray();
            summaries.Add(scenario, new ProductionColdStartScenarioSummary(
                SampleCount: samples.Length,
                InteractiveP50Ms: NearestRank(interactive, 0.50m),
                InteractiveP95Ms: NearestRank(interactive, 0.95m),
                JsTransferP50Bytes: NearestRank(jsTransfer, 0.50m),
                JsTransferP95Bytes: NearestRank(jsTransfer, 0.95m),
                LongTaskP50Ms: NearestRank(longTasks, 0.50m),
                LongTaskP95Ms: NearestRank(longTasks, 0.95m)));
        }
        if (!string.Equals(request.Cleanup.Status, "completed", StringComparison.Ordinal))
        {
            var cleanupCode = request.Cleanup.FailureCode ?? AcceptanceFailureCodes.CleanupIncomplete;
            violations.Add(new ProductionColdStartViolation(
                cleanupCode,
                null,
                null,
                "Ownership-safe cleanup did not reach the completed state."));
        }
        var evidenceViolationCount = violations.Count;
        foreach (var (scenario, budget) in request.Budgets.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            if (!summaries.TryGetValue(scenario, out var summary))
            {
                violations.Add(new ProductionColdStartViolation(
                    AcceptanceFailureCodes.ProductionColdStartBaselineMissing,
                    scenario,
                    null,
                    "Budget referenced an unknown cold-start scenario."));
                continue;
            }
            if (summary.InteractiveP50Ms > budget.MaxP50Ms)
            {
                violations.Add(new ProductionColdStartViolation(
                    AcceptanceFailureCodes.ProductionColdStartBudgetExceeded,
                    scenario,
                    null,
                    $"p50 {summary.InteractiveP50Ms} ms exceeded {budget.MaxP50Ms} ms."));
            }
            if (summary.InteractiveP95Ms > budget.MaxP95Ms)
            {
                violations.Add(new ProductionColdStartViolation(
                    AcceptanceFailureCodes.ProductionColdStartBudgetExceeded,
                    scenario,
                    null,
                    $"p95 {summary.InteractiveP95Ms} ms exceeded {budget.MaxP95Ms} ms."));
            }
            if (request.Mode is ProductionColdStartMode.Candidate
                && budget.MinimumImprovementRatio is decimal minimumImprovement)
            {
                if (request.BaselineSummaries is null
                    || !request.BaselineSummaries.TryGetValue(scenario, out var baseline)
                    || baseline.InteractiveP50Ms <= 0
                    || baseline.InteractiveP95Ms <= 0)
                {
                    violations.Add(new ProductionColdStartViolation(
                        AcceptanceFailureCodes.ProductionColdStartBaselineMissing,
                        scenario,
                        null,
                        "Candidate relative budget requires a valid frozen baseline summary."));
                    continue;
                }

                var p50Improvement = 1m - (decimal)summary.InteractiveP50Ms / baseline.InteractiveP50Ms;
                var p95Improvement = 1m - (decimal)summary.InteractiveP95Ms / baseline.InteractiveP95Ms;
                if (p50Improvement < minimumImprovement || p95Improvement < minimumImprovement)
                {
                    violations.Add(new ProductionColdStartViolation(
                        AcceptanceFailureCodes.ProductionColdStartBudgetExceeded,
                        scenario,
                        null,
                        $"p50/p95 improvement must both reach {minimumImprovement:P0}; observed {p50Improvement:P1}/{p95Improvement:P1}."));
                }
            }
        }

        var enforcedViolations = request.Mode is ProductionColdStartMode.Candidate
            ? violations
            : violations.Take(evidenceViolationCount).ToList();
        var firstViolation = enforcedViolations.FirstOrDefault();

        return new ProductionColdStartReductionResult(
            SchemaVersion: "1.0",
            Mode: request.Mode,
            Verdict: firstViolation is null ? AcceptanceVerdict.Passed : AcceptanceVerdict.Failed,
            FailureCode: firstViolation?.Code,
            Scenarios: summaries,
            Violations: violations,
            Cleanup: request.Cleanup);
    }

    // 使用 nearest-rank 避免实现或运行环境差异导致分位数插值漂移。
    private static long NearestRank(IReadOnlyList<long> orderedValues, decimal percentile)
    {
        if (orderedValues.Count == 0)
        {
            return 0;
        }

        var rank = Math.Max(1, (int)Math.Ceiling(orderedValues.Count * percentile));
        return orderedValues[rank - 1];
    }
}
