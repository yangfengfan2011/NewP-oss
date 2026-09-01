using ICP.Platform.Acceptance;
using Xunit;

namespace ICP.Platform.Acceptance.Tests;

/// <summary>
/// 收尾顺序此前在三个 runner 里各写一份，且只有真跑一次完整验收才会被执行到——
/// 也就是从来没被验证过。收口到 AcceptanceRunner 之后它有了可替身的接缝，
/// 这里在内存里钉住全部不变量：取消原样传播、清理与扫描一定都发生、
/// 收尾自身出错不许顶掉更具体的失败、失败诊断带场景名且已脱敏。
/// </summary>
public sealed class AcceptanceRunnerLifecycleTests : IDisposable
{
    private const string Scenario = "fake.scenario";
    private const string Secret = "sup3r-secret-value";

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

    // ExceptionDispatchInfo 的意义就在这里：抛回去的必须是同一个实例、带原始抛出点，
    // 否则「取消发生在哪」这条信息会在收尾时被抹掉。
    [Fact]
    public async Task RunAsync_WhenScenarioIsCancelled_KeepsTheOriginalInstanceAndStack()
    {
        var original = new OperationCanceledException("cancelled inside the scenario");
        var runner = new FakeRunner(_runDirectory, (_, _) => throw original);

        var thrown = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => runner.RunAsync(CancellationToken.None));

        Assert.Same(original, thrown);
        Assert.Contains(nameof(FakeRunner.RunScenariosAsyncForTest), thrown.StackTrace, StringComparison.Ordinal);
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

    // 扫描同样不能拿已取消的 token：拿到就会在第一个 ReadAllBytesAsync 上抛，
    // 结论从「确认没泄漏」退化成「没能扫」。这里靠植入的凭据是否被查出来分辨。
    [Fact]
    public async Task RunAsync_WhenTokenIsCancelled_StillScansEvidence()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var runner = new FakeRunner(_runDirectory, (self, _) =>
        {
            self.Register(Secret);
            PlantLeakedEvidence();
            return Task.CompletedTask;
        });

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => runner.RunAsync(cancellation.Token));

        Assert.Contains("凭据片段", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_WhenScenarioFails_NamesTheActiveScenarioAndRedactsSecrets()
    {
        var runner = new FakeRunner(
            _runDirectory,
            (self, token) => self.EnterScenario(Scenario, _ =>
            {
                self.Register(Secret);
                throw new InvalidOperationException($"connect failed for {Secret}");
            }, token));

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => runner.RunAsync(CancellationToken.None));

        Assert.Contains(Scenario, failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Secret, failure.Message, StringComparison.Ordinal);
        Assert.Contains("[REDACTED]", failure.Message, StringComparison.Ordinal);
    }

    // 清理失败比场景失败泛得多：两者同时发生时报清理，会把真正的定位信息藏起来。
    [Fact]
    public async Task RunAsync_WhenScenarioAndCleanupBothFail_ReportsTheScenarioFailure()
    {
        var runner = new FakeRunner(
            _runDirectory,
            (_, _) => throw new InvalidOperationException("scenario blew up"),
            () => new AcceptanceCleanupResult("failed", AcceptanceFailureCodes.CleanupFailed, "catalog still there"));

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
            () => new AcceptanceCleanupResult("failed", AcceptanceFailureCodes.CleanupFailed, "catalog still there"));

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => runner.RunAsync(CancellationToken.None));

        Assert.Contains("catalog still there", failure.Message, StringComparison.Ordinal);
    }

    // 脱敏是正向保护，证据扫描是反向确认：即便场景全过，产物里出现登记过的凭据也必须报。
    [Fact]
    public async Task RunAsync_WhenEvidenceStillHoldsARegisteredSecret_Fails()
    {
        var runner = new FakeRunner(_runDirectory, (self, _) =>
        {
            self.Register(Secret);
            PlantLeakedEvidence();
            return Task.CompletedTask;
        });

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => runner.RunAsync(CancellationToken.None));

        Assert.Contains("凭据片段", failure.Message, StringComparison.Ordinal);
    }

    // 泄漏是确凿结论，清理未完成只是状态：两者同时出现时先报泄漏。
    [Fact]
    public async Task RunAsync_WhenSecretLeakedAndCleanupFailed_ReportsTheLeak()
    {
        var runner = new FakeRunner(
            _runDirectory,
            (self, _) =>
            {
                self.Register(Secret);
                PlantLeakedEvidence();
                return Task.CompletedTask;
            },
            () => new AcceptanceCleanupResult("failed", AcceptanceFailureCodes.CleanupFailed, "catalog still there"));

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => runner.RunAsync(CancellationToken.None));

        Assert.Contains("凭据片段", failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("catalog still there", failure.Message, StringComparison.Ordinal);
    }

    // 收尾自身出错也让不出泄漏这条结论：查到的凭据残留是确凿事实，
    // teardown 抛异常只说明资源可能没还干净。
    [Fact]
    public async Task RunAsync_WhenSecretLeakedAndTeardownThrew_ReportsTheLeak()
    {
        var runner = new FakeRunner(
            _runDirectory,
            (self, _) =>
            {
                self.Register(Secret);
                PlantLeakedEvidence();
                return Task.CompletedTask;
            },
            () => throw new IOException("teardown could not reach the catalog"));

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => runner.RunAsync(CancellationToken.None));

        Assert.Contains("凭据片段", failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("teardown could not reach the catalog", failure.Message, StringComparison.Ordinal);
    }

    // teardown 的契约是「失败返回结果而不是抛」，但契约不等于强制。它真抛时
    // 扫描必须照常进行，场景失败也必须照常胜出。
    [Fact]
    public async Task RunAsync_WhenTeardownThrows_StillScansAndReportsTheScenarioFailure()
    {
        var runner = new FakeRunner(
            _runDirectory,
            (self, _) =>
            {
                self.Register(Secret);
                PlantLeakedEvidence();
                throw new InvalidOperationException("scenario blew up");
            },
            () => throw new IOException("teardown could not reach the catalog"));

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => runner.RunAsync(CancellationToken.None));

        Assert.Contains("scenario blew up", failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("teardown could not reach the catalog", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_WhenTeardownThrowsAndNothingElseFailed_ReportsTheTeardownException()
    {
        var runner = new FakeRunner(
            _runDirectory,
            (_, _) => Task.CompletedTask,
            () => throw new IOException("teardown could not reach the catalog"));

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => runner.RunAsync(CancellationToken.None));

        Assert.Contains("teardown could not reach the catalog", failure.Message, StringComparison.Ordinal);
    }

    // 扫描会遍历并读取本轮全部产物，撞上被占用或已消失的文件就抛——这是实测过的
    // 失败形态。它不能顶掉场景失败，否则最能定位的那条信息就换成了一个文件错误。
    [Fact]
    public async Task RunAsync_WhenEvidenceScanThrows_ReportsTheScenarioFailure()
    {
        var runner = new FakeRunner(
            _runDirectory,
            (self, _) =>
            {
                self.Register(Secret);
                throw new InvalidOperationException("scenario blew up");
            },
            () =>
            {
                Directory.Delete(_runDirectory, recursive: true);
                return new AcceptanceCleanupResult("completed", null);
            });

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => runner.RunAsync(CancellationToken.None));

        Assert.Contains("scenario blew up", failure.Message, StringComparison.Ordinal);
    }

    // 场景全过时扫描出错也不能静默：扫不了意味着「无法断言没泄漏」，不是「没泄漏」。
    [Fact]
    public async Task RunAsync_WhenEvidenceScanThrowsAndScenariosPassed_ReportsTheScanFailure()
    {
        var runner = new FakeRunner(
            _runDirectory,
            (self, _) =>
            {
                self.Register(Secret);
                return Task.CompletedTask;
            },
            () =>
            {
                Directory.Delete(_runDirectory, recursive: true);
                return new AcceptanceCleanupResult("completed", null);
            });

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => runner.RunAsync(CancellationToken.None));

        Assert.Contains("无法确认凭据是否残留", failure.Message, StringComparison.Ordinal);
    }

    private void PlantLeakedEvidence() =>
        File.WriteAllText(
            Path.Combine(_runDirectory, "evidence.json"),
            $"{{\"password\":\"{Secret}\"}}");

    private sealed class FakeRunner : AcceptanceRunner
    {
        private readonly Func<FakeRunner, CancellationToken, Task> _scenarios;
        private readonly Func<AcceptanceCleanupResult> _cleanup;

        public FakeRunner(
            string runDirectory,
            Func<FakeRunner, CancellationToken, Task> scenarios,
            Func<AcceptanceCleanupResult>? cleanup = null)
            : base(runDirectory, runDirectory, "fake-run")
        {
            _scenarios = scenarios;
            _cleanup = cleanup ?? (() => new AcceptanceCleanupResult("completed", null));
        }

        public bool TeardownRan { get; private set; }

        public CancellationToken TeardownToken { get; private set; }

        public Task EnterScenario(string scenario, Func<CancellationToken, Task> body, CancellationToken token) =>
            RunScenarioAsync(scenario, body, token);

        public void Register(string secret) => RegisterSecret(secret);

        // 名字被栈断言引用：改名会让「保留原始抛出点」那条测试静默失去意义。
        public Task RunScenariosAsyncForTest(CancellationToken cancellationToken) =>
            _scenarios(this, cancellationToken);

        protected override Task RunScenariosAsync(CancellationToken cancellationToken) =>
            RunScenariosAsyncForTest(cancellationToken);

        protected override Task<AcceptanceCleanupResult> TeardownAsync(CancellationToken cancellationToken)
        {
            TeardownRan = true;
            TeardownToken = cancellationToken;
            return Task.FromResult(_cleanup());
        }
    }
}
