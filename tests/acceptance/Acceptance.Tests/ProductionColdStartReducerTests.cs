using ICP.Platform.Acceptance;
using Xunit;

namespace ICP.Platform.Acceptance.Tests;

public class ProductionColdStartReducerTests
{
    private const string TargetAsset = "assets/personnel.primary.js";
    private const string RecoveryAsset = "assets/personnel.recovery.js";

    [Fact]
    public void Reduce_QualificationUsesOneCompleteSamplePerScenarioWithoutBecomingAFormalBudgetSample()
    {
        var samples = ProductionColdStartScenarios.All
            .Select(scenario => CompleteSample(scenario, 1, 1000))
            .ToArray();

        var result = ProductionColdStartReducer.Reduce(new ProductionColdStartReductionRequest(
            Mode: ProductionColdStartMode.Qualification,
            ExpectedSamplesPerScenario: 1,
            ExpectedThrottle: ProductionColdStartThrottleProfile.Required,
            Samples: samples,
            TargetCapabilityAssets: [TargetAsset, RecoveryAsset],
            Budgets: ProductionColdStartScenarios.All.ToDictionary(
                static scenario => scenario,
                static _ => new ProductionColdStartBudget(1, 1),
                StringComparer.Ordinal),
            BaselineSummaries: null,
            Cleanup: new AcceptanceCleanupResult("completed", null),
            CapabilityRecovery: CompleteRecoveryProbe()));

        Assert.Equal(AcceptanceVerdict.Passed, result.Verdict);
        Assert.Null(result.FailureCode);
        Assert.All(result.Scenarios, pair => Assert.Equal(1, pair.Value.SampleCount));
        Assert.Contains(result.Violations, item =>
            item.Code == AcceptanceFailureCodes.ProductionColdStartBudgetExceeded);
    }

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
            TargetCapabilityAssets: [TargetAsset, RecoveryAsset],
            Budgets: new Dictionary<string, ProductionColdStartBudget>(StringComparer.Ordinal),
            BaselineSummaries: null,
            Cleanup: new AcceptanceCleanupResult("completed", null),
            CapabilityRecovery: CompleteRecoveryProbe()));

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
            TargetCapabilityAssets: [TargetAsset, RecoveryAsset],
            Budgets: new Dictionary<string, ProductionColdStartBudget>(StringComparer.Ordinal),
            BaselineSummaries: null,
            Cleanup: new AcceptanceCleanupResult("completed", null),
            CapabilityRecovery: CompleteRecoveryProbe()));

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
            TargetCapabilityAssets: [TargetAsset, RecoveryAsset],
            Budgets: new Dictionary<string, ProductionColdStartBudget>(StringComparer.Ordinal),
            BaselineSummaries: null,
            Cleanup: new AcceptanceCleanupResult("completed", null),
            CapabilityRecovery: CompleteRecoveryProbe()));

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
            TargetCapabilityAssets: [TargetAsset, RecoveryAsset],
            Budgets: new Dictionary<string, ProductionColdStartBudget>(StringComparer.Ordinal),
            BaselineSummaries: null,
            Cleanup: new AcceptanceCleanupResult("completed", null),
            CapabilityRecovery: CompleteRecoveryProbe()));

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
            TargetCapabilityAssets: [TargetAsset, RecoveryAsset],
            Budgets: new Dictionary<string, ProductionColdStartBudget>(StringComparer.Ordinal),
            BaselineSummaries: null,
            Cleanup: new AcceptanceCleanupResult("completed", null),
            CapabilityRecovery: CompleteRecoveryProbe()));

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
    public void Reduce_WhenCandidateHomeP50MeetsRelativeBudget_P95ComparisonRemainsReportOnly()
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
                [ProductionColdStartScenarios.AuthenticatedHome] = new(2200, 3000, 0.30m)
            },
            BaselineSummaries = new Dictionary<string, ProductionColdStartScenarioSummary>(StringComparer.Ordinal)
            {
                [ProductionColdStartScenarios.AuthenticatedHome] = new(
                    SampleCount: 20,
                    InteractiveP50Ms: 1500,
                    InteractiveP95Ms: 2200,
                    JsTransferP50Bytes: 8192,
                    JsTransferP95Bytes: 8192,
                    LongTaskP50Ms: 0,
                    LongTaskP95Ms: 0)
            }
        };

        var result = ProductionColdStartReducer.Reduce(request);

        Assert.Equal(AcceptanceVerdict.Passed, result.Verdict);
        var summary = result.Scenarios[ProductionColdStartScenarios.AuthenticatedHome];
        Assert.Equal(1m - 1000m / 1500m, summary.InteractiveP50ImprovementRatio);
        Assert.Equal(1m - 1900m / 2200m, summary.InteractiveP95ImprovementRatio);
        Assert.DoesNotContain(
            result.Violations,
            item => item.Scenario == ProductionColdStartScenarios.AuthenticatedHome);
    }

    [Fact]
    public void Reduce_WhenCandidateHasNoRelativeBudget_ReportsComparisonWithoutEnforcingIt()
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
                [ProductionColdStartScenarios.AuthenticatedPersonnel] = new(2200, 3000)
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

        Assert.Equal(AcceptanceVerdict.Passed, result.Verdict);
        var summary = result.Scenarios[ProductionColdStartScenarios.AuthenticatedPersonnel];
        Assert.Equal(1m - 1000m / 1200m, summary.InteractiveP50ImprovementRatio);
        Assert.Equal(1m - 1900m / 2200m, summary.InteractiveP95ImprovementRatio);
        Assert.DoesNotContain(
            result.Violations,
            item => item.Scenario == ProductionColdStartScenarios.AuthenticatedPersonnel);
    }

    [Fact]
    public void Reduce_WhenUnauthorizedRouteRequestsTargetCapability_FailsInBaselineAndCandidateModes()
    {
        var targetAsset = TargetAsset;
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
        var request = RequestFor(samples) with { TargetCapabilityAssets = [targetAsset, RecoveryAsset] };

        var baseline = ProductionColdStartReducer.Reduce(request);
        var candidate = ProductionColdStartReducer.Reduce(request with { Mode = ProductionColdStartMode.Candidate });

        Assert.Equal(AcceptanceFailureCodes.ProductionColdStartTargetCapabilityRequested, baseline.FailureCode);
        Assert.Equal(AcceptanceFailureCodes.ProductionColdStartTargetCapabilityRequested, candidate.FailureCode);
    }

    [Fact]
    public void Reduce_WhenCapabilityRecoveryProbeIsMissing_FailsClosed()
    {
        var samples = ProductionColdStartScenarios.All
            .SelectMany(scenario => Enumerable.Range(1, 20)
                .Select(iteration => CompleteSample(scenario, iteration, iteration * 100L)))
            .ToArray();
        var request = RequestFor(samples) with
        {
            TargetCapabilityAssets = [TargetAsset, RecoveryAsset],
            CapabilityRecovery = null
        };

        var result = ProductionColdStartReducer.Reduce(request);

        Assert.Equal(AcceptanceVerdict.Failed, result.Verdict);
        Assert.Equal(AcceptanceFailureCodes.ProductionColdStartCapabilityRecoveryFailed, result.FailureCode);
    }

    [Fact]
    public void Reduce_WhenNormalPersonnelSampleRequestsRecoveryAsset_FailsClosed()
    {
        var samples = ProductionColdStartScenarios.All
            .SelectMany(scenario => Enumerable.Range(1, 20)
                .Select(iteration => CompleteSample(scenario, iteration, iteration * 100L)))
            .Select(sample => sample is
            {
                Scenario: ProductionColdStartScenarios.AuthenticatedPersonnel,
                Iteration: 1
            }
                ? sample with { RequestedTargetCapabilityAssets = [TargetAsset, RecoveryAsset] }
                : sample)
            .ToArray();

        var result = ProductionColdStartReducer.Reduce(RequestFor(samples));

        Assert.Equal(AcceptanceVerdict.Failed, result.Verdict);
        Assert.Equal(AcceptanceFailureCodes.ProductionColdStartCapabilityRecoveryFailed, result.FailureCode);
    }

    [Fact]
    public void Reduce_WhenCapabilityTargetAssetIsNotUnique_FailsClosed()
    {
        var samples = ProductionColdStartScenarios.All
            .SelectMany(scenario => Enumerable.Range(1, 20)
                .Select(iteration => CompleteSample(scenario, iteration, iteration * 100L)))
            .ToArray();

        var withoutTarget = ProductionColdStartReducer.Reduce(RequestFor(samples) with
        {
            TargetCapabilityAssets = [],
            CapabilityRecovery = CompleteRecoveryProbe()
        });
        var withMultipleTargets = ProductionColdStartReducer.Reduce(RequestFor(samples) with
        {
            TargetCapabilityAssets = [TargetAsset, RecoveryAsset, "assets/personnel-other.js"],
            CapabilityRecovery = CompleteRecoveryProbe()
        });

        Assert.Equal(
            AcceptanceFailureCodes.ProductionColdStartCapabilityRecoveryFailed,
            withoutTarget.FailureCode);
        Assert.Equal(
            AcceptanceFailureCodes.ProductionColdStartCapabilityRecoveryFailed,
            withMultipleTargets.FailureCode);
    }

    [Fact]
    public void Reduce_WhenCapabilityRecoveryEvidenceIsIncomplete_FailsClosed()
    {
        var samples = ProductionColdStartScenarios.All
            .SelectMany(scenario => Enumerable.Range(1, 20)
                .Select(iteration => CompleteSample(scenario, iteration, iteration * 100L)))
            .ToArray();
        var complete = CompleteRecoveryProbe();
        var incompleteProbes = new[]
        {
            complete with { SchemaVersion = "0.9" },
            complete with { ContainsSecrets = true },
            complete with { TargetCapabilityAsset = "assets/other.js" },
            complete with { RecoveryCapabilityAsset = "assets/other.recovery.js" },
            complete with { FailedRequestPath = "/assets/other.js" },
            complete with { RecoveryRequestPath = "/assets/other.recovery.js" },
            complete with { TargetRequestCount = 1 },
            complete with { FirstRequestAborted = false },
            complete with { SecondRequestStatus = 500 },
            complete with { ErrorCode = "ROUTE_CAPABILITY_RENDER_FAILED" },
            complete with { RetryRequested = false },
            complete with { RetryResult = "failed" },
            complete with { FreshAuthorizedContext = false },
            complete with { CacheCleared = false },
            complete with { ServiceWorkersBlocked = false },
            complete with { ShellRemainedInteractive = false },
            complete with { PersonnelRecovered = false },
            complete with { RegistryNavigationAvailable = false }
        };

        foreach (var probe in incompleteProbes)
        {
            var result = ProductionColdStartReducer.Reduce(RequestFor(samples) with
            {
                CapabilityRecovery = probe
            });

            Assert.Equal(
                AcceptanceFailureCodes.ProductionColdStartCapabilityRecoveryFailed,
                result.FailureCode);
        }
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

    private static ProductionColdStartCapabilityRecoveryProbe CompleteRecoveryProbe() =>
        new(
            SchemaVersion: "1.0",
            ContainsSecrets: false,
            TargetCapabilityAsset: TargetAsset,
            RecoveryCapabilityAsset: RecoveryAsset,
            FailedRequestPath: "/" + TargetAsset,
            RecoveryRequestPath: "/" + RecoveryAsset,
            TargetRequestCount: 2,
            FirstRequestAborted: true,
            SecondRequestStatus: 200,
            ErrorCode: "ROUTE_CAPABILITY_LOAD_FAILED",
            RetryRequested: true,
            RetryResult: "recovered",
            FreshAuthorizedContext: true,
            CacheCleared: true,
            ServiceWorkersBlocked: true,
            ShellRemainedInteractive: true,
            PersonnelRecovered: true,
            RegistryNavigationAvailable: true,
            Diagnostic: null);

    private static ProductionColdStartReductionRequest RequestFor(IReadOnlyList<ProductionColdStartSample> samples) =>
        new(
            Mode: ProductionColdStartMode.Baseline,
            ExpectedSamplesPerScenario: 20,
            ExpectedThrottle: ProductionColdStartThrottleProfile.Required,
            Samples: samples,
            TargetCapabilityAssets: [TargetAsset, RecoveryAsset],
            Budgets: new Dictionary<string, ProductionColdStartBudget>(StringComparer.Ordinal),
            BaselineSummaries: null,
            Cleanup: new AcceptanceCleanupResult("completed", null),
            CapabilityRecovery: CompleteRecoveryProbe());
}
