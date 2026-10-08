using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using ICP.Platform.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Playwright;

namespace ICP.Platform.Acceptance;

/// <summary>Workspace 的同一正式浏览器旅程及其专用测试库、Host 和凭据生命周期。</summary>
internal sealed class WorkspaceAcceptanceRunner(string repositoryRoot, string runDirectory, string runId)
    : AcceptanceRunner(repositoryRoot, runDirectory, runId)
{
    private ReusableSqlServerDatabase? _databaseLease;
    private readonly Dictionary<string, string?> _environment = new();
    private string? _connection;
    private string? _ownership;
    private string? _devRun;
    private int _hostPort;
    private int _shellPort;
    private WorkspaceAcceptanceActor? _actor;
    private string? _entryPosition;

    protected override async Task RunScenariosAsync(CancellationToken token)
    {
        await RunScenarioAsync("workspace.formal-initialization", PrepareAsync, token);
        await RunScenarioAsync("workspace.browser", RunBrowserAsync, token);
        await RunScenarioAsync("workspace.process-restart", VerifyProcessRestartAsync, token);
    }

    // 空 schema 只由正式 Host bootstrap/migration 建立，普通 actor 的初次个人初始化留给真实页面入口。
    private async Task PrepareAsync(CancellationToken token)
    {
        _databaseLease = await ReusableSqlServerDatabase.AcquireAsync(_repositoryRoot, token);
        _connection = _databaseLease.ConnectionString;
        AddConnectionSecrets(_connection, new SqlConnectionStringBuilder(_connection).Password);
        _environment["ConnectionStrings__SqlServer"] = _connection;
        _environment["NEWP_DEV_SQLSERVER_CONNECTION_STRING"] = _connection;
        _environment["Platform__PluginInstallRoot"] = Path.Combine(_runDirectory, "resources", "plugins");
        _environment["Authentication__Host__KeyRingPath"] = Path.Combine(_runDirectory, "resources", "key-ring");
        _environment["Authentication__Host__CookieName"] = WorkspaceAcceptanceFixture.CookieName;
        _environment["Authentication__Host__AllowInsecureDevelopmentCookie"] = "true";
        _environment["Identity__LocalAdministrator__ECODE"] = "admin";
        _environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        var bootstrap = await FormalIdentityBootstrap.RunAsync(_repositoryRoot,
            Path.Combine(_runDirectory, "resources", "bootstrap"), _environment, token);
        RegisterSecret(bootstrap.TemporaryCredential);
        _ownership = await ReadOwnershipAsync(token);
        await StartDevAsync(token);
        using var administrator = WorkspaceAcceptanceFixture.CreateClient(_hostPort);
        var administratorPassword = "Aa1!" + Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        RegisterSecret(administratorPassword);
        await WorkspaceAcceptanceFixture.LoginAndChangePasswordAsync(administrator, "admin", bootstrap.TemporaryCredential,
            administratorPassword, token);
        var features = await AcceptanceFeatureCatalog.LoadAsync(administrator, token);
        _entryPosition = features.Behavior("Workspace", "workspace.spaces.access").Position;
        _actor = await WorkspaceAcceptanceFixture.PrepareAsync(administrator, features, _runId, RegisterSecret, token);
        await AcceptanceInfrastructure.WriteJsonAsync(Path.Combine(_runDirectory, "initialization.json"),
            new { catalog = ReusableSqlServerDatabase.Catalog, devRun = _devRun, actorId = _actor.AccountId, formalInitialization = true }, token);
    }

    private async Task StartDevAsync(CancellationToken token)
    {
        var started = await AcceptanceInfrastructure.RunProcessAsync("pwsh.exe",
            ["-NoLogo", "-NoProfile", "-File", Path.Combine(_repositoryRoot, "scripts", "dev.ps1"),
             "-HostEnvironmentName", "Acceptance", "-SkipDependencyInstall", "-HostStartupTimeoutSeconds", "180"],
            _repositoryRoot, _environment, redirectOutput: false, cancellationToken: token);
        if (started.ExitCode != 0) throw new InvalidOperationException("Managed dev failed; inspect .codex-temp/dev/current-result.json.");
        using var state = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(_repositoryRoot, ".codex-temp", "dev", "state.json"), token));
        _devRun = state.RootElement.GetProperty("runId").GetString();
        _hostPort = state.RootElement.GetProperty("ports").GetProperty("host").GetInt32();
        _shellPort = state.RootElement.GetProperty("ports").GetProperty("shell").GetInt32();
    }

    // 该前缀从首片接入，后续功能继续在同一 driver 上追加内容/引用操作；浏览器只证明路径。
    private async Task RunBrowserAsync(CancellationToken token)
    {
        var actor = _actor ?? throw new InvalidOperationException("Workspace 普通主体尚未准备。");
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new()
        {
            Headless = true,
            ExecutablePath = Environment.GetEnvironmentVariable("NEWP_ACCEPTANCE_CHROMIUM_EXECUTABLE")
                ?? @"C:\Program Files\Google\Chrome\Application\chrome.exe"
        });
        await using var context = await browser.NewContextAsync(new() { Locale = "zh-CN", ViewportSize = new() { Width = 1440, Height = 1000 } });
        var page = await context.NewPageAsync();
        var errors = new ConcurrentQueue<string>();
        var failedResponses = new ConcurrentQueue<IResponse>();
        page.PageError += (_, error) => errors.Enqueue(error);
        page.RequestFailed += (_, request) => errors.Enqueue($"{request.Method} {request.Url}: {request.Failure}");
        page.Response += (_, response) => { if (response.Status >= 400) failedResponses.Enqueue(response); };
        page.SetDefaultTimeout(30_000);
        page.SetDefaultNavigationTimeout(120_000);
        var driver = new WorkspacePageDriver(page);
        try
        {
            token.ThrowIfCancellationRequested();
            await new ShellPageDriver(page, $"http://127.0.0.1:{_shellPort}").LoginAsync(actor.ECode, actor.Password);
            await driver.OpenAsync();
            await driver.SelectRootAsync("公共空间");
            await CapturePresentationAsync(page, "public-root.png");
            await driver.SelectRootAsync("个人空间");
            await CapturePresentationAsync(page, "personal-root.png");
            await driver.SelectRootAsync("公共空间");
            await page.SetViewportSizeAsync(900, 1000);
            await CapturePresentationAsync(page, "public-root-narrow.png");
            await AcceptanceInfrastructure.WriteJsonAsync(Path.Combine(_runDirectory, "browser.json"),
                new { url = page.Url, actor = "ordinary", rootSwitching = "public-personal-public" }, token);
        }
        catch (Exception failure)
        {
            await CaptureBrowserFailureAsync(page, driver, errors, failedResponses, failure);
            throw;
        }
    }

    private Task CapturePresentationAsync(IPage page, string name) => page.ScreenshotAsync(new()
    {
        Path = Path.Combine(_runDirectory, name), FullPage = true
    });

    // 同一 catalog 跨真实 Host 进程重启后新登录；只在 API 层比较空间和根身份，不重复浏览器旅程。
    private async Task VerifyProcessRestartAsync(CancellationToken token)
    {
        var actor = _actor ?? throw new InvalidOperationException("Workspace 普通主体尚未准备。");
        using var beforeClient = WorkspaceAcceptanceFixture.CreateClient(_hostPort);
        await WorkspaceAcceptanceFixture.LoginAsync(beforeClient, actor, token);
        var before = await WorkspaceAcceptanceFixture.SendAsync(beforeClient, HttpMethod.Get, "/api/workspace/spaces", null, _entryPosition, token);
        using (var state = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(_repositoryRoot, ".codex-temp", "dev", "state.json"), token)))
        {
            if (state.RootElement.GetProperty("runId").GetString() != _devRun)
                throw new InvalidOperationException("重启前 supervisor 归属已改变。");
        }
        var stop = await AcceptanceInfrastructure.RunProcessAsync("pwsh.exe",
            ["-NoLogo", "-NoProfile", "-File", Path.Combine(_repositoryRoot, "scripts", "stop-dev.ps1")], _repositoryRoot, cancellationToken: token);
        if (stop.ExitCode != 0) throw new InvalidOperationException($"正式 Host 重启停止失败：{Sanitize(stop.StandardOutput + stop.StandardError)}");
        var previousRun = _devRun;
        _devRun = null;
        await StartDevAsync(token);
        using var afterClient = WorkspaceAcceptanceFixture.CreateClient(_hostPort);
        await WorkspaceAcceptanceFixture.LoginAsync(afterClient, actor, token);
        var afterRead = await WorkspaceAcceptanceFixture.SendAsync(afterClient, HttpMethod.Get, "/api/workspace/spaces", null, _entryPosition, token);
        await WorkspaceAcceptanceFixture.SendAsync(afterClient, HttpMethod.Post, "/api/workspace/spaces/personal/ensure", null, _entryPosition, token);
        var afterEnsure = await WorkspaceAcceptanceFixture.SendAsync(afterClient, HttpMethod.Get, "/api/workspace/spaces", null, _entryPosition, token);
        var expected = RootIdentities(before);
        if (expected.Length != 2 || !expected.SequenceEqual(RootIdentities(afterRead)) || !expected.SequenceEqual(RootIdentities(afterEnsure)))
            throw new InvalidOperationException("真实 Host 进程重启后空间或根身份发生漂移。");
        await AcceptanceInfrastructure.WriteJsonAsync(Path.Combine(_runDirectory, "process-restart.json"),
            new { catalog = ReusableSqlServerDatabase.Catalog, previousRun, restartedRun = _devRun, rootIdentities = expected }, token);
    }

    private static string[] RootIdentities(JsonElement roots) => roots.EnumerateArray().Select(root =>
        $"{root.GetProperty("spaceKind").GetString()}/{root.GetProperty("spaceId").GetString()}/{root.GetProperty("rootFolderId").GetString()}")
        .Order(StringComparer.Ordinal).ToArray();

    // 采集失败不能覆盖原异常；失败响应、路由和页面文本脱敏后写入，截图屏蔽输入值。
    private async Task CaptureBrowserFailureAsync(IPage page, WorkspacePageDriver driver,
        ConcurrentQueue<string> errors, ConcurrentQueue<IResponse> failedResponses, Exception failure)
    {
        try
        {
            var diagnostics = new List<string>(errors);
            foreach (var response in failedResponses)
            {
                try { diagnostics.Add($"{response.Request.Method} {response.Url}: {response.Status} {await response.TextAsync()}"); }
                catch (Exception exception) { diagnostics.Add($"Response body unavailable: {exception.Message}"); }
            }
            await AcceptanceInfrastructure.WriteJsonAsync(Path.Combine(_runDirectory, "browser-failure.json"), new
            {
                url = page.Url,
                diagnostic = Sanitize(failure.ToString()),
                errors = diagnostics.Select(Sanitize).ToArray(),
                page = Sanitize(await driver.ReadPageDiagnosticAsync()),
                dom = Sanitize(await page.Locator("body").InnerTextAsync())
            });
            await page.ScreenshotAsync(new() { Path = Path.Combine(_runDirectory, "browser-failure.png"), FullPage = true, Mask = [page.Locator("input,textarea")] });
        }
        catch (Exception) { /* 诊断采集不得覆盖原始浏览器失败。 */ }
    }

    // 复用正式 Host ownership marker，只有 marker 与本轮 supervisor 身份一致才归还专用 schema。
    private async Task<string> ReadOwnershipAsync(CancellationToken token)
    {
        await using var db = new SqlConnection(_connection);
        await db.OpenAsync(token);
        await using var command = db.CreateCommand();
        command.CommandText = "IF OBJECT_ID(N'[dbo].[__NewPDatabaseOwnership]',N'U') IS NULL BEGIN IF EXISTS (SELECT 1 FROM sys.objects WHERE is_ms_shipped=0) THROW 51000,N'Unmarked catalog is not empty',1; SELECT N'owned-empty'; END ELSE SELECT [Version],[RepositoryId],[WorktreeId],[Provider],[ServerFingerprint],[CatalogFingerprint] FROM [dbo].[__NewPDatabaseOwnership] WHERE [MarkerId]=1 FOR JSON PATH";
        return (string?)await command.ExecuteScalarAsync(token) ?? throw new InvalidOperationException("Catalog ownership missing.");
    }

    protected override async Task<AcceptanceCleanupResult> TeardownAsync(CancellationToken token)
    {
        try { return await TeardownResourcesAsync(token); }
        finally
        {
            if (_databaseLease is not null)
            {
                await _databaseLease.ReleaseAsync();
                _databaseLease = null;
            }
        }
    }

    private async Task<AcceptanceCleanupResult> TeardownResourcesAsync(CancellationToken token)
    {
        if (_databaseLease is null) return new("completed", null);
        _ownership ??= await ReadOwnershipAsync(token);
        if (_ownership != await ReadOwnershipAsync(token)) return new("failed", AcceptanceFailureCodes.OwnershipMismatch);
        if (_devRun is not null)
        {
            using var state = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(_repositoryRoot, ".codex-temp", "dev", "state.json"), token));
            if (state.RootElement.GetProperty("runId").GetString() != _devRun) return new("failed", AcceptanceFailureCodes.OwnershipMismatch);
            var stop = await AcceptanceInfrastructure.RunProcessAsync("pwsh.exe",
                ["-NoLogo", "-NoProfile", "-File", Path.Combine(_repositoryRoot, "scripts", "stop-dev.ps1")], _repositoryRoot, cancellationToken: token);
            if (stop.ExitCode != 0) return new("failed", AcceptanceFailureCodes.CleanupFailed, Sanitize(stop.StandardOutput + stop.StandardError));
            _devRun = null;
        }
        await _databaseLease.DisposeAsync();
        _databaseLease = null;
        var resources = Path.Combine(_runDirectory, "resources");
        if (Directory.Exists(resources))
        {
            foreach (var directory in Directory.EnumerateDirectories(resources))
            {
                AcceptanceInfrastructure.EnsureOwnedResourceRoot(_runDirectory, directory);
                Directory.Delete(directory, recursive: true);
            }
            Directory.Delete(resources, recursive: false);
        }
        await AcceptanceInfrastructure.WriteJsonAsync(Path.Combine(_runDirectory, "cleanup.json"),
            new { catalog = ReusableSqlServerDatabase.Catalog, schemaCleaned = true, dropped = false }, token);
        return new("completed", null);
    }
}
