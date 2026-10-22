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
        if (request.TargetCapabilityAssets.Count != 2)
        {
            violations.Add(new ProductionColdStartViolation(
                AcceptanceFailureCodes.ProductionColdStartCapabilityRecoveryFailed,
                ProductionColdStartScenarios.AuthenticatedPersonnel,
                null,
                $"Capability recovery requires exactly two predeclared target assets; observed {request.TargetCapabilityAssets.Count}."));
        }
        else if (request.CapabilityRecovery is null)
        {
            violations.Add(new ProductionColdStartViolation(
                AcceptanceFailureCodes.ProductionColdStartCapabilityRecoveryFailed,
                ProductionColdStartScenarios.AuthenticatedPersonnel,
                null,
                "Capability recovery probe evidence was missing."));
        }
        else if (!IsCompleteCapabilityRecovery(
                     request.TargetCapabilityAssets,
                     request.CapabilityRecovery))
        {
            violations.Add(new ProductionColdStartViolation(
                AcceptanceFailureCodes.ProductionColdStartCapabilityRecoveryFailed,
                ProductionColdStartScenarios.AuthenticatedPersonnel,
                null,
                request.CapabilityRecovery.Diagnostic
                ?? "Capability recovery probe did not prove fail-once, local retry, and registry recovery."));
        }
        else if (request.Samples
                 .Where(static sample => string.Equals(
                     sample.Scenario,
                     ProductionColdStartScenarios.AuthenticatedPersonnel,
                     StringComparison.Ordinal))
                 .Any(sample => sample.RequestedTargetCapabilityAssets?.Any(asset => string.Equals(
                     asset,
                     request.CapabilityRecovery.RecoveryCapabilityAsset,
                     StringComparison.OrdinalIgnoreCase)) is true))
        {
            violations.Add(new ProductionColdStartViolation(
                AcceptanceFailureCodes.ProductionColdStartCapabilityRecoveryFailed,
                ProductionColdStartScenarios.AuthenticatedPersonnel,
                null,
                "The normal Personnel sample requested the recovery capability asset."));
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
        if (request.Mode is ProductionColdStartMode.Candidate && request.BaselineSummaries is not null)
        {
            foreach (var (scenario, summary) in summaries.ToArray())
            {
                if (!request.BaselineSummaries.TryGetValue(scenario, out var baseline)
                    || baseline.InteractiveP50Ms <= 0
                    || baseline.InteractiveP95Ms <= 0)
                {
                    continue;
                }

                summaries[scenario] = summary with
                {
                    InteractiveP50ImprovementRatio =
                        1m - (decimal)summary.InteractiveP50Ms / baseline.InteractiveP50Ms,
                    InteractiveP95ImprovementRatio =
                        1m - (decimal)summary.InteractiveP95Ms / baseline.InteractiveP95Ms
                };
            }
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
                && budget.MinimumP50ImprovementRatio is decimal minimumImprovement)
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

                var p50Improvement = summary.InteractiveP50ImprovementRatio!.Value;
                var p95Improvement = summary.InteractiveP95ImprovementRatio!.Value;
                if (p50Improvement < minimumImprovement)
                {
                    // 诊断文本必须与机器区域无关：":P0" 走 CurrentCulture 的
                    // PercentPositivePattern，invariant 下是 "30 %"（带空格），
                    // en-US 下是 "30%"。失败信号不能随装机语言变形。
                    violations.Add(new ProductionColdStartViolation(
                        AcceptanceFailureCodes.ProductionColdStartBudgetExceeded,
                        scenario,
                        null,
                        FormattableString.Invariant(
                            $"p50 improvement must reach {minimumImprovement * 100m:0.#}%; observed p50 {p50Improvement * 100m:0.0}%; p95 {p95Improvement * 100m:0.0}% is report-only.")));
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
            Cleanup: request.Cleanup,
            CapabilityRecovery: request.CapabilityRecovery);
    }

    // 恢复证据只接受 analyzer 预声明的 primary/recovery 两条物理 URL 及完整局部 retry 终态。
    private static bool IsCompleteCapabilityRecovery(
        IReadOnlyList<string> targetCapabilityAssets,
        ProductionColdStartCapabilityRecoveryProbe probe)
    {
        var normalizedTargets = targetCapabilityAssets
            .Select(static asset => asset.Replace('\\', '/').TrimStart('/'))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var normalizedTarget = probe.TargetCapabilityAsset?.Replace('\\', '/').TrimStart('/');
        var normalizedRecovery = probe.RecoveryCapabilityAsset?.Replace('\\', '/').TrimStart('/');
        var normalizedFailurePath = probe.FailedRequestPath?.Replace('\\', '/');
        var normalizedRecoveryPath = probe.RecoveryRequestPath?.Replace('\\', '/');
        return string.Equals(probe.SchemaVersion, "1.0", StringComparison.Ordinal)
               && !probe.ContainsSecrets
               && normalizedTarget is not null
               && normalizedRecovery is not null
               && !string.Equals(normalizedTarget, normalizedRecovery, StringComparison.OrdinalIgnoreCase)
               && normalizedTargets.SetEquals([normalizedTarget, normalizedRecovery])
               && normalizedFailurePath?.EndsWith("/" + normalizedTarget, StringComparison.OrdinalIgnoreCase) is true
               && normalizedRecoveryPath?.EndsWith("/" + normalizedRecovery, StringComparison.OrdinalIgnoreCase) is true
               && probe.TargetRequestCount == 2
               && probe.FirstRequestAborted
               && probe.SecondRequestStatus is >= 200 and < 400
               && string.Equals(probe.ErrorCode, "ROUTE_CAPABILITY_LOAD_FAILED", StringComparison.Ordinal)
               && probe.RetryRequested
               && string.Equals(probe.RetryResult, "recovered", StringComparison.Ordinal)
               && probe.FreshAuthorizedContext
               && probe.CacheCleared
               && probe.ServiceWorkersBlocked
               && probe.ShellRemainedInteractive
               && probe.PersonnelRecovered
               && probe.RegistryNavigationAvailable
               && probe.Diagnostic is null;
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
