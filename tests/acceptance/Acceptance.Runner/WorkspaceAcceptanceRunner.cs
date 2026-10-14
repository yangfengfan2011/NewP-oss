using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using ICP.Platform.Testing;
using ICP.Platform.Kernel.ModulePlanning;
using Microsoft.Data.SqlClient;
using Microsoft.Playwright;

namespace ICP.Platform.Acceptance;

/// <summary>Workspace 的同一正式浏览器旅程及其专用测试库、Host 和凭据生命周期。</summary>
internal sealed class WorkspaceAcceptanceRunner(string repositoryRoot, string runDirectory, string runId, bool includeSpaceLifecycle = true,
    Func<IPage, CancellationToken, Task>? browserJourney = null,
    Func<AcceptanceFeatureCatalog, IReadOnlyList<FeaturePoint>>? selectFeatures = null,
    Func<HttpClient, AcceptanceFeatureCatalog, WorkspaceAcceptanceActor, CancellationToken, Task>? prepareJourney = null,
    PublishedComponentAcceptanceFixture? publishedFixture = null,
    bool includeBrowser = true)
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
    private string? _publishedShellOrigin;
    private string PartCode => $"WS-P-{_runId[..8]}";
    private string PartName => $"混合零组件旅程 {_runId[..8]}";
    private string DocumentCode => $"WS-D-{_runId[..8]}";
    private string DocumentName => $"混合图文档旅程 {_runId[..8]}";

    protected override async Task RunScenariosAsync(CancellationToken token)
    {
        await RunScenarioAsync("workspace.formal-initialization", PrepareAsync, token);
        if (includeBrowser) await RunScenarioAsync("workspace.browser", RunBrowserAsync, token);
        if (browserJourney is null && includeSpaceLifecycle) await RunScenarioAsync("workspace.process-restart", VerifyProcessRestartAsync, token);
    }

    // 空 schema 只由正式 Host bootstrap/migration 建立，普通 actor 的初次个人初始化留给真实页面入口。
    private async Task PrepareAsync(CancellationToken token)
    {
        if (publishedFixture is not null)
        {
            var target = await publishedFixture.PrepareAsync(RegisterSecret, AddConnectionSecrets, Sanitize, token);
            _actor = target.Actor;
            _publishedShellOrigin = target.ShellOrigin;
            _hostPort = target.HostPort;
            _entryPosition = target.EntryPosition;
            return;
        }
        _databaseLease = await ReusableSqlServerDatabase.AcquireAsync(_repositoryRoot, token);
        _connection = _databaseLease.ConnectionString;
        AddConnectionSecrets(_connection, new SqlConnectionStringBuilder(_connection).Password);
        _environment["ConnectionStrings__SqlServer"] = _connection;
        _environment["NEWP_DEV_SQLSERVER_CONNECTION_STRING"] = _connection;
        _environment["Platform__PluginInstallRoot"] = Path.Combine(_runDirectory, "resources", "plugins");
        _environment["Authentication__Host__KeyRingPath"] = Path.Combine(_runDirectory, "resources", "key-ring");
        _environment["Files__StorageRoot"] = Path.Combine(_runDirectory, "resources", "storage");
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
        await WorkspaceAcceptanceFixture.VerifyInforCenterCompositionAsync(administrator, token);
        var features = await AcceptanceFeatureCatalog.LoadAsync(administrator, token);
        IReadOnlyList<FeaturePoint> grantedFeatures;
        if (selectFeatures is not null) grantedFeatures = selectFeatures(features);
        else
        {
            var entry = features.Behavior("Workspace", "workspace.spaces.access");
            var part = features.Behavior("Part", "part.part.read");
            var document = features.Behavior("Document", "document.document.read");
            grantedFeatures = [entry, features.Action(entry, "create", "list.pageActions", "folder"),
                features.Action(entry, "createPart", "list.pageActions", "folder"), features.Action(entry, "createDocument", "list.pageActions", "folder"), features.Action(entry, "remove", "list.rowActions", "folder"),
                features.Action(entry, "edit", "list.rowActions", "folder"), features.Action(entry, "delete", "list.rowActions", "folder"),
                features.Action(entry, "deleteObjects", "list.pageActions", "folder"), part, document,
                features.Behavior("Document", "document.document.download")];
        }
        if (browserJourney is null)
        {
            _entryPosition = features.Behavior("Workspace", "workspace.spaces.access").Position;
            await WorkspaceAcceptanceFixture.SendAsync(administrator, HttpMethod.Post,
                "/api/workspace/spaces/public/initialize", null, _entryPosition, token);
        }
        _actor = await WorkspaceAcceptanceFixture.PrepareAsync(administrator, features, _runId, RegisterSecret, grantedFeatures, token);

        if (prepareJourney is not null) await prepareJourney(administrator, features, _actor, token);
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
        var frontendResponses = new ConcurrentQueue<object>();
        page.Console += (_, message) => { if (message.Type is "error" or "warning") errors.Enqueue(message.Text); };
        page.PageError += (_, error) => errors.Enqueue(error);
        page.RequestFailed += (_, request) => errors.Enqueue($"{request.Method} {request.Url}: {request.Failure}");
        page.Response += (_, response) => { if (response.Status >= 400) failedResponses.Enqueue(response); };
        if (publishedFixture is not null) page.Response += (_, response) =>
        {
            var path = new Uri(response.Url).AbsolutePath;
            if (path.StartsWith("/plugins/runtime/", StringComparison.Ordinal)
                || path.StartsWith("/shared-runtime/", StringComparison.Ordinal))
                frontendResponses.Enqueue(new { path, response.Status });
        };
        page.SetDefaultTimeout(30_000);
        page.SetDefaultNavigationTimeout(120_000);
        var driver = new WorkspacePageDriver(page);
        try
        {
            token.ThrowIfCancellationRequested();
            await new ShellPageDriver(page, _publishedShellOrigin ?? $"http://127.0.0.1:{_shellPort}").LoginAsync(actor.ECode, actor.Password);
            if (browserJourney is not null)
            {
                await browserJourney(page, token);
                return;
            }
            await driver.OpenAsync();
            await driver.SelectRootAsync("公共空间");
            if (includeSpaceLifecycle)
            {
                await CapturePresentationAsync(page, "public-root.png");
                await driver.SelectRootAsync("个人空间");
                await CapturePresentationAsync(page, "personal-root.png");
                await driver.SelectRootAsync("公共空间");
            }
            var folderName = $"工作空间旅程文件夹 {_runId[..8]}";
            await driver.CreateFolderAsync(folderName);
            await driver.CreateObjectAsync("零组件", PartCode, PartName);
            var attachments = WorkspacePageDriver.SmallAttachmentFiles();
            await driver.CreateDocumentWithFilesAsync(DocumentCode, DocumentName, WorkspacePageDriver.SmallMainFile(), attachments: attachments);
            await driver.ExpandRootToFolderAsync("公共空间", folderName);
            var folderAddress = await driver.SelectTreePathAsync("公共空间", folderName);
            await driver.SelectRootAsync("个人空间");
            await driver.RestorePositionAsync(folderAddress, "公共空间", folderName);
            await driver.SelectRootAsync("公共空间");
            await CapturePresentationAsync(page, "folder-content.png");
            var properties = await driver.OpenFolderPropertiesAsync(folderName);
            await CapturePresentationAsync(page, "folder-properties.png");
            await page.SetViewportSizeAsync(900, 1000);
            await CapturePresentationAsync(page, "folder-properties-narrow.png");
            var references = await driver.OpenDirectReferencesAsync(properties, "公共空间");
            var referencingFolder = await driver.OpenReferencingFolderPropertiesAsync(references, "公共空间");
            await driver.ClosePropertiesAsync(referencingFolder);
            await driver.CloseDirectReferencesAsync(references);
            await driver.ClosePropertiesAsync(properties);
            await driver.OpenOwnedPropertiesAsync(PartName, "/parts");
            references = await driver.OpenDirectReferencesAsync(page.GetByRole(AriaRole.Main), "公共空间");
            await driver.CloseDirectReferencesAsync(references);
            await driver.ReturnToPublicContentAsync();
            await driver.OpenOwnedPropertiesAsync(DocumentName, "/documents");
            foreach (var file in new[] { WorkspacePageDriver.SmallMainFile() }.Concat(attachments))
                await driver.DownloadFileAsync(file.Name, Path.Combine(_runDirectory, "download-" + file.Name));
            await driver.ReturnToPublicContentAsync();
            await driver.RemoveReferenceAsync(PartName);
            await driver.CreateObjectAsync("零组件", PartCode + "-NEXT", PartName + " 新建");
            await driver.ExpandRootToFolderAsync("公共空间", folderName);
            await CapturePresentationAsync(page, "folder-content-narrow.png");
            await driver.EditObjectAsync(folderName, folderName + " 已编辑");
            await driver.EditObjectAsync(DocumentName, DocumentName + " 已编辑");
            await driver.EditObjectAsync(PartName + " 新建", PartName + " 已编辑");
            await CapturePresentationAsync(page, "folder-content-edited.png");
            await driver.DeleteSelectedAsync(folderName + " 已编辑", DocumentName + " 已编辑", PartName + " 已编辑");
            await AcceptanceInfrastructure.WriteJsonAsync(Path.Combine(_runDirectory, "browser.json"),
                new { url = page.Url, actor = "ordinary", createdFolder = folderName, includeSpaceLifecycle }, token);
        }
        catch (Exception failure)
        {
            await CaptureBrowserFailureAsync(page, driver, errors, failedResponses, failure);
            throw;
        }
        finally
        {
            // 失败同样保留已消费的 URL；辅助诊断失败不能覆盖真实旅程异常。
            try
            {
                if (publishedFixture is not null)
                    await AcceptanceInfrastructure.WriteJsonAsync(Path.Combine(_runDirectory, "frontend-load.json"), frontendResponses.ToArray());
            }
            catch (Exception) { /* 原始场景异常和统一凭据扫描仍由 AcceptanceRunner 处理。 */ }
        }
    }

    private Task CapturePresentationAsync(IPage page, string name) => page.ScreenshotAsync(new()
    {
        Path = Path.Combine(_runDirectory, name), FullPage = true, Animations = ScreenshotAnimations.Disabled
    });

    // 独立准备重启样本，再在同一 catalog 跨真实 Host 重启比较空间、根与混合关系；重启后只读，不补建内容。
    private async Task VerifyProcessRestartAsync(CancellationToken token)
    {
        var actor = _actor ?? throw new InvalidOperationException("Workspace 普通主体尚未准备。");
        using var beforeClient = WorkspaceAcceptanceFixture.CreateClient(_hostPort);
        await WorkspaceAcceptanceFixture.LoginAsync(beforeClient, actor, token);
        var before = await WorkspaceAcceptanceFixture.SendAsync(beforeClient, HttpMethod.Get, "/api/workspace/spaces", null, _entryPosition, token);
        var features = await AcceptanceFeatureCatalog.LoadAsync(beforeClient, token);
        await WorkspaceAcceptanceFixture.PrepareRestartContentAsync(beforeClient, features, before, _runId, token);
        var beforeContents = await WorkspaceAcceptanceFixture.ReadRestartContentAsync(beforeClient, before, _entryPosition!, token);
        var previousRun = publishedFixture?.HostIdentity ?? _devRun;
        if (publishedFixture is not null) await publishedFixture.RestartAsync(token);
        else
        {
            using (var state = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(_repositoryRoot, ".codex-temp", "dev", "state.json"), token)))
            {
                if (state.RootElement.GetProperty("runId").GetString() != _devRun)
                    throw new InvalidOperationException("重启前 supervisor 归属已改变。");
            }
            var stop = await AcceptanceInfrastructure.RunProcessAsync("pwsh.exe",
                ["-NoLogo", "-NoProfile", "-File", Path.Combine(_repositoryRoot, "scripts", "stop-dev.ps1")], _repositoryRoot, cancellationToken: token);
            if (stop.ExitCode != 0) throw new InvalidOperationException($"正式 Host 重启停止失败：{Sanitize(stop.StandardOutput + stop.StandardError)}");
            _devRun = null;
            await StartDevAsync(token);
        }
        using var afterClient = WorkspaceAcceptanceFixture.CreateClient(_hostPort);
        await WorkspaceAcceptanceFixture.LoginAsync(afterClient, actor, token);
        var afterRead = await WorkspaceAcceptanceFixture.SendAsync(afterClient, HttpMethod.Get, "/api/workspace/spaces", null, _entryPosition, token);
        await WorkspaceAcceptanceFixture.SendAsync(afterClient, HttpMethod.Post, "/api/workspace/spaces/personal/ensure", null, _entryPosition, token);
        var afterEnsure = await WorkspaceAcceptanceFixture.SendAsync(afterClient, HttpMethod.Get, "/api/workspace/spaces", null, _entryPosition, token);
        var expected = RootIdentities(before);
        if (expected.Length != 2 || !expected.SequenceEqual(RootIdentities(afterRead)) || !expected.SequenceEqual(RootIdentities(afterEnsure)))
            throw new InvalidOperationException("真实 Host 进程重启后空间或根身份发生漂移。");
        var afterContents = await WorkspaceAcceptanceFixture.ReadRestartContentAsync(afterClient, afterEnsure, _entryPosition!, token);
        if (!beforeContents.SequenceEqual(afterContents)) throw new InvalidOperationException("真实 Host 进程重启后混合引用或正式根内容查询发生漂移。");
        await AcceptanceInfrastructure.WriteJsonAsync(Path.Combine(_runDirectory, "process-restart.json"),
            new { catalog = ReusableSqlServerDatabase.Catalog, previousRun, restartedRun = publishedFixture?.HostIdentity ?? _devRun, rootIdentities = expected, contentIdentities = afterContents }, token);
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
                diagnostics.Add(await ReadFailedResponseDiagnosticAsync(response.Request.Method, response.Url, response.Status, response.TextAsync));
            }
            await AcceptanceInfrastructure.WriteJsonAsync(Path.Combine(_runDirectory, "browser-failure.json"), new
            {
                url = page.Url,
                diagnostic = Sanitize(failure.ToString()),
                errors = diagnostics.Select(Sanitize).ToArray(),
                page = Sanitize(await driver.ReadPageDiagnosticAsync()),
                dom = Sanitize(await page.Locator("body").InnerTextAsync()),
                aria = await ReadSanitizedAriaAsync(page.Locator("body"), Sanitize)
            });
            await page.ScreenshotAsync(new() { Path = Path.Combine(_runDirectory, "browser-failure.png"), FullPage = true, Mask = [page.Locator("input,textarea")] });
        }
        catch (Exception) { /* 诊断采集不得覆盖原始浏览器失败。 */ }
    }

    internal static async Task<string> ReadSanitizedAriaAsync(ILocator surface, Func<string?, string> sanitize) =>
        sanitize(await surface.AriaSnapshotAsync());

    // 响应体可能已被浏览器归还；读取失败仍保留请求身份和状态，最终由外层统一脱敏。
    internal static async Task<string> ReadFailedResponseDiagnosticAsync(string method, string url, int status, Func<Task<string>> readBody)
    {
        var request = $"{method} {url}: {status}";
        try { return $"{request} {await readBody()}"; }
        catch (Exception exception) { return $"{request}; Response body unavailable: {exception.Message}"; }
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
        if (publishedFixture is not null) return await publishedFixture.TeardownAsync(token);
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
