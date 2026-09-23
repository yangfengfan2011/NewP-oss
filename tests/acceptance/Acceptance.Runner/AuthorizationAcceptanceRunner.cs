using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.Playwright;
namespace ICP.Platform.Acceptance;

/// <summary>Authorization 的唯一真实用户旅程和一次性资源 owner。</summary>
internal sealed class AuthorizationAcceptanceRunner(string repositoryRoot, string runDirectory, string runId)
    : AcceptanceRunner(repositoryRoot, runDirectory, runId)
{
    private string? _adminConnection;
    private string? _connection;
    private string? _catalog;
    private string? _ownership;
    private string? _devRun;
    private bool _created;
    private readonly Dictionary<string, string?> _environment = new();

    /// <inheritdoc/>
    protected override async Task RunScenariosAsync(CancellationToken cancellationToken)
    {
        await RunScenarioAsync("authorization.startup-and-read", StartAndReadAsync, cancellationToken);
    }

    // 新空库由正式 Host 维护命令运行全部 migration，随后才进入真实 HTTP 工作台。
    private async Task StartAndReadAsync(CancellationToken token)
    {
        var connection = AcceptanceSqlServerConnectionResolver.Resolve(_repositoryRoot);
        AddConnectionSecrets(connection.ConfiguredConnectionString, connection.Password);
        _adminConnection = connection.AdminConnectionString;
        _catalog = $"NewP_acceptance_authorization_{_runId}";
        if (!System.Text.RegularExpressions.Regex.IsMatch(_catalog, "^NewP_acceptance_authorization_[0-9a-f]{24}$"))
            throw new InvalidOperationException("Invalid owned catalog name.");
        _connection = new SqlConnectionStringBuilder(_adminConnection) { InitialCatalog = _catalog, Pooling = false }.ConnectionString;
        await using (var db = new SqlConnection(_adminConnection))
        {
            await db.OpenAsync(token);
            await using var create = db.CreateCommand();
            create.CommandText = $"CREATE DATABASE [{_catalog}];";
            await create.ExecuteNonQueryAsync(token);
            _created = true;
        }
        _environment["ConnectionStrings__SqlServer"] = _connection;
        _environment["NEWP_DEV_SQLSERVER_CONNECTION_STRING"] = _connection;
        _environment["Platform__PluginInstallRoot"] = Path.Combine(_runDirectory, "resources", "plugins");
        _environment["Authentication__Host__KeyRingPath"] = Path.Combine(_runDirectory, "resources", "key-ring");
        _environment["Authentication__Host__CookieName"] = "ICP.Platform.AuthorizationAcceptance";
        _environment["Authentication__Host__AllowInsecureDevelopmentCookie"] = "true";
        _environment["Identity__LocalAdministrator__ECODE"] = "admin";
        _environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        var bootstrap = await FormalIdentityBootstrap.RunAsync(_repositoryRoot, Path.Combine(_runDirectory, "resources", "bootstrap"), _environment, token);
        RegisterSecret(bootstrap.TemporaryCredential);
        _ownership = await ReadOwnershipAsync(token);
        var started = await AcceptanceInfrastructure.RunProcessAsync("pwsh.exe",
            ["-NoLogo", "-NoProfile", "-File", Path.Combine(_repositoryRoot, "scripts", "dev.ps1"),
             "-HostEnvironmentName", "Acceptance", "-SkipDependencyInstall", "-HostStartupTimeoutSeconds", "180"],
            _repositoryRoot, _environment, redirectOutput: false, cancellationToken: token);
        using var state = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(_repositoryRoot, ".codex-temp", "dev", "state.json"), token));
        _devRun = state.RootElement.GetProperty("runId").GetString();
        if (started.ExitCode != 0) throw new InvalidOperationException($"Managed dev failed; inspect .codex-temp/dev/runs/{_devRun}.");
        var port = state.RootElement.GetProperty("ports").GetProperty("host").GetInt32();
        using var client = new HttpClient(new HttpClientHandler { CookieContainer = new CookieContainer() }) { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
        using var login = await PostAsync(client, "/api/auth/login", new { ecode = "admin", password = bootstrap.TemporaryCredential }, token);
        var revision = login.RootElement.GetProperty("revision").GetInt64();
        var password = "Aa1!" + Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        RegisterSecret(password);
        using var changed = await PostAsync(client, "/api/identity/me/password-change", new { currentPassword = bootstrap.TemporaryCredential, newPassword = password, expectedRevision = revision }, token);
        var features = await AcceptanceFeatureCatalog.LoadAsync(client, token);
        using var readRequest = new HttpRequestMessage(HttpMethod.Get, "/api/authorization/workbench/catalog");
        readRequest.Headers.Add("X-NewP-Feature", features.Behavior("Authorization", "authorization.workbench.read").Position);
        using var response = await client.SendAsync(readRequest, token);
        await EnsureSuccessAsync(response, token);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
        if (body.RootElement.GetProperty("points").GetArrayLength() == 0) throw new InvalidOperationException("Workbench catalog is empty.");
        var roleName = $"授权旅程-{_runId[..8]}";
        var roleCreate = features.Action(features.Behavior("Authorization", "authorization.role.read"), "create", "list.pageActions");
        using var role = await PostAsync(client, "/api/authorization/roles", new { Code = "AUTH-" + _runId[..8], Name = roleName }, token,
            request => request.Headers.Add("X-NewP-Feature", roleCreate.Position));
        var roleId = role.RootElement.GetProperty("id").GetString()
            ?? throw new InvalidOperationException("Role creation omitted id.");
        var department = await PrepareDepartmentSubjectsAsync(client, features, token);
        var shellPort = state.RootElement.GetProperty("ports").GetProperty("shell").GetInt32();
        await RunScenarioAsync("authorization.browser", _ => RunBrowserAsync(shellPort, password, roleId, roleName, department), token);
        await AcceptanceInfrastructure.WriteJsonAsync(Path.Combine(_runDirectory, "startup-read.json"), new { catalog = _catalog, devRun = _devRun, formalMigration = true, workbenchRead = true }, token);
    }

    // 唯一浏览器旅程只观察进入、读取、提交和用户可见结果；授权值和错误边界在 API 层验证。
    private async Task RunBrowserAsync(int port, string password, string roleId, string roleName, DataScopeDepartmentSelection department)
    {
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new()
        {
            Headless = true,
            ExecutablePath = Environment.GetEnvironmentVariable("NEWP_ACCEPTANCE_CHROMIUM_EXECUTABLE") ?? @"C:\Program Files\Google\Chrome\Application\chrome.exe"
        });
        var context = await browser.NewContextAsync(new() { Locale = "zh-CN", ViewportSize = new() { Width = 1440, Height = 1000 } });
        var page = await context.NewPageAsync();
        var browserErrors = new System.Collections.Concurrent.ConcurrentQueue<string>();
        var failedResponses = new System.Collections.Concurrent.ConcurrentQueue<IResponse>();
        page.PageError += (_, message) => browserErrors.Enqueue(message);
        page.RequestFailed += (_, request) => browserErrors.Enqueue($"{request.Method} {request.Url}: {request.Failure}");
        page.Response += (_, response) => { if (response.Status >= 400) failedResponses.Enqueue(response); };
        page.SetDefaultTimeout(30_000);
        try
        {
            await new ShellPageDriver(page, $"http://127.0.0.1:{port}").LoginAsync("admin", password);
            var workbench = new AuthorizationWorkbenchPageDriver(page);
            await page.Locator(".np-sidebar-menu").GetByRole(AriaRole.Link, new() { Name = "角色管理", Exact = true }).ClickAsync();
            var row = await new StandardCrudPageDriver(page).WaitForRowAsync(roleName);
            await page.ScreenshotAsync(new() { Path = Path.Combine(_runDirectory, "role-list-baseline.png"), FullPage = true });
            await row.GetByRole(AriaRole.Button, new() { Name = "更多", Exact = true }).ClickAsync();
            await page.GetByRole(AriaRole.Menuitem, new() { Name = "配置权限", Exact = true }).ClickAsync();
            var roleCode = "AUTH-" + _runId[..8];
            await workbench.WaitForCurrentRoleTitleAsync(roleName, roleCode);
            await workbench.ClickFirstVisibleFeatureCheckboxAsync();
            await workbench.SaveAsync(roleId);
            await workbench.WaitForRoleButtonEnabledAsync(roleName, roleCode);
            await workbench.WaitForSavePresentationFinishedAsync();
            await page.ScreenshotAsync(new() { Path = Path.Combine(_runDirectory, "workbench.png"), FullPage = true });
            await page.SetViewportSizeAsync(900, 1000);
            await page.ScreenshotAsync(new() { Path = Path.Combine(_runDirectory, "workbench-narrow.png"), FullPage = true });
            await page.SetViewportSizeAsync(1440, 1000);
            var rules = new DataScopeRulesPageDriver(page);
            await rules.CreatePersonRuleAsync("人员读取规则", roleName, department,
                () => page.ScreenshotAsync(new() { Path = Path.Combine(_runDirectory, "department-subject-candidates.png"), FullPage = true }));
            await rules.ReloadRuleAsync("人员读取规则", roleName, department);
            await page.ScreenshotAsync(new() { Path = Path.Combine(_runDirectory, "data-scope-rules.png"), FullPage = true });
            await page.SetViewportSizeAsync(900, 1000);
            await page.ScreenshotAsync(new() { Path = Path.Combine(_runDirectory, "data-scope-rules-narrow.png"), FullPage = true });
        }
        catch (Exception failure)
        {
            try
            {
                var diagnostics = new List<string>(browserErrors);
                foreach (var response in failedResponses)
                {
                    try { diagnostics.Add($"{response.Request.Method} {response.Url}: {response.Status} {await response.TextAsync()}"); }
                    catch (Exception exception) { diagnostics.Add($"Failed response body unavailable: {exception.Message}"); }
                }
                await AcceptanceInfrastructure.WriteJsonAsync(Path.Combine(_runDirectory, "browser-failure.json"), new { url = page.Url, diagnostic = Sanitize(failure.ToString()), errors = diagnostics.Select(Sanitize), dom = Sanitize(await page.Locator("body").InnerTextAsync()) });
                await page.ScreenshotAsync(new() { Path = Path.Combine(_runDirectory, "browser-failure.png"), FullPage = true, Mask = [page.Locator("input,textarea")] });
            }
            catch (Exception) { /* 诊断采集不得覆盖原始浏览器失败。 */ }
            throw;
        }
    }

    // 用正式 API 创建同名组织主体，目录展示只使用其最小路径；不直接写库或新建语义旅程。
    private async Task<DataScopeDepartmentSelection> PrepareDepartmentSubjectsAsync(HttpClient client,
        AcceptanceFeatureCatalog features, CancellationToken token)
    {
        var createCompany = features.Action(features.OrganizationTab("companies"), "create", "list.pageActions");
        var createDepartment = features.Action(features.OrganizationTab("departments"), "create", "list.pageActions");
        var firstCompanyName = $"甲公司{_runId[..6]}";
        foreach (var (key, name) in new[] { ("A", firstCompanyName), ("B", $"乙公司{_runId[..6]}") })
        {
            using var company = await PostAsync(client, "/api/organization/companies",
                new { ECODE = $"AUTH-{key}-{_runId[..8]}", ENAME = name, SortOrder = "0100" }, token,
                request => request.Headers.Add("X-NewP-Feature", createCompany.Position));
            var companyId = company.RootElement.GetProperty("fields").GetProperty("EID").GetString();
            using var root = await PostAsync(client, "/api/organization/departments",
                new { ECODE = "ROOT", ENAME = "总部", CompanyId = companyId, SortOrder = "0100" }, token,
                request => request.Headers.Add("X-NewP-Feature", createDepartment.Position));
            var rootId = root.RootElement.GetProperty("fields").GetProperty("EID").GetString();
            using var leaf = await PostAsync(client, "/api/organization/departments",
                new { ECODE = "TEAM", ENAME = "研发部", CompanyId = companyId, ParentDepartmentId = rootId, SortOrder = "0100" }, token,
                request => request.Headers.Add("X-NewP-Feature", createDepartment.Position));
        }
        return new("研发部", $"{firstCompanyName} / 总部 / 研发部");
    }

    // 保留标准 CRUD 规范字段名并获取同会话防伪令牌；secure loopback cookie 仅由验收客户端回放。
    private static async Task<JsonDocument> PostAsync(HttpClient client, string path, object body, CancellationToken token, Action<HttpRequestMessage>? configure = null)
    {
        using var antiforgery = await client.GetFromJsonAsync<JsonDocument>("/api/auth/antiforgery-token", token);
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body, options: new JsonSerializerOptions()) };
        request.Headers.Add("X-CSRF-TOKEN", antiforgery!.RootElement.GetProperty("requestToken").GetString());
        configure?.Invoke(request);
        using var response = await client.SendAsync(request, token);
        await EnsureSuccessAsync(response, token);
        if (response.Headers.TryGetValues("Set-Cookie", out var cookies))
        {
            var cookie = cookies.Select(value => value.Split(';', 2)[0]).SingleOrDefault(value => value.StartsWith("ICP.Platform.AuthorizationAcceptance=", StringComparison.Ordinal));
            if (cookie is not null) { client.DefaultRequestHeaders.Remove("Cookie"); client.DefaultRequestHeaders.Add("Cookie", cookie); }
        }
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
    }

    // 保留实际 HTTP 失败体，由外层统一脱敏，不将启动器症状当根因。
    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken token)
    {
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"HTTP {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(token)}");
    }

    // 读取 Host 正式 ownership marker；不以自建替代标记授权清理。
    private async Task<string> ReadOwnershipAsync(CancellationToken token)
    {
        await using var db = new SqlConnection(_connection);
        await db.OpenAsync(token);
        await using var command = db.CreateCommand();
        command.CommandText = "IF OBJECT_ID(N'[dbo].[__NewPDatabaseOwnership]',N'U') IS NULL BEGIN IF EXISTS (SELECT 1 FROM sys.objects WHERE is_ms_shipped=0) THROW 51000,N'Unmarked catalog is not empty',1; SELECT N'owned-empty'; END ELSE SELECT [Version],[RepositoryId],[WorktreeId],[Provider],[ServerFingerprint],[CatalogFingerprint] FROM [dbo].[__NewPDatabaseOwnership] WHERE [MarkerId]=1 FOR JSON PATH";
        return (string?)await command.ExecuteScalarAsync(token) ?? throw new InvalidOperationException("Catalog ownership missing.");
    }

    /// <inheritdoc/>
    protected override async Task<AcceptanceCleanupResult> TeardownAsync(CancellationToken token)
    {
        if (!_created) return new("completed", null);
        _ownership ??= await ReadOwnershipAsync(token);
        if (_ownership != await ReadOwnershipAsync(token)) return new("failed", AcceptanceFailureCodes.OwnershipMismatch);
        if (_devRun is not null)
        {
            using var state = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(_repositoryRoot, ".codex-temp", "dev", "state.json"), token));
            if (state.RootElement.GetProperty("runId").GetString() != _devRun) return new("failed", AcceptanceFailureCodes.OwnershipMismatch);
            var stop = await AcceptanceInfrastructure.RunProcessAsync("pwsh.exe", ["-NoLogo", "-NoProfile", "-File", Path.Combine(_repositoryRoot, "scripts", "stop-dev.ps1")], _repositoryRoot, cancellationToken: token);
            if (stop.ExitCode != 0) return new("failed", AcceptanceFailureCodes.CleanupFailed, Sanitize(stop.StandardOutput + stop.StandardError));
        }
        await using var db = new SqlConnection(_adminConnection);
        await db.OpenAsync(token);
        await using var drop = db.CreateCommand();
        drop.CommandText = $"ALTER DATABASE [{_catalog}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_catalog}];";
        await drop.ExecuteNonQueryAsync(token);
        _created = false;
        var resources = Path.GetFullPath(Path.Combine(_runDirectory, "resources"));
        if (!resources.StartsWith(Path.GetFullPath(_runDirectory) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Resource root escaped run directory.");
        if (Directory.Exists(resources)) Directory.Delete(resources, recursive: true);
        await AcceptanceInfrastructure.WriteJsonAsync(Path.Combine(_runDirectory, "cleanup.json"), new { catalog = _catalog, dropped = true, marker = _ownership == "owned-empty" ? "created-empty" : "verified" }, token);
        return new("completed", null);
    }
}
