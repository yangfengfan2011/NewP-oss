using ICP.Platform.Acceptance;
using Xunit;

namespace ICP.Platform.Acceptance.Tests;

/// <summary>
/// 收尾顺序此前在三个 runner 里各写一份，且只有真跑一次完整验收才会被执行到——
/// 也就是从来没被验证过。收口到 AcceptanceRunner 之后它有了可替身的接缝，
/// 这里用假 runner 在内存里钉住四条不变量：取消原样传播、清理一定发生、
/// 场景失败优先于清理失败、失败诊断带场景名且已脱敏。
/// </summary>
public sealed class AcceptanceRunnerLifecycleTests : IDisposable
{
    private const string Scenario = "fake.scenario";

    private readonly string _runDirectory = Path.Combine(
        Path.GetTempPath(),
        $"newp-runner-lifecycle-{Guid.NewGuid():N}");

    public AcceptanceRunnerLifecycleTests() => Directory.CreateDirectory(_runDirectory);

    public void Dispose()
    {
        if (Directory.Exists(_runDirectory))
        {
            Directory.Delete(_runDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_WhenEveryScenarioPasses_RunsTeardownAndReturns()
    {
        var runner = new FakeRunner(_runDirectory, (_, _) => Task.CompletedTask);

        await runner.RunAsync(CancellationToken.None);

        Assert.True(runner.TeardownRan);
    }

    // 取消必须以 OperationCanceledException 原样到达上层：一旦被重建成
    // InvalidOperationException，一次主动中断就会被读成一次断言失败。
    [Fact]
    public async Task RunAsync_WhenScenarioIsCancelled_PropagatesCancellation()
    {
        var runner = new FakeRunner(_runDirectory, (_, _) => throw new OperationCanceledException());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => runner.RunAsync(CancellationToken.None));
        Assert.True(runner.TeardownRan);
    }

    // 取消恰恰是最需要归还一次性 catalog 与 dev supervisor 的时刻。
    // 收尾若沿用已取消的 token，一次取消就会变成一地残留资源。
    [Fact]
    public async Task RunAsync_WhenTokenIsCancelled_StillTearsDownWithAnUncancelledToken()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var runner = new FakeRunner(_runDirectory, (_, token) => Task.FromCanceled(token));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => runner.RunAsync(cancellation.Token));

        Assert.True(runner.TeardownRan);
        Assert.False(runner.TeardownToken.IsCancellationRequested);
    }

    [Fact]
    public async Task RunAsync_WhenScenarioFails_NamesTheActiveScenarioAndRedactsSecrets()
    {
        var runner = new FakeRunner(
            _runDirectory,
            (self, token) => self.EnterScenario(Scenario, _ =>
            {
                self.Register("sup3r-secret-value");
                throw new InvalidOperationException("connect failed for sup3r-secret-value");
            }, token));

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => runner.RunAsync(CancellationToken.None));

        Assert.Contains(Scenario, failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("sup3r-secret-value", failure.Message, StringComparison.Ordinal);
        Assert.Contains("[REDACTED]", failure.Message, StringComparison.Ordinal);
    }

    // 清理失败比场景失败泛得多：两者同时发生时报清理，会把真正的定位信息藏起来。
    [Fact]
    public async Task RunAsync_WhenScenarioAndCleanupBothFail_ReportsTheScenarioFailure()
    {
        var runner = new FakeRunner(
            _runDirectory,
            (_, _) => throw new InvalidOperationException("scenario blew up"),
            new AcceptanceCleanupResult("failed", AcceptanceFailureCodes.CleanupFailed, "catalog still there"));

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => runner.RunAsync(CancellationToken.None));

        Assert.Contains("scenario blew up", failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("catalog still there", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_WhenOnlyCleanupFails_ReportsTheCleanupDiagnostic()
    {
        var runner = new FakeRunner(
            _runDirectory,
            (_, _) => Task.CompletedTask,
            new AcceptanceCleanupResult("failed", AcceptanceFailureCodes.CleanupFailed, "catalog still there"));

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => runner.RunAsync(CancellationToken.None));

        Assert.Contains("catalog still there", failure.Message, StringComparison.Ordinal);
    }

    // 脱敏是正向保护，证据扫描是反向确认：即便场景全过，产物里出现登记过的凭据也必须报。
    [Fact]
    public async Task RunAsync_WhenEvidenceStillHoldsARegisteredSecret_Fails()
    {
        var runner = new FakeRunner(_runDirectory, async (self, token) =>
        {
            self.Register("sup3r-secret-value");
            await File.WriteAllTextAsync(
                Path.Combine(_runDirectory, "evidence.json"),
                "{\"password\":\"sup3r-secret-value\"}",
                token);
        });

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => runner.RunAsync(CancellationToken.None));

        Assert.Contains("凭据片段", failure.Message, StringComparison.Ordinal);
    }

    private sealed class FakeRunner : AcceptanceRunner
    {
        private readonly Func<FakeRunner, CancellationToken, Task> _scenarios;
        private readonly AcceptanceCleanupResult _cleanup;

        public FakeRunner(
            string runDirectory,
            Func<FakeRunner, CancellationToken, Task> scenarios,
            AcceptanceCleanupResult? cleanup = null)
            : base(runDirectory, runDirectory, "fake-run")
        {
            _scenarios = scenarios;
            _cleanup = cleanup ?? new AcceptanceCleanupResult("completed", null);
        }

        public bool TeardownRan { get; private set; }

        public CancellationToken TeardownToken { get; private set; }

        public Task EnterScenario(string scenario, Func<CancellationToken, Task> body, CancellationToken token) =>
            RunScenarioAsync(scenario, body, token);

        public void Register(string secret) => AddConnectionSecrets(secret, null);

        protected override Task RunScenariosAsync(CancellationToken cancellationToken) =>
            _scenarios(this, cancellationToken);

        protected override Task<AcceptanceCleanupResult> TeardownAsync(CancellationToken cancellationToken)
        {
            TeardownRan = true;
            TeardownToken = cancellationToken;
            return Task.FromResult(_cleanup);
        }
    }
}
