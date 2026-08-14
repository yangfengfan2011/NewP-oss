using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Playwright;

namespace ICP.Platform.Acceptance;

internal sealed record ProductionColdStartBrowserRequest(
    string RepositoryRoot,
    string RunDirectory,
    string RunId,
    string AssetsRoot,
    int HostPort,
    string AdministratorECode,
    string AdministratorPassword,
    string UnauthorizedECode,
    string UnauthorizedPassword,
    int SamplesPerScenario,
    ProductionColdStartThrottleProfile Throttle,
    IReadOnlyList<string> TargetCapabilityAssets,
    string RawSamplesPath);

internal sealed record ProductionColdStartBrowserExecution(
    IReadOnlyList<ProductionColdStartSample> Samples,
    ProductionColdStartCapabilityRecoveryProbe? CapabilityRecovery,
    string ChromiumVersion,
    string ProductionServerIdentity,
    AcceptanceCleanupResult ProductionServerCleanup,
    string? FailureDiagnostic);

internal sealed class ProductionColdStartBrowserRunner
{
    private const string LongTaskInitScript = """
        (() => {
          performance.setResourceTimingBufferSize(2000);
          window.__newpColdStartLongTasks = [];
          window.__newpColdStartLongTaskObserverSucceeded = false;
          try {
            if (!PerformanceObserver.supportedEntryTypes.includes('longtask')) return;
            const observer = new PerformanceObserver((list) => {
              for (const entry of list.getEntries()) {
                window.__newpColdStartLongTasks.push({
                  startTimeMs: entry.startTime,
                  durationMs: entry.duration,
                });
              }
            });
            observer.observe({ type: 'longtask', buffered: true });
            window.__newpColdStartLongTaskObserverSucceeded = true;
          } catch {
            window.__newpColdStartLongTaskObserverSucceeded = false;
          }
        })();
        """;

    private const string ReadTimingScript = """
        () => {
          const navigation = performance.getEntriesByType('navigation')[0];
          return {
            navigation: navigation ? {
              domContentLoadedMs: navigation.domContentLoadedEventEnd,
              loadMs: navigation.loadEventEnd,
              responseEndMs: navigation.responseEnd,
            } : null,
            requests: performance.getEntriesByType('resource').map((entry) => ({
              path: new URL(entry.name, window.location.href).pathname,
              resourceType: entry.initiatorType || 'other',
              status: Number.isFinite(entry.responseStatus) ? entry.responseStatus : 0,
              transferBytes: entry.transferSize || 0,
              startTimeMs: entry.startTime,
              durationMs: entry.duration,
            })),
            longTasks: window.__newpColdStartLongTasks || null,
            longTaskObserverSucceeded: window.__newpColdStartLongTaskObserverSucceeded === true,
          };
        }
        """;

    /// <summary>
    /// 在 owned production 静态服务上采集四个场景的逐次真实 Chromium 观测，并逐样本 checkpoint。
    /// </summary>
    public async Task<ProductionColdStartBrowserExecution> RunAsync(
        ProductionColdStartBrowserRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var samples = new List<ProductionColdStartSample>();
        ProductionShellServerLease? server = null;
        var serverCleanup = new AcceptanceCleanupResult("not-started", AcceptanceFailureCodes.CleanupIncomplete);
        var chromiumVersion = "unavailable";
        var serverIdentity = "unavailable";
        ProductionColdStartCapabilityRecoveryProbe? capabilityRecovery = null;
        string? failureDiagnostic = null;

        try
        {
            server = await ProductionShellServerLease.StartAsync(request, cancellationToken);
            serverIdentity = server.Identity;
            using var playwright = await Playwright.CreateAsync();
            await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = true,
                ExecutablePath = ResolveChromePath()
            });
            chromiumVersion = browser.Version;
            var origin = $"http://127.0.0.1:{server.Port}";
            var administratorStorage = await CaptureStorageStateAsync(
                browser,
                origin,
                request.AdministratorECode,
                request.AdministratorPassword);
            var unauthorizedStorage = await CaptureStorageStateAsync(
                browser,
                origin,
                request.UnauthorizedECode,
                request.UnauthorizedPassword);
            capabilityRecovery = await CaptureCapabilityRecoveryProbeAsync(
                browser,
                origin,
                administratorStorage,
                request,
                cancellationToken);

            foreach (var scenario in ProductionColdStartScenarios.All)
            {
                for (var iteration = 1; iteration <= request.SamplesPerScenario; iteration++)
                {
                    var storageState = scenario switch
                    {
                        ProductionColdStartScenarios.AuthenticatedHome => administratorStorage,
                        ProductionColdStartScenarios.AuthenticatedPersonnel => administratorStorage,
                        ProductionColdStartScenarios.UnauthorizedPersonnel => unauthorizedStorage,
                        _ => null
                    };
                    var sample = await CaptureSampleAsync(
                        browser,
                        origin,
                        scenario,
                        iteration,
                        storageState,
                        request,
                        cancellationToken);
                    samples.Add(sample);
                    await CheckpointAsync(request.RawSamplesPath, samples, cancellationToken);
                }
            }
        }
        catch (Exception exception)
        {
            failureDiagnostic = Redact(
                exception.Message,
                request.AdministratorPassword,
                request.UnauthorizedPassword);
        }
        finally
        {
            if (server is not null)
            {
                serverCleanup = await server.StopAsync(CancellationToken.None);
                await server.DisposeAsync();
            }
        }

        return new ProductionColdStartBrowserExecution(
            samples,
            capabilityRecovery,
            chromiumVersion,
            serverIdentity,
            serverCleanup,
            failureDiagnostic);
    }

    // 独立于性能样本验证 fail-once 后的局部错误、显式 retry、Personnel 恢复与 registry 导航存活。
    private static async Task<ProductionColdStartCapabilityRecoveryProbe> CaptureCapabilityRecoveryProbeAsync(
        IBrowser browser,
        string origin,
        string administratorStorage,
        ProductionColdStartBrowserRequest request,
        CancellationToken cancellationToken)
    {
        var primaryAssets = request.TargetCapabilityAssets
            .Where(static asset => asset.Contains(".primary-", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var recoveryAssets = request.TargetCapabilityAssets
            .Where(static asset => asset.Contains(".recovery-", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var targetAsset = primaryAssets.Length == 1 ? primaryAssets[0] : null;
        var recoveryAsset = recoveryAssets.Length == 1 ? recoveryAssets[0] : null;
        string? failedRequestPath = null;
        string? recoveryRequestPath = null;
        var targetRequestCount = 0;
        var firstRequestAborted = false;
        int? secondRequestStatus = null;
        string? errorCode = null;
        var retryRequested = false;
        var freshAuthorizedContext = false;
        var cacheCleared = false;
        var serviceWorkersBlocked = false;
        var shellRemainedInteractive = false;
        var personnelRecovered = false;
        var registryNavigationAvailable = false;
        string? diagnostic = null;

        try
        {
            if (request.TargetCapabilityAssets.Count != 2 || targetAsset is null || recoveryAsset is null)
            {
                throw new InvalidOperationException(
                    "Capability recovery requires exactly one primary and one recovery target asset; "
                    + $"observed {primaryAssets.Length} primary, {recoveryAssets.Length} recovery, "
                    + $"{request.TargetCapabilityAssets.Count} total.");
            }

            await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
            {
                Locale = "zh-CN",
                ViewportSize = new ViewportSize { Width = 1440, Height = 900 },
                ServiceWorkers = ServiceWorkerPolicy.Block,
                StorageState = administratorStorage
            });
            freshAuthorizedContext = true;
            serviceWorkersBlocked = true;
            var page = await context.NewPageAsync();
            page.SetDefaultTimeout(120_000);
            page.SetDefaultNavigationTimeout(120_000);
            var cdp = await context.NewCDPSessionAsync(page);
            await ApplyThrottleAsync(cdp, request.Throttle);
            cacheCleared = true;
            var secondResponse = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

            page.Response += (_, response) =>
            {
                if (AssetUrlMatches(response.Url, recoveryAsset))
                {
                    recoveryRequestPath = new Uri(response.Url).AbsolutePath;
                    secondResponse.TrySetResult(response.Status);
                }
            };
            await page.RouteAsync("**/*", async route =>
            {
                if (AssetUrlMatches(route.Request.Url, targetAsset))
                {
                    Interlocked.Increment(ref targetRequestCount);
                    failedRequestPath = new Uri(route.Request.Url).AbsolutePath;
                    firstRequestAborted = true;
                    await route.AbortAsync("failed");
                    return;
                }

                if (!AssetUrlMatches(route.Request.Url, recoveryAsset))
                {
                    await route.ContinueAsync();
                    return;
                }

                Interlocked.Increment(ref targetRequestCount);
                await route.ContinueAsync();
            });

            await page.GotoAsync(origin + "/organization/persons", new PageGotoOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded
            });
            var localError = page.Locator("[data-route-capability-error]");
            await localError.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
            errorCode = await localError.GetAttributeAsync("data-route-capability-error");
            var shellNavigation = page.GetByLabel("折叠菜单", new PageGetByLabelOptions { Exact = true });
            await shellNavigation.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
            shellRemainedInteractive = await shellNavigation.IsEnabledAsync();

            var retry = page.GetByRole(
                AriaRole.Button,
                new PageGetByRoleOptions { Name = "重试当前页面", Exact = true });
            await retry.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
            retryRequested = true;
            await retry.ClickAsync();
            secondRequestStatus = await secondResponse.Task.WaitAsync(
                TimeSpan.FromSeconds(120),
                cancellationToken);
            var keyword = page.GetByLabel("关键字", new PageGetByLabelOptions { Exact = true });
            var table = page.Locator(".ant-table").First;
            await keyword.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
            await table.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
            personnelRecovered = await keyword.IsEnabledAsync();

            var identityAccounts = page.GetByRole(
                AriaRole.Link,
                new PageGetByRoleOptions { Name = "账号管理", Exact = true }).First;
            await identityAccounts.ClickAsync();
            await page.WaitForURLAsync(origin + "/identity/accounts");
            await page.GetByRole(
                    AriaRole.Heading,
                    new PageGetByRoleOptions { Name = "账号管理", Exact = true })
                .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
            var personnel = page.GetByRole(
                AriaRole.Link,
                new PageGetByRoleOptions { Name = "人员中心", Exact = true }).First;
            await personnel.ClickAsync();
            await page.WaitForURLAsync(origin + "/organization/persons");
            await page.GetByLabel("关键字", new PageGetByLabelOptions { Exact = true })
                .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
            registryNavigationAvailable = true;
        }
        catch (Exception exception)
        {
            diagnostic = Redact(
                $"{exception.GetType().Name}: {exception.Message}",
                request.AdministratorPassword,
                request.UnauthorizedPassword);
        }

        var probe = new ProductionColdStartCapabilityRecoveryProbe(
            SchemaVersion: "1.0",
            ContainsSecrets: false,
            TargetCapabilityAsset: targetAsset,
            RecoveryCapabilityAsset: recoveryAsset,
            FailedRequestPath: failedRequestPath,
            RecoveryRequestPath: recoveryRequestPath,
            TargetRequestCount: targetRequestCount,
            FirstRequestAborted: firstRequestAborted,
            SecondRequestStatus: secondRequestStatus,
            ErrorCode: errorCode,
            RetryRequested: retryRequested,
            RetryResult: personnelRecovered && registryNavigationAvailable ? "recovered" : "failed",
            FreshAuthorizedContext: freshAuthorizedContext,
            CacheCleared: cacheCleared,
            ServiceWorkersBlocked: serviceWorkersBlocked,
            ShellRemainedInteractive: shellRemainedInteractive,
            PersonnelRecovered: personnelRecovered,
            RegistryNavigationAvailable: registryNavigationAvailable,
            Diagnostic: diagnostic);
        await AcceptanceInfrastructure.WriteJsonAtomicallyAsync(
            Path.Combine(request.RunDirectory, "capability-recovery.json"),
            probe,
            cancellationToken);
        return probe;
    }

    // 每次样本使用新 context；cache、Service Worker、viewport 和节流都在导航前显式固定。
    private static async Task<ProductionColdStartSample> CaptureSampleAsync(
        IBrowser browser,
        string origin,
        string scenario,
        int iteration,
        string? storageState,
        ProductionColdStartBrowserRequest request,
        CancellationToken cancellationToken)
    {
        var cacheCleared = false;
        try
        {
            await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
            {
                Locale = "zh-CN",
                ViewportSize = new ViewportSize { Width = 1440, Height = 900 },
                ServiceWorkers = ServiceWorkerPolicy.Block,
                StorageState = storageState
            });
            await context.AddInitScriptAsync(LongTaskInitScript);
            var page = await context.NewPageAsync();
            page.SetDefaultTimeout(120_000);
            page.SetDefaultNavigationTimeout(120_000);
            var cdp = await context.NewCDPSessionAsync(page);
            await ApplyThrottleAsync(cdp, request.Throttle);
            cacheCleared = true;

            var path = scenario switch
            {
                ProductionColdStartScenarios.AnonymousLogin => "/login",
                ProductionColdStartScenarios.AuthenticatedHome => "/",
                ProductionColdStartScenarios.AuthenticatedPersonnel => "/organization/persons",
                ProductionColdStartScenarios.UnauthorizedPersonnel => "/organization/persons",
                _ => throw new InvalidOperationException($"Unknown production cold-start scenario '{scenario}'.")
            };
            var stopwatch = Stopwatch.StartNew();
            await page.GotoAsync(origin + path, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded
            });
            await WaitForInteractiveEndpointAsync(page, scenario);
            stopwatch.Stop();
            await page.WaitForLoadStateAsync(LoadState.Load);
            await page.EvaluateAsync("() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))");
            var observation = await page.EvaluateAsync<JsonElement>(ReadTimingScript);
            var navigation = ReadNavigation(observation);
            var requests = ReadRequests(observation);
            var longTasks = ReadLongTasks(observation);
            var longTaskSucceeded = observation.GetProperty("longTaskObserverSucceeded").GetBoolean();
            var requestedTargets = request.TargetCapabilityAssets
                .Where(asset => requests.Any(item => AssetPathMatches(item.Path, asset)))
                .Order(StringComparer.Ordinal)
                .ToArray();

            return new ProductionColdStartSample(
                scenario,
                iteration,
                Skipped: false,
                SkipReason: null,
                CacheCleared: cacheCleared,
                ServiceWorkersBlocked: true,
                ViewportWidth: 1440,
                ViewportHeight: 900,
                request.Throttle,
                InteractiveMs: stopwatch.ElapsedMilliseconds,
                navigation,
                requests,
                longTasks,
                longTaskSucceeded,
                requestedTargets);
        }
        catch (Exception exception)
        {
            if (exception is OperationCanceledException
                || string.Equals(exception.GetType().Name, "TargetClosedException", StringComparison.Ordinal))
            {
                throw;
            }
            return new ProductionColdStartSample(
                scenario,
                iteration,
                Skipped: true,
                SkipReason: Redact(
                    $"{exception.GetType().Name}: {exception.Message}",
                    request.AdministratorPassword,
                    request.UnauthorizedPassword),
                CacheCleared: cacheCleared,
                ServiceWorkersBlocked: true,
                ViewportWidth: 1440,
                ViewportHeight: 900,
                request.Throttle,
                InteractiveMs: 0,
                Navigation: null,
                Requests: null,
                LongTasks: null,
                LongTaskObserverSucceeded: false,
                RequestedTargetCapabilityAssets: []);
        }
    }

    // CDP 使用 bytes/second；数值与 #58 冻结的 10/5 Mbps、40 ms RTT 和 4x CPU 一致。
    private static async Task ApplyThrottleAsync(
        ICDPSession session,
        ProductionColdStartThrottleProfile throttle)
    {
        await session.SendAsync("Network.enable");
        await session.SendAsync("Network.setCacheDisabled", new Dictionary<string, object>
        {
            ["cacheDisabled"] = true
        });
        await session.SendAsync("Network.clearBrowserCache");
        await session.SendAsync("Network.setBypassServiceWorker", new Dictionary<string, object>
        {
            ["bypass"] = true
        });
        await session.SendAsync("Network.emulateNetworkConditions", new Dictionary<string, object>
        {
            ["offline"] = false,
            ["latency"] = throttle.LatencyMs,
            ["downloadThroughput"] = throttle.DownloadBytesPerSecond,
            ["uploadThroughput"] = throttle.UploadBytesPerSecond
        });
        await session.SendAsync("Emulation.setCPUThrottlingRate", new Dictionary<string, object>
        {
            ["rate"] = throttle.CpuSlowdownRate
        });
    }

    // 登录准备只生成内存 storage state；Cookie 与凭据都不会写入 evidence。
    private static async Task<string> CaptureStorageStateAsync(
        IBrowser browser,
        string origin,
        string ecode,
        string password)
    {
        await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            Locale = "zh-CN",
            ViewportSize = new ViewportSize { Width = 1440, Height = 900 },
            ServiceWorkers = ServiceWorkerPolicy.Block
        });
        var page = await context.NewPageAsync();
        page.SetDefaultTimeout(120_000);
        page.SetDefaultNavigationTimeout(120_000);
        await page.GotoAsync(origin + "/login", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.DOMContentLoaded
        });
        await page.Locator("#ecode").FillAsync(ecode);
        await page.Locator("#password").FillAsync(password);
        await page.GetByRole(
            AriaRole.Button,
            new PageGetByRoleOptions { Name = "登录", Exact = true }).ClickAsync();
        await page.WaitForURLAsync(origin + "/", new PageWaitForURLOptions
        {
            WaitUntil = WaitUntilState.DOMContentLoaded
        });
        await page.GetByRole(
                AriaRole.Heading,
                new PageGetByRoleOptions { Name = "欢迎使用 ICP Platform", Exact = true })
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        return await context.StorageStateAsync();
    }

    // 用户可操作终点只依赖稳定可访问控件，不用 load 或首张绘制替代。
    private static async Task WaitForInteractiveEndpointAsync(IPage page, string scenario)
    {
        if (scenario == ProductionColdStartScenarios.AnonymousLogin)
        {
            var account = page.Locator("#ecode");
            var password = page.Locator("#password");
            var login = page.GetByRole(
                AriaRole.Button,
                new PageGetByRoleOptions { Name = "登录", Exact = true });
            await account.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
            await password.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
            await login.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
            if (!await account.IsEnabledAsync() || !await password.IsEnabledAsync() || !await login.IsEnabledAsync())
            {
                throw new InvalidOperationException("Anonymous login controls were visible but not operable.");
            }
            return;
        }

        if (scenario == ProductionColdStartScenarios.AuthenticatedHome)
        {
            var navigation = page.GetByLabel("折叠菜单", new PageGetByLabelOptions { Exact = true });
            var heading = page.GetByRole(
                AriaRole.Heading,
                new PageGetByRoleOptions { Name = "欢迎使用 ICP Platform", Exact = true });
            await navigation.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
            await heading.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
            if (!await navigation.IsEnabledAsync())
            {
                throw new InvalidOperationException("Authenticated home navigation was not operable.");
            }
            return;
        }

        if (scenario == ProductionColdStartScenarios.AuthenticatedPersonnel)
        {
            var keyword = page.GetByLabel("关键字", new PageGetByLabelOptions { Exact = true });
            var table = page.Locator(".ant-table").First;
            await keyword.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
            await table.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
            if (!await keyword.IsEnabledAsync())
            {
                throw new InvalidOperationException("Personnel query control was visible but not operable.");
            }
            return;
        }

        await page.GetByText("无权限访问", new PageGetByTextOptions { Exact = true })
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
    }

    // 将浏览器原始导航对象收敛为稳定结果 schema。
    private static ProductionColdStartNavigationSample? ReadNavigation(JsonElement observation)
    {
        var navigation = observation.GetProperty("navigation");
        return navigation.ValueKind is JsonValueKind.Null
            ? null
            : new ProductionColdStartNavigationSample(
                navigation.GetProperty("domContentLoadedMs").GetDouble(),
                navigation.GetProperty("loadMs").GetDouble(),
                navigation.GetProperty("responseEndMs").GetDouble());
    }

    // 请求只保留同源 URL path 与 timing/transfer/status，不保留 query、headers 或 Cookie。
    private static IReadOnlyList<ProductionColdStartRequestSample> ReadRequests(JsonElement observation) =>
        observation.GetProperty("requests")
            .EnumerateArray()
            .Select(item => new ProductionColdStartRequestSample(
                item.GetProperty("path").GetString() ?? "/",
                item.GetProperty("resourceType").GetString() ?? "other",
                (int)Math.Round(item.GetProperty("status").GetDouble()),
                (long)Math.Round(item.GetProperty("transferBytes").GetDouble()),
                item.GetProperty("startTimeMs").GetDouble(),
                item.GetProperty("durationMs").GetDouble()))
            .ToArray();

    // Long Task 原始 start/duration 全量保留，分位数只在 reducer 中计算。
    private static IReadOnlyList<ProductionColdStartLongTaskSample>? ReadLongTasks(JsonElement observation)
    {
        var longTasks = observation.GetProperty("longTasks");
        return longTasks.ValueKind is JsonValueKind.Null
            ? null
            : longTasks.EnumerateArray()
                .Select(item => new ProductionColdStartLongTaskSample(
                    item.GetProperty("startTimeMs").GetDouble(),
                    item.GetProperty("durationMs").GetDouble()))
                .ToArray();
    }

    // 每次完成即原子覆盖 checkpoint，长跑中断时仍保留此前全部原始样本。
    private static Task CheckpointAsync(
        string path,
        IReadOnlyList<ProductionColdStartSample> samples,
        CancellationToken cancellationToken) =>
        AcceptanceInfrastructure.WriteJsonAtomicallyAsync(
            path,
            new
            {
                schemaVersion = "1.0",
                containsSecrets = false,
                samples
            },
            cancellationToken);

    // source-map 分析器输出相对 asset path；浏览器观察只做规范化后精确 suffix 对比。
    private static bool AssetPathMatches(string requestedPath, string asset) =>
        requestedPath.Replace('\\', '/').EndsWith(
            "/" + asset.Replace('\\', '/').TrimStart('/'),
            StringComparison.OrdinalIgnoreCase);

    // 网络拦截只比较 URL path 与 analyzer asset suffix，绝不读取 query、header 或 Cookie。
    private static bool AssetUrlMatches(string requestedUrl, string asset)
    {
        return Uri.TryCreate(requestedUrl, UriKind.Absolute, out var uri)
               && AssetPathMatches(uri.AbsolutePath, asset);
    }

    // 所有可能带浏览器诊断的文本在落 evidence 前再次替换内存凭据。
    private static string Redact(string value, params string[] secrets)
    {
        var result = value;
        foreach (var secret in secrets.Where(static item => !string.IsNullOrEmpty(item)))
        {
            result = result.Replace(secret, "[REDACTED]", StringComparison.Ordinal);
        }
        return result;
    }

    // 使用与现有 Acceptance browser observer 相同的显式 Chrome 解析顺序。
    private static string ResolveChromePath()
    {
        var configured = Environment.GetEnvironmentVariable("NEWP_ACCEPTANCE_CHROMIUM_EXECUTABLE");
        return new[]
            {
                configured,
                @"C:\Program Files\Google\Chrome\Application\chrome.exe",
                @"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe",
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Google",
                    "Chrome",
                    "Application",
                    "chrome.exe")
            }
            .FirstOrDefault(static path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
            ?? throw new InvalidOperationException("No Chromium executable was found for production cold-start acceptance.");
    }
}

internal sealed class ProductionShellServerLease : IAsyncDisposable
{
    private readonly Process _process;
    private readonly string _readyFile;
    private readonly string _runId;
    private readonly string _standardOutputPath;
    private readonly string _standardErrorPath;
    private readonly Task<string> _standardOutput;
    private readonly Task<string> _standardError;
    private bool _stopped;

    private ProductionShellServerLease(
        Process process,
        string readyFile,
        string runId,
        int port,
        string standardOutputPath,
        string standardErrorPath)
    {
        _process = process;
        _readyFile = readyFile;
        _runId = runId;
        Port = port;
        Identity = $"production-shell:{process.Id}:{runId}";
        _standardOutputPath = standardOutputPath;
        _standardErrorPath = standardErrorPath;
        _standardOutput = process.StandardOutput.ReadToEndAsync();
        _standardError = process.StandardError.ReadToEndAsync();
    }

    public int Port { get; }

    public string Identity { get; }

    /// <summary>
    /// 启动只服务 build dist 且代理当前 owned Host 的 Node 进程，并用 ready file 校验 pid/run identity。
    /// </summary>
    public static async Task<ProductionShellServerLease> StartAsync(
        ProductionColdStartBrowserRequest request,
        CancellationToken cancellationToken)
    {
        var readyFile = Path.Combine(request.RunDirectory, "production-shell", "ready.json");
        var standardOutputPath = Path.Combine(request.RunDirectory, "production-shell", "stdout.log");
        var standardErrorPath = Path.Combine(request.RunDirectory, "production-shell", "stderr.log");
        Directory.CreateDirectory(Path.GetDirectoryName(readyFile)!);
        var startInfo = new ProcessStartInfo("node")
        {
            WorkingDirectory = request.RepositoryRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in new[]
                 {
                     Path.Combine(request.RepositoryRoot, "scripts", "performance", "production-shell-server.mjs"),
                     "--assets-root", request.AssetsRoot,
                     "--host-origin", $"http://127.0.0.1:{request.HostPort}",
                     "--port", "0",
                     "--run-id", request.RunId,
                     "--ready-file", readyFile
                 })
        {
            startInfo.ArgumentList.Add(argument);
        }

        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Unable to start the owned production Shell server.");
        try
        {
            var port = await WaitForReadyAsync(process, readyFile, request.RunId, cancellationToken);
            return new ProductionShellServerLease(
                process,
                readyFile,
                request.RunId,
                port,
                standardOutputPath,
                standardErrorPath);
        }
        catch
        {
            // 此 Process 实例由本方法刚创建；ready 失败时只终止这一精确 owned 进程，避免留下孤儿服务。
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None);
            }
            process.Dispose();
            throw;
        }
    }

    /// <summary>
    /// 仅在 ready identity 仍匹配时终止精确 owned pid，并保存不含秘密的服务日志。
    /// </summary>
    public async Task<AcceptanceCleanupResult> StopAsync(CancellationToken cancellationToken)
    {
        if (_stopped)
        {
            return new AcceptanceCleanupResult("completed", null);
        }
        _stopped = true;
        try
        {
            if (!_process.HasExited)
            {
                using var ready = JsonDocument.Parse(await File.ReadAllTextAsync(_readyFile, cancellationToken));
                if (ready.RootElement.GetProperty("pid").GetInt32() != _process.Id
                    || !string.Equals(
                        ready.RootElement.GetProperty("runId").GetString(),
                        _runId,
                        StringComparison.Ordinal))
                {
                    return new AcceptanceCleanupResult("failed", AcceptanceFailureCodes.OwnershipMismatch);
                }
                _process.Kill(entireProcessTree: true);
            }
            await _process.WaitForExitAsync(cancellationToken);
            await File.WriteAllTextAsync(_standardOutputPath, await _standardOutput, new UTF8Encoding(false), cancellationToken);
            await File.WriteAllTextAsync(_standardErrorPath, await _standardError, new UTF8Encoding(false), cancellationToken);
            return new AcceptanceCleanupResult("completed", null);
        }
        catch
        {
            return new AcceptanceCleanupResult("failed", AcceptanceFailureCodes.CleanupFailed);
        }
    }

    // ready file 与 HTTP endpoint 必须都返回相同 run identity，避免端口复用误认他人服务。
    private static async Task<int> WaitForReadyAsync(
        Process process,
        string readyFile,
        string runId,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (process.HasExited)
            {
                throw new InvalidOperationException($"Production Shell server exited early with code {process.ExitCode}.");
            }
            if (File.Exists(readyFile))
            {
                try
                {
                    using var ready = JsonDocument.Parse(await File.ReadAllTextAsync(readyFile, cancellationToken));
                    var readyRunId = ready.RootElement.GetProperty("runId").GetString();
                    var readyPid = ready.RootElement.GetProperty("pid").GetInt32();
                    var port = ready.RootElement.GetProperty("port").GetInt32();
                    if (readyPid == process.Id && string.Equals(readyRunId, runId, StringComparison.Ordinal))
                    {
                        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
                        using var response = await client.GetAsync(
                            $"http://127.0.0.1:{port}/__newp/performance/ready",
                            cancellationToken);
                        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
                        using var endpoint = JsonDocument.Parse(responseBody);
                        if (response.IsSuccessStatusCode
                            && string.Equals(
                                endpoint.RootElement.GetProperty("runId").GetString(),
                                runId,
                                StringComparison.Ordinal))
                        {
                            return port;
                        }
                    }
                }
                catch (IOException)
                {
                    // Node 可能正在原子上尚未完成的 ready-file 写入窗口，下一轮重新读取。
                }
                catch (JsonException)
                {
                    // 同上，只有完整 JSON 与 endpoint 双重匹配才会接受服务。
                }
            }
            await Task.Delay(100, cancellationToken);
        }
        throw new TimeoutException("Production Shell server did not publish a matching ready identity within 30 seconds.");
    }

    /// <summary>
    /// 异常路径也复用相同 ownership-safe stop；正常路径的第二次调用为空操作。
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None);
        _process.Dispose();
    }
}
