using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Playwright;

namespace ICP.Platform.Acceptance;

internal sealed record FeatureScaleRole(string Id, string Code, string Name);
internal sealed record FeatureScaleSample(int PointCount, int RoleCount, long GrantCount);
internal sealed record FeatureScaleBrowserRequest(string HostUrl, string ShellUrl, string OutputDirectory,
    string AdministratorECode, FeatureScaleRole[] Roles, FeatureScaleSample Sample)
{
    [JsonIgnore] public string AdministratorPassword { get; init; } = "";
}

// 父 fixture 拥有 Host、Shell 和 SQL；此观察器只拥有浏览器，凭据仅从 stdin 进入内存。
internal static class FeatureScaleBrowserObserver
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private const string Workbench = "/api/authorization/workbench";

    internal static FeatureScaleBrowserRequest Parse(string input)
    {
        try
        {
            using var json = JsonDocument.Parse(input);
            var request = json.Deserialize<FeatureScaleBrowserRequest>(JsonOptions)
                ?? throw new ArgumentException();
            var password = json.RootElement.GetProperty("administratorPassword").GetString();
            if (!IsLocalOrigin(request.HostUrl) || !IsLocalOrigin(request.ShellUrl)
                || string.IsNullOrWhiteSpace(request.OutputDirectory) || string.IsNullOrWhiteSpace(request.AdministratorECode)
                || string.IsNullOrWhiteSpace(password) || request.Roles is not { Length: 2 }
                || request.Roles.Any(role => role is null || string.IsNullOrWhiteSpace(role.Id)
                    || string.IsNullOrWhiteSpace(role.Code) || string.IsNullOrWhiteSpace(role.Name)
                    || role.Code.Equals("LocalAdmin", StringComparison.OrdinalIgnoreCase))
                || request.Roles[0].Id == request.Roles[1].Id || request.Sample is null
                || request.Sample.PointCount <= 0 || request.Sample.RoleCount <= 20 || request.Sample.GrantCount <= 0)
                throw new ArgumentException();
            return request with { AdministratorPassword = password, OutputDirectory = Path.GetFullPath(request.OutputDirectory) };
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or InvalidOperationException or KeyNotFoundException or NotSupportedException)
        {
            throw new ArgumentException("Invalid feature-scale observer input; expected local origins, two distinct ordinary roles and a pageable sample.");
        }
    }

    private static bool IsLocalOrigin(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.IsLoopback && uri.Scheme is "http" or "https" && uri.UserInfo.Length == 0
        && uri.AbsolutePath == "/" && uri.Query.Length == 0 && uri.Fragment.Length == 0;

    internal static string Redact(FeatureScaleBrowserRequest request, string text) =>
        AcceptanceInfrastructure.Sanitize([request.AdministratorPassword], text);

    // 独立 CLI 不经过通用异常输出，确保解析失败不回显 stdin，运行失败仍保留脱敏原栈。
    internal static async Task<int> RunConsoleAsync()
    {
        FeatureScaleBrowserRequest? request = null;
        try
        {
            request = Parse(await Console.In.ReadToEndAsync());
            var path = await ObserveAsync(request);
            Console.WriteLine($"FEATURE_SCALE_BROWSER_RESULT: {path}");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(request is null ? "Invalid feature-scale observer input." : Redact(request, exception.ToString()));
            return 1;
        }
    }

    // 只输出实际响应的规模与分页事实，不落地角色授权全集或请求秘密。
    internal static object DescribeResponse(string path, int status, string body)
    {
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        int? Count(string field) => root.TryGetProperty(field, out var value) && value.ValueKind == JsonValueKind.Array ? value.GetArrayLength() : null;
        int? total = root.TryGetProperty("total", out var count) && count.TryGetInt32(out var value) ? value : null;
        return new { path, status, pointCount = Count("points"), itemCount = Count("items"), total, positionCount = Count("positions") };
    }

    private static async Task<string> ObserveAsync(FeatureScaleBrowserRequest request)
    {
        Directory.CreateDirectory(request.OutputDirectory);
        using var playwright = await Playwright.CreateAsync();
        IBrowser? browser = null;
        IPage? page = null;
        Exception? failure = null;
        Exception? cleanupFailure = null;
        var browserClosed = false;
        var requests = new ConcurrentQueue<string>();
        var responses = new ConcurrentQueue<IResponse>();
        var errors = new ConcurrentQueue<string>();
        var stages = new List<object>();
        var treeSamples = new List<JsonElement>();
        var catalogCounts = new List<object>();
        var responseFacts = new List<object>();
        string? browserVersion = null;
        JsonElement? timing = null;
        var total = Stopwatch.StartNew();
        try
        {
            browser = await playwright.Chromium.LaunchAsync(new()
            {
                Headless = true,
                ExecutablePath = Environment.GetEnvironmentVariable("NEWP_ACCEPTANCE_CHROMIUM_EXECUTABLE")
                    ?? @"C:\Program Files\Google\Chrome\Application\chrome.exe"
            });
            browserVersion = browser.Version;
            var context = await browser.NewContextAsync(new() { Locale = "zh-CN", ViewportSize = new() { Width = 1440, Height = 1000 } });
            await context.AddInitScriptAsync("""
                (() => {
                  window.__featureScaleTasks = []; window.__featureScaleDropped = 0;
                  window.__featureScaleSupported = PerformanceObserver.supportedEntryTypes.includes('longtask');
                  if (window.__featureScaleSupported) new PerformanceObserver(list => {
                    for (const e of list.getEntries()) {
                      if (window.__featureScaleTasks.length < 10000) window.__featureScaleTasks.push({startTimeMs:e.startTime,durationMs:e.duration});
                      else window.__featureScaleDropped++;
                    }
                  }).observe({type:'longtask',buffered:true});
                })();
                """);
            page = await context.NewPageAsync();
            page.SetDefaultTimeout(30_000);
            page.SetDefaultNavigationTimeout(120_000);
            page.Request += (_, message) =>
            {
                var uri = new Uri(message.Url);
                if (uri.AbsolutePath.StartsWith(Workbench, StringComparison.Ordinal)) requests.Enqueue(uri.PathAndQuery);
            };
            page.Response += (_, response) =>
            {
                if (new Uri(response.Url).AbsolutePath.StartsWith(Workbench, StringComparison.Ordinal)) responses.Enqueue(response);
            };
            page.PageError += (_, error) => errors.Enqueue(error);
            page.RequestFailed += (_, message) => errors.Enqueue($"{message.Method} {new Uri(message.Url).AbsolutePath}: {message.Failure}");
            await new ShellPageDriver(page, request.ShellUrl.TrimEnd('/')).LoginAsync(request.AdministratorECode, request.AdministratorPassword);

            int CatalogRequests() => requests.Count(path => path.Split('?')[0] == Workbench + "/catalog");
            async Task SettledAsync() => await page.EvaluateAsync("() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))");
            async Task StageAsync(string name, Func<Task> action)
            {
                var watch = Stopwatch.StartNew();
                await action();
                await SettledAsync();
                stages.Add(new { name, elapsedMs = watch.Elapsed.TotalMilliseconds });
                catalogCounts.Add(new { stage = name, count = CatalogRequests() });
            }
            bool Endpoint(IResponse response, string suffix) => new Uri(response.Url).AbsolutePath == Workbench + suffix;

            await StageAsync("open-workbench", async () =>
            {
                var catalog = page.WaitForResponseAsync(response => Endpoint(response, "/catalog"));
                var roles = page.WaitForResponseAsync(response => Endpoint(response, "/roles"));
                await page.Locator(".np-sidebar-menu").GetByRole(AriaRole.Link, new() { Name = "功能授权", Exact = true }).ClickAsync();
                await Task.WhenAll(catalog, roles);
                await page.Locator(".ant-list-item button").First.WaitForAsync();
            });
            foreach (var target in new[] { 2, 1 })
            {
                await StageAsync($"role-page-{target}", async () =>
                {
                    var response = page.WaitForResponseAsync(value => Endpoint(value, "/roles")
                        && StandardCrudPageDriver.HasQueryValue(new Uri(value.Url), "pageIndex", target.ToString()));
                    await page.Locator(target == 2 ? ".ant-pagination-next" : ".ant-pagination-prev").ClickAsync();
                    await response;
                });
            }
            foreach (var role in request.Roles)
            {
                await StageAsync($"select-role-{role.Code}", async () =>
                {
                    var search = page.GetByLabel("搜索角色", new() { Exact = true });
                    var listed = page.WaitForResponseAsync(value => Endpoint(value, "/roles")
                        && StandardCrudPageDriver.HasQueryValue(new Uri(value.Url), "query", role.Code));
                    await search.FillAsync(role.Code);
                    await search.PressAsync("Enter");
                    await listed;
                    var detail = page.WaitForResponseAsync(value => Endpoint(value, "/roles/" + role.Id));
                    await page.GetByRole(AriaRole.Button, new() { Name = $"{role.Name} · {role.Code}", Exact = true }).ClickAsync();
                    await detail;
                    await page.GetByText(role.Name + " · " + role.Code, new() { Exact = true })
                        .And(page.Locator(".ant-card-head-title")).WaitForAsync();
                });
            }
            await StageAsync("expand-tree", () => page.GetByRole(AriaRole.Button, new() { Name = "展开全部", Exact = true }).ClickAsync());
            const string treeSelector = ".ant-tree-list-holder";
            await page.Locator(treeSelector).WaitForAsync();
            async Task ReadTreeAsync(string stage)
            {
                treeSamples.Add(await page.EvaluateAsync<JsonElement>("""
                    stage => { const holder=document.querySelector('.ant-tree-list-holder');
                      const nodes=[...document.querySelectorAll('.ant-tree-treenode')];
                      return {stage, mountedNodes:nodes.length,scrollTop:holder.scrollTop,scrollHeight:holder.scrollHeight,
                        clientHeight:holder.clientHeight,firstTitle:nodes[0]?.textContent,lastTitle:nodes.at(-1)?.textContent}; }
                    """, stage));
            }
            await ReadTreeAsync("expanded-top");
            await StageAsync("scroll-tree-bottom", async () =>
            {
                await page.Locator(treeSelector).EvaluateAsync("element => { element.scrollTop = element.scrollHeight; element.dispatchEvent(new Event('scroll')); }");
            });
            await ReadTreeAsync("expanded-bottom");
            await page.ScreenshotAsync(new() { Path = Path.Combine(request.OutputDirectory, "feature-scale-workbench.png"), FullPage = true,
                Animations = ScreenshotAnimations.Disabled, Mask = [page.Locator("input, textarea")] });
            timing = await page.EvaluateAsync<JsonElement>("""
                () => ({timeOrigin:performance.timeOrigin,nowMs:performance.now(),longTaskSupported:window.__featureScaleSupported,
                    longTasks:window.__featureScaleTasks,droppedLongTasks:window.__featureScaleDropped,
                    network:performance.getEntriesByType('resource').filter(e=>new URL(e.name).pathname.startsWith('/api/authorization/workbench'))
                      .map(e=>({path:new URL(e.name).pathname,durationMs:e.duration,transferBytes:e.transferSize,decodedBodyBytes:e.decodedBodySize}))})
                """);
        }
        catch (Exception exception)
        {
            failure = exception;
            if (page is not null)
            {
                try
                {
                    await File.WriteAllTextAsync(Path.Combine(request.OutputDirectory, "feature-scale-failure.txt"),
                        Redact(request, exception + "\n" + await page.Locator("body").InnerTextAsync()));
                    await page.ScreenshotAsync(new() { Path = Path.Combine(request.OutputDirectory, "feature-scale-failure.png"), FullPage = true,
                        Mask = [page.Locator("input, textarea")] });
                }
                catch (Exception) { /* 原操作异常优先于诊断采集失败。 */ }
            }
        }
        finally
        {
            foreach (var response in responses)
            {
                try
                {
                    var path = new Uri(response.Url).PathAndQuery;
                    responseFacts.Add(DescribeResponse(path, response.Status, await response.TextAsync()));
                    if (response.Status >= 400) failure ??= new InvalidOperationException($"Workbench request failed: {path} ({response.Status}).");
                }
                catch (Exception exception) { failure ??= exception; }
            }
            if (browser is not null)
            {
                try { await browser.CloseAsync(); browserClosed = true; }
                catch (Exception exception) { cleanupFailure = exception; }
            }
        }
        var result = new { sample = request.Sample, hostOrigin = request.HostUrl, shellOrigin = request.ShellUrl,
            browserVersion, elapsedMs = total.Elapsed.TotalMilliseconds, stages, catalogRequests = catalogCounts,
            requests = requests.ToArray(), responses = responseFacts, treeSamples, timing, browserClosed,
            errors = errors.Select(error => Redact(request, error)).ToArray() };
        var resultPath = Path.Combine(request.OutputDirectory, "feature-scale-browser.json");
        try { await File.WriteAllTextAsync(resultPath, Redact(request, JsonSerializer.Serialize(result, JsonOptions))); }
        catch (Exception exception) { failure ??= exception; }
        try
        {
            if (await AcceptanceInfrastructure.ScanForSecretsAsync(request.OutputDirectory, [request.AdministratorPassword], CancellationToken.None))
                failure ??= new InvalidOperationException("Credential found in browser observation output.");
        }
        catch (Exception exception) { failure ??= exception; }
        failure ??= cleanupFailure;
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        return resultPath;
    }
}
