using System.Diagnostics;
using System.Text.Json;
using Xunit;

namespace ICP.Platform.Acceptance.Tests;

public class BrowserRequestDuplicateGuardTests
{
    [Fact]
    public void Observe_NormalizesQueryAndJsonObjectOrderIntoSameFingerprint()
    {
        var guard = new BrowserRequestDuplicateGuard(
            "GET",
            "/api/organization/persons",
            []);

        Assert.True(guard.Observe(
            "GET",
            "https://example.test/api/organization/persons?name=Alice%20Smith&role=admin",
            "{\"profile\":{\"last\":\"Smith\",\"first\":\"Alice\"},\"enabled\":true}"));
        Assert.True(guard.Observe(
            "GET",
            "https://example.test/api/organization/persons?role=admin&name=Alice+Smith",
            "{\"enabled\":true,\"profile\":{\"first\":\"Alice\",\"last\":\"Smith\"}}"));

        var report = guard.CreateReport();

        Assert.Equal("failed-duplicate", report.Decision);
        Assert.Equal("BROWSER_REQUEST_DUPLICATE_GUARD_DUPLICATE", report.Diagnostic);
        var exception = Assert.Throws<InvalidOperationException>(report.EnsurePassed);
        Assert.Equal("BROWSER_REQUEST_DUPLICATE_GUARD_DUPLICATE", exception.Message);
        var fingerprint = Assert.Single(report.Fingerprints);
        Assert.Equal(2, fingerprint.PhysicalCount);
        Assert.Equal(1, fingerprint.DuplicateCount);
    }

    [Fact]
    public void CreateReport_WithNoMatchingRequestFailsClosedWithStableDiagnostic()
    {
        var guard = new BrowserRequestDuplicateGuard(
            "GET",
            "/api/organization/persons",
            []);

        var report = guard.CreateReport();

        Assert.Equal("failed-zero-match", report.Decision);
        Assert.Equal("BROWSER_REQUEST_DUPLICATE_GUARD_ZERO_MATCH", report.Diagnostic);
        var exception = Assert.Throws<InvalidOperationException>(report.EnsurePassed);
        Assert.Equal("BROWSER_REQUEST_DUPLICATE_GUARD_ZERO_MATCH", exception.Message);
    }

    [Fact]
    public async Task WaitForQuietPeriodAsync_ReturnsAfterTheLastMatchingRequestBecomesQuiet()
    {
        var guard = new BrowserRequestDuplicateGuard(
            "GET",
            "/api/organization/persons",
            []);
        guard.Observe(
            "GET",
            "https://example.test/api/organization/persons?pageIndex=1",
            null);

        var report = await guard.WaitForQuietPeriodAsync(
            TimeSpan.FromMilliseconds(15),
            TimeSpan.FromSeconds(1),
            CancellationToken.None);

        Assert.Equal("passed", report.Decision);
    }

    [Fact]
    public async Task WaitForQuietPeriodAsync_MatchingRequestResetsTheQuietWindow()
    {
        var guard = new BrowserRequestDuplicateGuard(
            "GET",
            "/api/organization/persons",
            []);
        guard.Observe(
            "GET",
            "https://example.test/api/organization/persons?pageIndex=1",
            null);
        var elapsed = Stopwatch.StartNew();
        var reportTask = guard.WaitForQuietPeriodAsync(
            TimeSpan.FromMilliseconds(200),
            TimeSpan.FromSeconds(2),
            CancellationToken.None);

        await Task.Delay(50);
        guard.Observe(
            "GET",
            "https://example.test/api/organization/persons?pageIndex=2",
            null);
        var report = await reportTask;

        Assert.True(elapsed.Elapsed >= TimeSpan.FromMilliseconds(225));
        Assert.Equal("passed", report.Decision);
        Assert.Equal(2, report.Fingerprints.Count);
    }

    [Fact]
    public void Observe_DifferentNormalizedUrlOrBodyProducesDifferentFingerprints()
    {
        var guard = CreateGuard();

        guard.Observe("GET", "https://example.test/api/organization/persons?page=1", "plain-a");
        guard.Observe("GET", "https://example.test/api/organization/persons?page=2", "plain-a");
        guard.Observe("GET", "https://example.test/api/organization/persons?page=1", "plain-b");

        var report = guard.CreateReport();

        Assert.Equal("passed", report.Decision);
        Assert.Equal(3, report.Fingerprints.Count);
    }

    [Fact]
    public void Observe_JsonArrayOrderRemainsSignificant()
    {
        var guard = CreateGuard();

        guard.Observe("GET", "https://example.test/api/organization/persons", "{\"ids\":[1,2]}");
        guard.Observe("GET", "https://example.test/api/organization/persons", "{\"ids\":[2,1]}");

        var report = guard.CreateReport();

        Assert.Equal("passed", report.Decision);
        Assert.Equal(2, report.Fingerprints.Count);
    }

    [Fact]
    public void Observe_SecondRequestStartCountsAsDuplicateEvenWithoutCompletionObservation()
    {
        var guard = CreateGuard();
        const string url = "https://example.test/api/organization/persons?page=1";

        guard.Observe("GET", url, null);
        guard.Observe("GET", url, null);

        var report = guard.CreateReport();

        Assert.Equal("failed-duplicate", report.Decision);
        Assert.Equal(2, Assert.Single(report.Fingerprints).PhysicalCount);
    }

    [Fact]
    public void Observe_IgnoresDetailPathAndNonGetMethod()
    {
        var guard = CreateGuard();

        Assert.False(guard.Observe(
            "POST",
            "https://example.test/api/organization/persons",
            "{\"name\":\"write\"}"));
        Assert.False(guard.Observe(
            "GET",
            "https://example.test/api/organization/persons/person-1",
            null));

        Assert.Equal("failed-zero-match", guard.CreateReport().Decision);
    }

    [Fact]
    public void CreateReport_DoesNotExposeRawQueryOrBodySecrets()
    {
        var guard = CreateGuard();
        guard.Observe(
            "GET",
            "https://example.test/api/organization/persons?access_token=query-secret",
            "{\"password\":\"body-secret\"}");

        var json = JsonSerializer.Serialize(guard.CreateReport());

        Assert.DoesNotContain("query-secret", json, StringComparison.Ordinal);
        Assert.DoesNotContain("body-secret", json, StringComparison.Ordinal);
        Assert.DoesNotContain("access_token", json, StringComparison.Ordinal);
        Assert.DoesNotContain("password", json, StringComparison.Ordinal);
        Assert.Contains("sha256:", json, StringComparison.Ordinal);
        Assert.Contains("/api/organization/persons", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Observe_PreservesRepeatedQueryParametersDuringNormalization()
    {
        var guard = CreateGuard();

        guard.Observe(
            "GET",
            "https://example.test/api/organization/persons?tag=b&tag=a",
            null);
        guard.Observe(
            "GET",
            "https://example.test/api/organization/persons?tag=a",
            null);

        Assert.Equal(2, guard.CreateReport().Fingerprints.Count);
    }

    [Fact]
    public void Observe_OnlyExcludesQueryParametersNamedByTheConstructor()
    {
        var guard = new BrowserRequestDuplicateGuard(
            "GET",
            "/api/organization/persons",
            ["correlation"]);

        guard.Observe(
            "GET",
            "https://example.test/api/organization/persons?page=1&correlation=first",
            null);
        guard.Observe(
            "GET",
            "https://example.test/api/organization/persons?correlation=second&page=1",
            null);

        Assert.Equal("failed-duplicate", guard.CreateReport().Decision);
    }

    [Fact]
    public void Observe_EmptyBodyHasOneStableNormalization()
    {
        var guard = CreateGuard();
        const string url = "https://example.test/api/organization/persons";

        guard.Observe("GET", url, null);
        guard.Observe("GET", url, string.Empty);

        Assert.Equal("failed-duplicate", guard.CreateReport().Decision);
    }

    [Fact]
    public void Observe_IsSafeForConcurrentRequestEvents()
    {
        var guard = CreateGuard();
        const string url = "https://example.test/api/organization/persons?page=1";

        Parallel.For(0, 32, _ => guard.Observe("GET", url, null));

        var fingerprint = Assert.Single(guard.CreateReport().Fingerprints);
        Assert.Equal(32, fingerprint.PhysicalCount);
        Assert.Equal(31, fingerprint.DuplicateCount);
    }

    [Fact]
    public async Task WaitForQuietPeriodAsync_PropagatesCancellation()
    {
        var guard = CreateGuard();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            guard.WaitForQuietPeriodAsync(
                TimeSpan.FromMilliseconds(20),
                TimeSpan.FromSeconds(1),
                cancellation.Token));
    }

    private static BrowserRequestDuplicateGuard CreateGuard() =>
        new("GET", "/api/organization/persons", []);
}
