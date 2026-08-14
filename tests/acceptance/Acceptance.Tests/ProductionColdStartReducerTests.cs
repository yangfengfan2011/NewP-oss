using ICP.Platform.Acceptance;
using Xunit;

namespace ICP.Platform.Acceptance.Tests;

public class ProductionColdStartReducerTests
{
    [Fact]
    public void Reduce_WithTwentyCompleteSamplesPerScenario_ComputesNearestRankPercentiles()
    {
        var samples = ProductionColdStartScenarios.All
            .SelectMany(scenario => Enumerable.Range(1, 20)
                .Select(iteration => CompleteSample(scenario, iteration, iteration * 100L)))
            .ToArray();

        var result = ProductionColdStartReducer.Reduce(new ProductionColdStartReductionRequest(
            Mode: ProductionColdStartMode.Baseline,
            ExpectedSamplesPerScenario: 20,
            ExpectedThrottle: ProductionColdStartThrottleProfile.Required,
            Samples: samples,
            TargetCapabilityAssets: [],
            Budgets: new Dictionary<string, ProductionColdStartBudget>(StringComparer.Ordinal),
            BaselineSummaries: null,
            Cleanup: new AcceptanceCleanupResult("completed", null)));

        Assert.Equal(AcceptanceVerdict.Passed, result.Verdict);
        Assert.Null(result.FailureCode);
        foreach (var scenario in ProductionColdStartScenarios.All)
        {
            var summary = result.Scenarios[scenario];
            Assert.Equal(20, summary.SampleCount);
            Assert.Equal(1000, summary.InteractiveP50Ms);
            Assert.Equal(1900, summary.InteractiveP95Ms);
            Assert.Equal(4096, summary.JsTransferP50Bytes);
            Assert.Equal(4096, summary.JsTransferP95Bytes);
        }
    }

    [Fact]
    public void Reduce_WhenAScenarioHasFewerThanTwentySamples_FailsClosed()
    {
        var samples = ProductionColdStartScenarios.All
            .SelectMany(scenario => Enumerable.Range(1, 20)
                .Select(iteration => CompleteSample(scenario, iteration, iteration * 100L)))
            .Where(sample => sample is not
            {
                Scenario: ProductionColdStartScenarios.AnonymousLogin,
                Iteration: 20
            })
            .ToArray();

        var result = ProductionColdStartReducer.Reduce(new ProductionColdStartReductionRequest(
            Mode: ProductionColdStartMode.Baseline,
            ExpectedSamplesPerScenario: 20,
            ExpectedThrottle: ProductionColdStartThrottleProfile.Required,
            Samples: samples,
            TargetCapabilityAssets: [],
            Budgets: new Dictionary<string, ProductionColdStartBudget>(StringComparer.Ordinal),
            BaselineSummaries: null,
            Cleanup: new AcceptanceCleanupResult("completed", null)));

        Assert.Equal(AcceptanceVerdict.Failed, result.Verdict);
        Assert.Equal(AcceptanceFailureCodes.ProductionColdStartSampleCount, result.FailureCode);
        Assert.Contains(result.Violations, item =>
            item.Code == AcceptanceFailureCodes.ProductionColdStartSampleCount
            && item.Scenario == ProductionColdStartScenarios.AnonymousLogin);
    }

    [Fact]
    public void Reduce_WhenAScenarioIsSkipped_FailsClosed()
    {
        var samples = ProductionColdStartScenarios.All
            .SelectMany(scenario => Enumerable.Range(1, 20)
                .Select(iteration => CompleteSample(scenario, iteration, iteration * 100L)))
            .Select(sample => sample is
            {
                Scenario: ProductionColdStartScenarios.AuthenticatedHome,
                Iteration: 7
            }
                ? sample with { Skipped = true, SkipReason = "fixture unavailable" }
                : sample)
            .ToArray();

        var result = ProductionColdStartReducer.Reduce(new ProductionColdStartReductionRequest(
            Mode: ProductionColdStartMode.Baseline,
            ExpectedSamplesPerScenario: 20,
            ExpectedThrottle: ProductionColdStartThrottleProfile.Required,
            Samples: samples,
            TargetCapabilityAssets: [],
            Budgets: new Dictionary<string, ProductionColdStartBudget>(StringComparer.Ordinal),
            BaselineSummaries: null,
            Cleanup: new AcceptanceCleanupResult("completed", null)));

        Assert.Equal(AcceptanceVerdict.Failed, result.Verdict);
        Assert.Equal(AcceptanceFailureCodes.ProductionColdStartScenarioSkipped, result.FailureCode);
        Assert.Contains(result.Violations, item =>
            item.Code == AcceptanceFailureCodes.ProductionColdStartScenarioSkipped
            && item.Scenario == ProductionColdStartScenarios.AuthenticatedHome
            && item.Iteration == 7);
    }

    [Fact]
    public void Reduce_WhenHttpCacheWasNotCleared_FailsClosed()
    {
        var samples = ProductionColdStartScenarios.All
            .SelectMany(scenario => Enumerable.Range(1, 20)
                .Select(iteration => CompleteSample(scenario, iteration, iteration * 100L)))
            .Select(sample => sample is
            {
                Scenario: ProductionColdStartScenarios.AnonymousLogin,
                Iteration: 3
            }
                ? sample with { CacheCleared = false }
                : sample)
            .ToArray();

        var result = ProductionColdStartReducer.Reduce(new ProductionColdStartReductionRequest(
            Mode: ProductionColdStartMode.Baseline,
            ExpectedSamplesPerScenario: 20,
            ExpectedThrottle: ProductionColdStartThrottleProfile.Required,
            Samples: samples,
            TargetCapabilityAssets: [],
            Budgets: new Dictionary<string, ProductionColdStartBudget>(StringComparer.Ordinal),
            BaselineSummaries: null,
            Cleanup: new AcceptanceCleanupResult("completed", null)));

        Assert.Equal(AcceptanceFailureCodes.ProductionColdStartCacheNotCleared, result.FailureCode);
        Assert.Contains(result.Violations, item =>
            item.Code == AcceptanceFailureCodes.ProductionColdStartCacheNotCleared
            && item.Iteration == 3);
    }

    [Fact]
    public void Reduce_WhenThrottleProfileDiffersBetweenSamples_FailsClosed()
    {
        var samples = ProductionColdStartScenarios.All
            .SelectMany(scenario => Enumerable.Range(1, 20)
                .Select(iteration => CompleteSample(scenario, iteration, iteration * 100L)))
            .Select(sample => sample is
            {
                Scenario: ProductionColdStartScenarios.AuthenticatedPersonnel,
                Iteration: 12
            }
                ? sample with
                {
                    Throttle = ProductionColdStartThrottleProfile.Required with { LatencyMs = 41 }
                }
                : sample)
            .ToArray();

        var result = ProductionColdStartReducer.Reduce(new ProductionColdStartReductionRequest(
            Mode: ProductionColdStartMode.Baseline,
            ExpectedSamplesPerScenario: 20,
            ExpectedThrottle: ProductionColdStartThrottleProfile.Required,
            Samples: samples,
            TargetCapabilityAssets: [],
            Budgets: new Dictionary<string, ProductionColdStartBudget>(StringComparer.Ordinal),
            BaselineSummaries: null,
            Cleanup: new AcceptanceCleanupResult("completed", null)));

        Assert.Equal(AcceptanceFailureCodes.ProductionColdStartThrottleMismatch, result.FailureCode);
        Assert.Contains(result.Violations, item =>
            item.Code == AcceptanceFailureCodes.ProductionColdStartThrottleMismatch
            && item.Scenario == ProductionColdStartScenarios.AuthenticatedPersonnel
            && item.Iteration == 12);
    }

    [Fact]
    public void Reduce_WhenRequestOrLongTaskEvidenceIsMissing_FailsClosed()
    {
        var complete = ProductionColdStartScenarios.All
            .SelectMany(scenario => Enumerable.Range(1, 20)
                .Select(iteration => CompleteSample(scenario, iteration, iteration * 100L)))
            .ToArray();
        var missingRequests = complete
            .Select(sample => sample is
            {
                Scenario: ProductionColdStartScenarios.AnonymousLogin,
                Iteration: 5
            }
                ? sample with { Requests = null }
                : sample)
            .ToArray();
        var missingLongTasks = complete
            .Select(sample => sample is
            {
                Scenario: ProductionColdStartScenarios.AnonymousLogin,
                Iteration: 5
            }
                ? sample with { LongTasks = null, LongTaskObserverSucceeded = false }
                : sample)
            .ToArray();

        var requestResult = ProductionColdStartReducer.Reduce(RequestFor(missingRequests));
        var longTaskResult = ProductionColdStartReducer.Reduce(RequestFor(missingLongTasks));

        Assert.Equal(AcceptanceFailureCodes.ProductionColdStartRequestEvidenceMissing, requestResult.FailureCode);
        Assert.Equal(AcceptanceFailureCodes.ProductionColdStartLongTaskEvidenceMissing, longTaskResult.FailureCode);
    }

    [Fact]
    public void Reduce_WhenOwnershipSafeCleanupFails_CannotPass()
    {
        var samples = ProductionColdStartScenarios.All
            .SelectMany(scenario => Enumerable.Range(1, 20)
                .Select(iteration => CompleteSample(scenario, iteration, iteration * 100L)))
            .ToArray();

        var result = ProductionColdStartReducer.Reduce(RequestFor(samples) with
        {
            Cleanup = new AcceptanceCleanupResult("failed", AcceptanceFailureCodes.CleanupFailed)
        });

        Assert.Equal(AcceptanceVerdict.Failed, result.Verdict);
        Assert.Equal(AcceptanceFailureCodes.CleanupFailed, result.FailureCode);
        Assert.Contains(result.Violations, item => item.Code == AcceptanceFailureCodes.CleanupFailed);
    }

    [Fact]
    public void Reduce_WhenServiceWorkersOrViewportDifferFromTheFrozenProfile_FailsClosed()
    {
        var complete = ProductionColdStartScenarios.All
            .SelectMany(scenario => Enumerable.Range(1, 20)
                .Select(iteration => CompleteSample(scenario, iteration, iteration * 100L)))
            .ToArray();
        var serviceWorkerResult = ProductionColdStartReducer.Reduce(RequestFor(complete
            .Select(sample => sample.Iteration == 1
                ? sample with { ServiceWorkersBlocked = false }
                : sample)
            .ToArray()));
        var viewportResult = ProductionColdStartReducer.Reduce(RequestFor(complete
            .Select(sample => sample.Iteration == 1
                ? sample with { ViewportHeight = 901 }
                : sample)
            .ToArray()));

        Assert.Equal(AcceptanceFailureCodes.ProductionColdStartServiceWorkerEnabled, serviceWorkerResult.FailureCode);
        Assert.Equal(AcceptanceFailureCodes.ProductionColdStartViewportMismatch, viewportResult.FailureCode);
    }

    [Fact]
    public void Reduce_RecordsBudgetViolationsForBaselineAndEnforcesThemForCandidate()
    {
        var samples = ProductionColdStartScenarios.All
            .SelectMany(scenario => Enumerable.Range(1, 20)
                .Select(iteration => CompleteSample(scenario, iteration, iteration * 100L)))
            .ToArray();
        var budgets = new Dictionary<string, ProductionColdStartBudget>(StringComparer.Ordinal)
        {
            [ProductionColdStartScenarios.AuthenticatedHome] = new(900, 1800)
        };
        var baselineRequest = RequestFor(samples) with { Budgets = budgets };

        var baseline = ProductionColdStartReducer.Reduce(baselineRequest);
        var candidate = ProductionColdStartReducer.Reduce(baselineRequest with
        {
            Mode = ProductionColdStartMode.Candidate
        });

        Assert.Equal(AcceptanceVerdict.Passed, baseline.Verdict);
        Assert.Contains(baseline.Violations, item =>
            item.Code == AcceptanceFailureCodes.ProductionColdStartBudgetExceeded);
        Assert.Equal(AcceptanceVerdict.Failed, candidate.Verdict);
        Assert.Equal(AcceptanceFailureCodes.ProductionColdStartBudgetExceeded, candidate.FailureCode);
    }

    [Fact]
    public void Reduce_WhenCandidateDoesNotImproveThirtyPercent_FailsRelativeBudget()
    {
        var samples = ProductionColdStartScenarios.All
            .SelectMany(scenario => Enumerable.Range(1, 20)
                .Select(iteration => CompleteSample(scenario, iteration, iteration * 100L)))
            .ToArray();
        var request = RequestFor(samples) with
        {
            Mode = ProductionColdStartMode.Candidate,
            Budgets = new Dictionary<string, ProductionColdStartBudget>(StringComparer.Ordinal)
            {
                [ProductionColdStartScenarios.AuthenticatedPersonnel] = new(2200, 3000, 0.30m)
            },
            BaselineSummaries = new Dictionary<string, ProductionColdStartScenarioSummary>(StringComparer.Ordinal)
            {
                [ProductionColdStartScenarios.AuthenticatedPersonnel] = new(
                    SampleCount: 20,
                    InteractiveP50Ms: 1200,
                    InteractiveP95Ms: 2200,
                    JsTransferP50Bytes: 8192,
                    JsTransferP95Bytes: 8192,
                    LongTaskP50Ms: 0,
                    LongTaskP95Ms: 0)
            }
        };

        var result = ProductionColdStartReducer.Reduce(request);

        Assert.Equal(AcceptanceVerdict.Failed, result.Verdict);
        Assert.Equal(AcceptanceFailureCodes.ProductionColdStartBudgetExceeded, result.FailureCode);
        Assert.Contains(result.Violations, item => item.Diagnostic.Contains("30%", StringComparison.Ordinal));
    }

    [Fact]
    public void Reduce_WhenUnauthorizedRouteRequestsTargetCapability_FailsInBaselineAndCandidateModes()
    {
        var targetAsset = "/assets/personnel.js";
        var samples = ProductionColdStartScenarios.All
            .SelectMany(scenario => Enumerable.Range(1, 20)
                .Select(iteration => CompleteSample(scenario, iteration, iteration * 100L)))
            .Select(sample => sample is
            {
                Scenario: ProductionColdStartScenarios.UnauthorizedPersonnel,
                Iteration: 1
            }
                ? sample with { RequestedTargetCapabilityAssets = [targetAsset] }
                : sample)
            .ToArray();
        var request = RequestFor(samples) with { TargetCapabilityAssets = [targetAsset] };

        var baseline = ProductionColdStartReducer.Reduce(request);
        var candidate = ProductionColdStartReducer.Reduce(request with { Mode = ProductionColdStartMode.Candidate });

        Assert.Equal(AcceptanceFailureCodes.ProductionColdStartTargetCapabilityRequested, baseline.FailureCode);
        Assert.Equal(AcceptanceFailureCodes.ProductionColdStartTargetCapabilityRequested, candidate.FailureCode);
    }

    [Fact]
    public void Reduce_WhenIterationIdentityIsDuplicated_FailsSampleCompleteness()
    {
        var samples = ProductionColdStartScenarios.All
            .SelectMany(scenario => Enumerable.Range(1, 20)
                .Select(iteration => CompleteSample(scenario, iteration, iteration * 100L)))
            .Select(sample => sample is
            {
                Scenario: ProductionColdStartScenarios.AnonymousLogin,
                Iteration: 20
            }
                ? sample with { Iteration = 19 }
                : sample)
            .ToArray();

        var result = ProductionColdStartReducer.Reduce(RequestFor(samples));

        Assert.Equal(AcceptanceFailureCodes.ProductionColdStartSampleCount, result.FailureCode);
    }

    private static ProductionColdStartSample CompleteSample(string scenario, int iteration, long interactiveMs) =>
        new(
            Scenario: scenario,
            Iteration: iteration,
            Skipped: false,
            SkipReason: null,
            CacheCleared: true,
            ServiceWorkersBlocked: true,
            ViewportWidth: 1440,
            ViewportHeight: 900,
            Throttle: ProductionColdStartThrottleProfile.Required,
            InteractiveMs: interactiveMs,
            Navigation: new ProductionColdStartNavigationSample(10, 20, 30),
            Requests:
            [
                new ProductionColdStartRequestSample(
                    "/assets/index.js",
                    "script",
                    200,
                    4096,
                    1,
                    20)
            ],
            LongTasks: [],
            LongTaskObserverSucceeded: true,
            RequestedTargetCapabilityAssets: []);

    private static ProductionColdStartReductionRequest RequestFor(IReadOnlyList<ProductionColdStartSample> samples) =>
        new(
            Mode: ProductionColdStartMode.Baseline,
            ExpectedSamplesPerScenario: 20,
            ExpectedThrottle: ProductionColdStartThrottleProfile.Required,
            Samples: samples,
            TargetCapabilityAssets: [],
            Budgets: new Dictionary<string, ProductionColdStartBudget>(StringComparer.Ordinal),
            BaselineSummaries: null,
            Cleanup: new AcceptanceCleanupResult("completed", null));
}
