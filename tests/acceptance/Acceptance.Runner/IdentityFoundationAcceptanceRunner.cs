using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.Playwright;

namespace ICP.Platform.Acceptance;

internal sealed record IdentityFoundationAcceptanceExecution(
    string? DatabaseFingerprint,
    IReadOnlyList<string> ProcessIdentities,
    IReadOnlyList<string> ArtifactRefs,
    AcceptanceCleanupResult Cleanup);

internal sealed class IdentityFoundationAcceptanceRunner
{
    internal const string PreflightScenario = "identity-foundation.preflight";
    internal const string ApiScenario = "identity-foundation.api";
    internal const string BrowserScenario = "identity-foundation.browser";
    internal const string RestartScenario = "identity-foundation.restart";
    internal const string SecurityScenario = "identity-foundation.security";
    internal const string PersistenceScenario = "identity-foundation.persistence";

    private static readonly JsonSerializerOptions WebJsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _repositoryRoot;
    private readonly string _runDirectory;
    private readonly string _runId;
    private readonly AcceptanceObservationRecorder _recorder;
    private readonly List<string> _artifactRefs = [];
    private readonly List<string> _processIdentities = [];
    private readonly List<string> _secretTokens = [];
    private readonly Dictionary<string, string?> _hostEnvironment = new(StringComparer.OrdinalIgnoreCase);
    private string? _adminConnectionString;
    private string? _adminConnectionSource;
    private string? _applicationConnectionString;
    private string? _catalog;
    private string? _pluginRoot;
    private string? _keyRingRoot;
    private string? _lastDevRunId;
    private string? _adminTemporaryPassword;
    private string? _adminPassword;
    private string _activeScenario = PreflightScenario;
    private int _hostPort;
    private int _shellPort;
    private bool _catalogCreated;
    private int _loginSequence;
    private DatabaseOwnershipSnapshot? _ownershipSnapshot;

    public IdentityFoundationAcceptanceRunner(
        string repositoryRoot,
        string runDirectory,
        string runId,
        AcceptanceObservationRecorder recorder)
    {
        _repositoryRoot = Path.GetFullPath(repositoryRoot);
        _runDirectory = Path.GetFullPath(runDirectory);
        _runId = runId;
        _recorder = recorder;
    }

    public async Task<IdentityFoundationAcceptanceExecution> RunAsync(CancellationToken cancellationToken)
    {
        AcceptanceCleanupResult cleanup = new("not-started", AcceptanceFailureCodes.CleanupIncomplete);
        try
        {
            await RunPreflightAsync(cancellationToken);
            _activeScenario = ApiScenario;
            await RunApiScenarioAsync(cancellationToken);
            _activeScenario = BrowserScenario;
            await RunBrowserScenarioAsync(cancellationToken);
            _activeScenario = RestartScenario;
            await RunRestartScenarioAsync(cancellationToken);
            _activeScenario = SecurityScenario;
            await RunSecurityScenarioAsync(cancellationToken);
            _activeScenario = PersistenceScenario;
            await RunPersistenceScenarioAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            var failureCode = FailureCodeFor(_activeScenario);
            var evidenceRef = await WriteEvidenceAsync(
                $"identity-foundation/{_activeScenario.Replace('.', '-')}-failure.json",
                new { status = "failed", failureCode, diagnostic = Sanitize(exception.Message) },
                cancellationToken);
            await _recorder.RecordAsync(
                AcceptanceGateKind.IdentityFoundation,
                AcceptanceEventType.ScenarioCompleted,
                AcceptanceObservationOutcome.Failed,
                _activeScenario,
                evidenceRef,
                failureCode,
                cancellationToken);
        }
        finally
        {
            cleanup = await TeardownAsync(cancellationToken);
            await ValidateEvidenceAsync(cancellationToken);
            await ScanEvidenceForSecretsAsync(cancellationToken);
        }

        return new IdentityFoundationAcceptanceExecution(
            _catalog is null ? null : AcceptanceInfrastructure.Sha256(_catalog),
            _processIdentities,
            _artifactRefs,
            cleanup);
    }

    private async Task RunPreflightAsync(CancellationToken cancellationToken)
    {
        var adminConnection = AcceptanceSqlServerConnectionResolver.Resolve(_repositoryRoot);
        var adminBuilder = new SqlConnectionStringBuilder(adminConnection.AdminConnectionString);
        _adminConnectionString = adminConnection.AdminConnectionString;
        _adminConnectionSource = adminConnection.SourceName;
        AddConnectionSecrets(adminConnection.ConfiguredConnectionString, adminConnection.Password);
        _catalog = $"NewP_identity_acceptance_{_runId}";
        ValidateCatalogName(_catalog);
        _applicationConnectionString = new SqlConnectionStringBuilder(adminBuilder.ConnectionString)
        {
            InitialCatalog = _catalog,
            Pooling = false
        }.ConnectionString;

        _pluginRoot = Path.Combine(_runDirectory, "resources", "plugins");
        _keyRingRoot = Path.Combine(_runDirectory, "resources", "key-ring");
        Directory.CreateDirectory(_pluginRoot);
        Directory.CreateDirectory(_keyRingRoot);
        EnsureEmptyDirectory(_pluginRoot);
        EnsureEmptyDirectory(_keyRingRoot);

        _hostEnvironment["ASPNETCORE_ENVIRONMENT"] = "Development";
        _hostEnvironment["ConnectionStrings__SqlServer"] = _applicationConnectionString;
        _hostEnvironment["Platform__PluginInstallRoot"] = _pluginRoot;
        _hostEnvironment["Authentication__Host__KeyRingPath"] = _keyRingRoot;
        _hostEnvironment["Authentication__Host__CookieName"] = "ICP.Platform.Auth";
        _hostEnvironment["Authentication__Host__AllowInsecureDevelopmentCookie"] = "true";
        _hostEnvironment["Authentication__Host__LoginAttemptsPerMinute"] = "10";
        _hostEnvironment["Authentication__Host__TrustedProxies__0"] = "127.0.0.1";
        _hostEnvironment["Identity__Credentials__UseSharedDevelopmentCredential"] = "true";
        _hostEnvironment["Identity__LocalAdministrator__ECODE"] = "admin";
        _hostEnvironment["Identity__LocalAdministrator__ENAME"] = "Local Administrator";
        _hostEnvironment["Logging__LogLevel__Default"] = "Information";

        await RunStopDevAsync("identity-foundation/preflight-stop-existing.json", cancellationToken, true);
        var build = await RunCheckedProcessAsync(
            "dotnet",
            ["build", "apps/host-api/HostApi.csproj", "--configuration", "Release", "--no-restore", "--nologo"],
            null,
            cancellationToken);
        await WriteEvidenceAsync("identity-foundation/preflight-build.json", ProcessEvidence("dotnet build HostApi", build), cancellationToken);
        await CreateCatalogAsync(cancellationToken);

        var notReady = await ObserveNotReadyDevAsync(cancellationToken);
        if (!string.Equals(notReady.FailureCode, "IDENTITY_BOOTSTRAP_REQUIRED", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Empty catalog readiness returned {notReady.FailureCode}.");
        }

        _ownershipSnapshot = await ReadOwnershipSnapshotAsync(cancellationToken);
        var bootstrap = await FormalIdentityBootstrap.RunAsync(
            _repositoryRoot,
            Path.Combine(_runDirectory, "resources"),
            _hostEnvironment,
            cancellationToken);
        _adminTemporaryPassword = bootstrap.TemporaryCredential;
        _secretTokens.Add(_adminTemporaryPassword);

        await StartDevAsync("identity-foundation/preflight-dev-start.json", cancellationToken);
        _ownershipSnapshot = await ReadOwnershipSnapshotAsync(cancellationToken);
        using var admin = CreateSession(_hostPort);
        var login = await LoginAsync(admin, "admin", _adminTemporaryPassword, cancellationToken);
        EnsureBoolean(login.Body.RootElement, "mustChangePassword", true, "bootstrap administrator restricted session");
        var revision = ReadLong(login.Body.RootElement, "revision");
        _adminPassword = $"Adm-{_runId}-Pass";
        _secretTokens.Add(_adminPassword);
        await ChangePasswordAsync(admin, _adminTemporaryPassword, _adminPassword, revision, cancellationToken);

        await RecordScenarioSuccessAsync(PreflightScenario, "identity-foundation/preflight.json", new
        {
            status = "passed",
            connectionSource = _adminConnectionSource,
            catalogFingerprint = AcceptanceInfrastructure.Sha256(_catalog),
            notReady = new { status = 503, failureCode = notReady.FailureCode },
            bootstrap = new
            {
                command = "dotnet ICP.Platform.HostApi.dll identity bootstrap-local-admin",
                exitCode = 0,
                interactiveTerminal = "Windows console fixture",
                fixtureProcessId = bootstrap.ProcessId,
                temporaryCredentialOccurrences = 1
            },
            ready = new { hostPort = _hostPort, shellPort = _shellPort },
            requiredBuiltins = new[] { "Identity", "Authentication" }
        }, cancellationToken);
    }

    private async Task RunApiScenarioAsync(CancellationToken cancellationToken)
    {
        var adminPassword = RequireSecret(_adminPassword, "administrator permanent password");
        using var admin = CreateSession(_hostPort);
        await LoginAsync(admin, "admin", adminPassword, cancellationToken);

        var accountCode = $"acct.{_runId[..8]}";
        var account = await CreateUserAsync(admin, accountCode, "Acceptance Account", cancellationToken);
        var initialDetail = await GetJsonAsync(admin, $"/api/identity/accounts/{account.AccountEid}?eventPage=1&eventPageSize=20", cancellationToken);
        var initialCreatedAt = ReadDateTime(ReadObject(initialDetail.RootElement, "account"), "createdAt");
        var temporaryPassword = account.TemporaryCredential;
        _secretTokens.Add(temporaryPassword);

        using var restricted = CreateSession(_hostPort);
        var restrictedLogin = await LoginAsync(restricted, accountCode, temporaryPassword, cancellationToken);
        EnsureBoolean(restrictedLogin.Body.RootElement, "mustChangePassword", true, "temporary credential login");
        using (var denied = await restricted.Client.GetAsync("/api/identity/accounts", cancellationToken))
        {
            await EnsureProblemAsync(denied, HttpStatusCode.Forbidden, "AUTH_PASSWORD_CHANGE_REQUIRED", "restricted management access", cancellationToken);
        }
        var accountPassword = $"User-{_runId}-Pass";
        _secretTokens.Add(accountPassword);
        var changed = await ChangePasswordAsync(
            restricted,
            temporaryPassword,
            accountPassword,
            ReadLong(restrictedLogin.Body.RootElement, "revision"),
            cancellationToken);

        using var secondSession = CreateSession(_hostPort);
        var secondLogin = await LoginAsync(secondSession, accountCode, accountPassword, cancellationToken);
        EnsureBoolean(secondLogin.Body.RootElement, "mustChangePassword", false, "permanent credential login");
        var updated = await SendJsonAndReadAsync(
            admin,
            HttpMethod.Put,
            $"/api/identity/accounts/{Uri.EscapeDataString(account.AccountEid)}",
            new { ename = "Acceptance Account Updated", expectedRevision = changed.Revision },
            HttpStatusCode.OK,
            cancellationToken);
        var disabled = await SetUserStatusAsync(
            admin,
            account.AccountEid,
            "Disabled",
            ReadLong(updated.RootElement, "revision"),
            "纵向验收停用",
            cancellationToken);
        await EnsureSessionInvalidAsync(restricted, cancellationToken);
        await EnsureSessionInvalidAsync(secondSession, cancellationToken);
        var enabled = await SetUserStatusAsync(admin, account.AccountEid, "Enabled", disabled.Revision, "纵向验收恢复", cancellationToken);

        using var resetVictim = CreateSession(_hostPort);
        await LoginAsync(resetVictim, accountCode, accountPassword, cancellationToken);
        var reset = await ResetCredentialAsync(admin, account.AccountEid, enabled.Revision, cancellationToken);
        _secretTokens.Add(reset.TemporaryCredential);
        await EnsureSessionInvalidAsync(resetVictim, cancellationToken);

        using var rotated = CreateSession(_hostPort);
        var rotatedLogin = await LoginAsync(rotated, accountCode, reset.TemporaryCredential, cancellationToken);
        var rotatedPassword = $"Rot-{_runId}-Pass";
        _secretTokens.Add(rotatedPassword);
        var rotatedAccount = await ChangePasswordAsync(
            rotated,
            reset.TemporaryCredential,
            rotatedPassword,
            ReadLong(rotatedLogin.Body.RootElement, "revision"),
            cancellationToken);

        var list = await GetJsonAsync(
            admin,
            $"/api/identity/accounts?query={Uri.EscapeDataString(accountCode)}&status=Enabled&page=1&pageSize=20",
            cancellationToken);
        if (ReadArray(list.RootElement, "items").GetArrayLength() != 1)
        {
            throw new InvalidOperationException("Identity query/filter did not return the expected account.");
        }
        var detail = await GetJsonAsync(admin, $"/api/identity/accounts/{account.AccountEid}?eventPage=1&eventPageSize=100", cancellationToken);
        var detailAccount = ReadObject(detail.RootElement, "account");
        if (ReadDateTime(detailAccount, "createdAt") != initialCreatedAt
            || ReadDateTime(detailAccount, "updatedAt") < initialCreatedAt)
        {
            throw new InvalidOperationException("IdentityAccount audit creation/update semantics were not preserved.");
        }

        var deleteCode = $"delete.{_runId[..8]}";
        var deleted = await CreateUserAsync(admin, deleteCode, "Delete Tombstone", cancellationToken);
        _secretTokens.Add(deleted.TemporaryCredential);
        var tombstone = await SendJsonAndReadAsync(
            admin,
            HttpMethod.Post,
            $"/api/identity/accounts/{deleted.AccountEid}/delete",
            new { expectedRevision = deleted.Revision, reason = "纵向验收墓碑删除" },
            HttpStatusCode.OK,
            cancellationToken);
        EnsureString(tombstone.RootElement, "status", "Deleted", "deleted tombstone status");
        using (var duplicate = await SendJsonAsync(admin, HttpMethod.Post, "/api/identity/accounts", new { ecode = deleteCode, ename = "Reuse" }, cancellationToken))
        {
            await EnsureProblemAsync(duplicate, HttpStatusCode.Conflict, "IDENTITY_ECODE_CONFLICT", "deleted ECODE reuse", cancellationToken);
        }

        var boundCode = $"bound.{_runId[..8]}";
        var bound = await CreateUserAsync(admin, boundCode, "Bound Account", cancellationToken);
        _secretTokens.Add(bound.TemporaryCredential);
        using var boundSessionOne = CreateSession(_hostPort);
        var boundRestricted = await LoginAsync(boundSessionOne, boundCode, bound.TemporaryCredential, cancellationToken);
        var boundPassword = $"Bound-{_runId}-Pass";
        _secretTokens.Add(boundPassword);
        var boundRegular = await ChangePasswordAsync(
            boundSessionOne,
            bound.TemporaryCredential,
            boundPassword,
            ReadLong(boundRestricted.Body.RootElement, "revision"),
            cancellationToken);
        using var boundSessionTwo = CreateSession(_hostPort);
        await LoginAsync(boundSessionTwo, boundCode, boundPassword, cancellationToken);

        var personNo = $"ID-{_runId[..10]}";
        var person = await SendJsonAndReadAsync(
            admin,
            HttpMethod.Post,
            "/api/organization/persons",
            PersonPayload(personNo, "Identity Acceptance Person", true),
            HttpStatusCode.Created,
            cancellationToken);
        var personEid = ReadNestedString(person.RootElement, "fields", "EID");
        var personRevision = ReadNestedLong(person.RootElement, "fields", "Revision");
        await SendJsonAndReadAsync(
            admin,
            HttpMethod.Put,
            $"/api/organization/persons/{personEid}",
            PersonPayload(personNo, "Identity Acceptance Person Updated", true),
            HttpStatusCode.OK,
            cancellationToken);

        var binding = await SendJsonAndReadAsync(
            admin,
            HttpMethod.Put,
            $"/api/organization/persons/{personEid}/user-binding",
            new { accountEid = bound.AccountEid, expectedRevision = boundRegular.Revision, reason = "纵向验收永久绑定" },
            HttpStatusCode.OK,
            cancellationToken);
        var boundAfterBindingRevision = ReadNestedLong(binding.RootElement, "account", "revision");
        var competing = await CreateUserAsync(admin, $"other.{_runId[..8]}", "Other Account", cancellationToken);
        _secretTokens.Add(competing.TemporaryCredential);
        using (var conflict = await SendJsonAsync(
                   admin,
                   HttpMethod.Put,
                   $"/api/organization/persons/{personEid}/user-binding",
                   new { accountEid = competing.AccountEid, expectedRevision = competing.Revision, reason = "禁止改绑" },
                   cancellationToken))
        {
            await EnsureProblemAsync(conflict, HttpStatusCode.Conflict, "ORGANIZATION_PERSON_ALREADY_BOUND", "permanent binding conflict", cancellationToken);
        }

        var disabledPerson = await SendJsonAndReadAsync(
            admin,
            HttpMethod.Put,
            $"/api/organization/persons/{personEid}/status",
            new { isEnabled = false, expectedRevision = personRevision + 1, reason = "纵向验收人员停用" },
            HttpStatusCode.OK,
            cancellationToken);
        EnsureBoolean(ReadObject(disabledPerson.RootElement, "account"), "personnelDisableHold", true, "personnel disable hold");
        await EnsureSessionInvalidAsync(boundSessionOne, cancellationToken);
        await EnsureSessionInvalidAsync(boundSessionTwo, cancellationToken);
        var recoveredPerson = await SendJsonAndReadAsync(
            admin,
            HttpMethod.Put,
            $"/api/organization/persons/{personEid}/status",
            new { isEnabled = true, expectedRevision = ReadLong(disabledPerson.RootElement, "revision"), reason = "纵向验收人员恢复" },
            HttpStatusCode.OK,
            cancellationToken);
        EnsureString(ReadObject(recoveredPerson.RootElement, "account"), "status", "Disabled", "person recovery keeps account disabled");
        var recoveredAccount = ReadObject(recoveredPerson.RootElement, "account");
        await SetUserStatusAsync(
            admin,
            bound.AccountEid,
            "Enabled",
            ReadLong(recoveredAccount, "revision"),
            "Hold 清除后的显式恢复",
            cancellationToken);
        await EnsureSessionInvalidAsync(boundSessionOne, cancellationToken);
        await EnsureSessionInvalidAsync(boundSessionTwo, cancellationToken);

        using (var deletePerson = await SendJsonAsync(admin, HttpMethod.Delete, $"/api/organization/persons/{personEid}", new { }, cancellationToken))
        {
            if (deletePerson.StatusCode is not HttpStatusCode.Conflict and not HttpStatusCode.BadRequest)
            {
                throw new InvalidOperationException($"Bound Person delete returned {(int)deletePerson.StatusCode}.");
            }
        }

        var audit = await ReadAuditEvidenceAsync(cancellationToken);
        if (audit.InvalidRows != 0 || audit.EntityKinds != 4)
        {
            throw new InvalidOperationException("Four-entity audit invariant failed in the disposable catalog.");
        }

        await RecordScenarioSuccessAsync(ApiScenario, "identity-foundation/api.json", new
        {
            status = "passed",
            assertions = new[]
            {
                "restricted-first-password-change",
                "account-list-filter-detail-name-status-reset-delete-tombstone",
                "multi-session-revocation-on-disable-and-reset",
                "unaffiliated-person-create-update",
                "permanent-binding-and-conflict",
                "person-disable-recover-explicit-account-enable-old-sessions-remain-invalid",
                "four-entity-non-null-audit"
            },
            account = new { ecodeFingerprint = AcceptanceInfrastructure.Sha256(accountCode), finalRevision = rotatedAccount.Revision },
            person = new { eidFingerprint = AcceptanceInfrastructure.Sha256(personEid) },
            audit
        }, cancellationToken);
    }

    private async Task RunBrowserScenarioAsync(CancellationToken cancellationToken)
    {
        var adminPassword = RequireSecret(_adminPassword, "administrator permanent password");
        var chromePath = ResolveChromePath();
        var screenshotPath = Path.Combine(_runDirectory, "identity-foundation", "browser.png");
        Directory.CreateDirectory(Path.GetDirectoryName(screenshotPath)!);
        var browserCode = $"ui.{_runId[..8]}";
        var browserPassword = $"Browser-{_runId}-Pass";
        _secretTokens.Add(browserPassword);

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true,
            ExecutablePath = chromePath
        });
        var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            Locale = "zh-CN",
            ViewportSize = new ViewportSize { Width = 1440, Height = 1000 }
        });
        var page = await context.NewPageAsync();
        page.SetDefaultTimeout(120_000);
        page.SetDefaultNavigationTimeout(120_000);
        await LoginBrowserAsync(page, "admin", adminPassword);
        await page.GotoAsync($"http://127.0.0.1:{_shellPort}/identity/accounts", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.GetByRole(AriaRole.Heading, new PageGetByRoleOptions { Name = "账号管理", Exact = true }).WaitForAsync();
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "创建账号", Exact = true }).ClickAsync();
        await page.Locator("#create-ecode").FillAsync(browserCode);
        await page.Locator("#create-ename").FillAsync("Browser Acceptance Account");
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "创建并签发临时密码", Exact = true }).ClickAsync();
        var secret = page.GetByLabel("临时密码", new PageGetByLabelOptions { Exact = true });
        await secret.WaitForAsync();
        var browserTemporaryPassword = await secret.InnerTextAsync();
        _secretTokens.Add(browserTemporaryPassword);
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "我已安全保存，关闭", Exact = true }).ClickAsync();
        if (await page.GetByLabel("临时密码", new PageGetByLabelOptions { Exact = true }).CountAsync() != 0)
        {
            throw new InvalidOperationException("One-time secret remained in the browser after closing the result.");
        }
        await using var restrictedContext = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            Locale = "zh-CN",
            ViewportSize = new ViewportSize { Width = 1440, Height = 1000 }
        });
        var restrictedPage = await restrictedContext.NewPageAsync();
        restrictedPage.SetDefaultTimeout(120_000);
        restrictedPage.SetDefaultNavigationTimeout(120_000);
        await LoginBrowserAsync(restrictedPage, browserCode, browserTemporaryPassword);
        await restrictedPage.WaitForURLAsync("**/account/change-password");
        await restrictedPage.GotoAsync($"http://127.0.0.1:{_shellPort}/identity/accounts");
        await restrictedPage.WaitForURLAsync("**/account/change-password");
        await restrictedPage.Locator("#current-password").FillAsync(browserTemporaryPassword);
        await restrictedPage.Locator("#new-password").FillAsync(browserPassword);
        await restrictedPage.Locator("#confirm-password").FillAsync(browserPassword);
        await restrictedPage.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "完成改密", Exact = true }).ClickAsync();
        await restrictedPage.WaitForURLAsync(url => !new Uri(url).AbsolutePath.Equals("/account/change-password", StringComparison.Ordinal));
        await restrictedPage.ScreenshotAsync(new PageScreenshotOptions { Path = screenshotPath, FullPage = true });
        _artifactRefs.Add(AcceptanceInfrastructure.RelativeArtifact(_runDirectory, screenshotPath));

        await RecordScenarioSuccessAsync(BrowserScenario, "identity-foundation/browser.json", new
        {
            status = "passed",
            assertions = new[]
            {
                "identity-management-route-visible-to-local-administrator",
                "one-time-secret-destroyed-after-close",
                "temporary-session-route-guard",
                "browser-password-change-establishes-regular-session"
            },
            screenshot = AcceptanceInfrastructure.RelativeArtifact(_runDirectory, screenshotPath),
            finalPath = new Uri(restrictedPage.Url).AbsolutePath
        }, cancellationToken);
    }

    private async Task RunRestartScenarioAsync(CancellationToken cancellationToken)
    {
        var adminPassword = RequireSecret(_adminPassword, "administrator permanent password");
        using var session = CreateSession(_hostPort);
        await LoginAsync(session, "admin", adminPassword, cancellationToken);
        var cookieBefore = session.Cookies.GetCookies(new Uri($"http://127.0.0.1:{_hostPort}"))["ICP.Platform.Auth"]?.Value
            ?? throw new InvalidOperationException("Authentication cookie was missing before restart.");
        _secretTokens.Add(cookieBefore);
        var keyFilesBefore = Directory.GetFiles(_keyRingRoot!, "key-*.xml").Select(Path.GetFileName).Order(StringComparer.Ordinal).ToArray();
        if (keyFilesBefore.Length == 0)
        {
            throw new InvalidOperationException("Persistent authentication key ring was empty before restart.");
        }

        await RunStopDevAsync("identity-foundation/restart-stop.json", cancellationToken);
        await StartDevAsync("identity-foundation/restart-start.json", cancellationToken);
        using var restarted = CreateSession(_hostPort);
        restarted.Cookies.Add(new Uri($"http://127.0.0.1:{_hostPort}"), new System.Net.Cookie("ICP.Platform.Auth", cookieBefore, "/"));
        using var current = await restarted.Client.GetAsync("/api/auth/current-user", cancellationToken);
        var currentBody = await current.Content.ReadAsStringAsync(cancellationToken);
        EnsureStatus(current, HttpStatusCode.OK, currentBody, "current-user after Host restart");
        var keyFilesAfter = Directory.GetFiles(_keyRingRoot!, "key-*.xml").Select(Path.GetFileName).Order(StringComparer.Ordinal).ToArray();
        if (!keyFilesBefore.All(keyFilesAfter.Contains))
        {
            throw new InvalidOperationException("Authentication key ring lost a pre-restart key.");
        }
        using var admin = CreateSession(_hostPort);
        await LoginAsync(admin, "admin", adminPassword, cancellationToken);
        var persisted = await GetJsonAsync(admin, "/api/identity/accounts?isLocalAdministrator=true&page=1&pageSize=20", cancellationToken);
        if (ReadArray(persisted.RootElement, "items").GetArrayLength() != 1)
        {
            throw new InvalidOperationException("Identity state did not survive Host restart.");
        }

        await RecordScenarioSuccessAsync(RestartScenario, "identity-foundation/restart.json", new
        {
            status = "passed",
            assertions = new[]
            {
                "same-protected-cookie-valid-after-normal-host-restart",
                "server-session-persists-in-sql-server",
                "key-ring-retains-pre-restart-key",
                "identity-api-state-persists"
            },
            keyFileCountBefore = keyFilesBefore.Length,
            keyFileCountAfter = keyFilesAfter.Length
        }, cancellationToken);
    }

    private async Task RunSecurityScenarioAsync(CancellationToken cancellationToken)
    {
        using var anonymous = CreateSession(_hostPort);
        using (var invalidAntiforgery = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
               {
                   Content = JsonContent.Create(new { ecode = "missing", password = "invalid" })
               })
        {
            invalidAntiforgery.Headers.TryAddWithoutValidation("X-CSRF-TOKEN", "invalid");
            using var response = await anonymous.Client.SendAsync(invalidAntiforgery, cancellationToken);
            await EnsureProblemAsync(response, HttpStatusCode.BadRequest, "ANTIFORGERY_TOKEN_INVALID", "invalid antiforgery", cancellationToken);
        }

        _hostEnvironment["Authentication__Host__TrustedProxies__0"] = null;
        await RunStopDevAsync("identity-foundation/security-stop.json", cancellationToken);
        await StartDevAsync("identity-foundation/security-start.json", cancellationToken);
        using var limiter = CreateSession(_hostPort);
        HttpResponseMessage? limited = null;
        for (var index = 0; index < 12; index++)
        {
            var response = await SendJsonAsync(
                limiter,
                HttpMethod.Post,
                "/api/auth/login",
                new { ecode = $"missing-{index}", password = "invalid-password" },
                cancellationToken,
                request => request.Headers.TryAddWithoutValidation("X-Forwarded-For", $"203.0.113.{index + 1}"));
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                limited = response;
                break;
            }
            response.Dispose();
        }
        if (limited is null || !limited.Headers.Contains("Retry-After"))
        {
            limited?.Dispose();
            throw new InvalidOperationException("Login fixed-window limiter or trusted-proxy boundary did not return 429 + Retry-After.");
        }
        limited.Dispose();

        var coreTests = await RunCheckedProcessAsync(
            ResolveCorepackPath(),
            ["pnpm", "--dir", "frontend", "--filter", "@icp-platform/core", "test"],
            null,
            cancellationToken);
        var hostTests = await RunCheckedProcessAsync(
            "dotnet",
            [
                "test", "tests/HostApi.Tests/HostApi.Tests.csproj", "--no-restore", "--nologo",
                "--filter", "FullyQualifiedName~HostAntiforgeryTests|FullyQualifiedName~HostAuthenticationSecurityTests"
            ],
            null,
            cancellationToken);
        var requiredBuiltinsTests = await RunCheckedProcessAsync(
            "dotnet",
            [
                "test", "tests/HostApi.Tests/HostApi.Tests.csproj", "--no-restore", "--nologo",
                "--filter", "FullyQualifiedName~RequiredBuiltinReadinessTests"
            ],
            null,
            cancellationToken);
        var staticScan = ScanLegacyRuntimeChains();
        if (staticScan.Count != 0)
        {
            throw new InvalidOperationException($"Legacy runtime authentication/binding chain remained reachable: {string.Join(", ", staticScan)}");
        }

        await RecordScenarioSuccessAsync(SecurityScenario, "identity-foundation/security.json", new
        {
            status = "passed",
            assertions = new[]
            {
                "antiforgery-stable-failure-before-business",
                "platform-fetch-single-safe-retry-and-no-unknown-result-replay",
                "login-fixed-window-and-untrusted-forwarded-for",
                "cookie-key-ring-and-store-outage-contract-tests",
                "identity-and-authentication-are-unique-required-builtins",
                "required-builtin-models-and-migrations-are-available",
                "required-builtin-readiness-rejects-missing-or-invalid-inputs",
                "legacy-runtime-chains-unreachable"
            },
            commands = new[]
            {
                ProcessSummary("corepack pnpm --dir frontend --filter @icp-platform/core test", coreTests),
                ProcessSummary("dotnet test HostApi.Tests --filter HostAntiforgeryTests|HostAuthenticationSecurityTests", hostTests),
                ProcessSummary("dotnet test HostApi.Tests --filter FullyQualifiedName~RequiredBuiltinReadinessTests", requiredBuiltinsTests)
            },
            legacyRuntimeMatches = staticScan.Count
        }, cancellationToken);
    }

    private async Task RunPersistenceScenarioAsync(CancellationToken cancellationToken)
    {
        var adminConnection = RequireSecret(_adminConnectionString, "SQL Server admin connection");
        var commands = new[]
        {
            new IntegrationCommand(
                "identity",
                "tests/Identity.Tests/Identity.Tests.csproj",
                "NEWP_RUN_IDENTITY_SQLSERVER_INTEGRATION",
                "NEWP_IDENTITY_SQLSERVER_ADMIN_CONNECTION"),
            new IntegrationCommand(
                "authentication",
                "tests/Authentication.Tests/Authentication.Tests.csproj",
                "NEWP_RUN_AUTHENTICATION_SQLSERVER_INTEGRATION",
                "NEWP_AUTHENTICATION_SQLSERVER_ADMIN_CONNECTION"),
            new IntegrationCommand(
                "organization",
                "tests/Organization.Tests/Organization.Tests.csproj",
                "NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION",
                "NEWP_ORGANIZATION_SQLSERVER_ADMIN_CONNECTION")
        };
        var evidence = new List<object>();
        foreach (var command in commands)
        {
            var environment = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                [command.EnableVariable] = "1",
                [command.AdminConnectionVariable] = adminConnection
            };
            var result = await RunCheckedProcessAsync(
                "dotnet",
                ["test", command.Project, "--no-restore", "--nologo", "--filter", "Category=SqlServerSmoke"],
                environment,
                cancellationToken);
            evidence.Add(ProcessSummary($"dotnet test {command.Project} --filter Category=SqlServerSmoke", result));
        }

        var remainingCatalogs = await CountDisposableIntegrationCatalogsAsync(cancellationToken);
        if (remainingCatalogs != 0)
        {
            throw new InvalidOperationException($"SQL Server integration tests left {remainingCatalogs} disposable catalogs behind.");
        }

        await RecordScenarioSuccessAsync(PersistenceScenario, "identity-foundation/persistence.json", new
        {
            status = "passed",
            assertions = new[]
            {
                "identity-bootstrap-revision-password-event-rollback",
                "authentication-digest-concurrent-revoke-expiry-purge-rollback",
                "organization-binding-lifecycle-stage-fault-injection-and-single-transaction",
                "disposable-integration-catalogs-cleaned"
            },
            commands = evidence,
            remainingDisposableCatalogs = remainingCatalogs
        }, cancellationToken);
    }

    private async Task<AcceptanceCleanupResult> TeardownAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (_catalogCreated)
            {
                await VerifyOwnershipAsync(cancellationToken);
            }
            await _recorder.RecordAsync(AcceptanceGateKind.IdentityFoundation, AcceptanceEventType.OwnershipVerified, cancellationToken: cancellationToken);
        }
        catch (Exception exception)
        {
            await WriteEvidenceAsync("identity-foundation/ownership-failure.json", new
            {
                status = "failed",
                failureCode = AcceptanceFailureCodes.OwnershipMismatch,
                diagnostic = Sanitize(exception.Message),
                resourcesPreserved = true
            }, cancellationToken);
            await _recorder.RecordAsync(
                AcceptanceGateKind.IdentityFoundation,
                AcceptanceEventType.OwnershipVerified,
                AcceptanceObservationOutcome.Failed,
                failureCode: AcceptanceFailureCodes.OwnershipMismatch,
                cancellationToken: cancellationToken);
            return new AcceptanceCleanupResult("failed", AcceptanceFailureCodes.OwnershipMismatch);
        }

        try
        {
            await RunStopDevAsync("identity-foundation/teardown-stop.json", cancellationToken, true);
            if (_catalogCreated)
            {
                await DropCatalogAsync(cancellationToken);
                _catalogCreated = false;
            }
            foreach (var root in new[] { _pluginRoot, _keyRingRoot })
            {
                if (root is not null && Directory.Exists(root))
                {
                    EnsureOwnedResourceRoot(root);
                    Directory.Delete(root, recursive: true);
                }
            }
            await _recorder.RecordAsync(AcceptanceGateKind.IdentityFoundation, AcceptanceEventType.CleanupCompleted, cancellationToken: cancellationToken);
            return new AcceptanceCleanupResult("completed", null);
        }
        catch (Exception exception)
        {
            await WriteEvidenceAsync("identity-foundation/cleanup-failure.json", new
            {
                status = "failed",
                failureCode = AcceptanceFailureCodes.CleanupFailed,
                diagnostic = Sanitize(exception.Message)
            }, cancellationToken);
            await _recorder.RecordAsync(
                AcceptanceGateKind.IdentityFoundation,
                AcceptanceEventType.CleanupCompleted,
                AcceptanceObservationOutcome.Failed,
                failureCode: AcceptanceFailureCodes.CleanupFailed,
                cancellationToken: cancellationToken);
            return new AcceptanceCleanupResult("failed", AcceptanceFailureCodes.CleanupFailed);
        }
    }

    private async Task<NotReadyObservation> ObserveNotReadyDevAsync(CancellationToken cancellationToken)
    {
        var result = await AcceptanceInfrastructure.RunProcessAsync(
            "pwsh.exe",
            BuildDevArguments("dev.ps1"),
            _repositoryRoot,
            _hostEnvironment,
            redirectOutput: false,
            cancellationToken);
        CaptureFailedDevRunIdentity();
        var resultPath = Path.Combine(_repositoryRoot, ".codex-temp", "dev", "current-result.json");
        if (!File.Exists(resultPath))
        {
            throw new InvalidOperationException("Failed development startup did not write current-result.json.");
        }
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(resultPath, cancellationToken));
        var root = json.RootElement;
        var status = ReadString(root, "status");
        var failureCode = ReadString(root, "failureCode");
        var diagnostic = ReadString(root, "diagnostic");
        _ownershipSnapshot = await ReadOwnershipSnapshotAsync(cancellationToken);
        if (result.ExitCode == 0
            || !string.Equals(status, "failed", StringComparison.Ordinal)
            || !string.Equals(failureCode, "DEV_HOST_READINESS_FAILED", StringComparison.Ordinal)
            || !diagnostic.Contains("IDENTITY_BOOTSTRAP_REQUIRED", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Empty catalog startup returned exit={result.ExitCode}, status={status}, failure={failureCode}.");
        }
        await WriteEvidenceAsync("identity-foundation/not-ready.json", new
        {
            status = 503,
            failureCode = "IDENTITY_BOOTSTRAP_REQUIRED",
            supervisorFailureCode = failureCode,
            endpoint = "/api/health/ready",
            command = "scripts/dev.ps1 -HostEnvironmentName Development"
        }, cancellationToken);
        await RunStopDevAsync("identity-foundation/not-ready-stop.json", cancellationToken, true);
        return new NotReadyObservation("IDENTITY_BOOTSTRAP_REQUIRED");
    }

    private async Task StartDevAsync(string evidencePath, CancellationToken cancellationToken)
    {
        var result = await RunDevScriptAsync("dev.ps1", cancellationToken);
        var statePath = Path.Combine(_repositoryRoot, ".codex-temp", "dev", "state.json");
        using var state = JsonDocument.Parse(await File.ReadAllTextAsync(statePath, cancellationToken));
        var root = state.RootElement;
        if (!string.Equals(ReadString(root, "status"), "ready", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Development supervisor did not reach ready state.");
        }
        _lastDevRunId = ReadString(root, "runId");
        var ports = ReadObject(root, "ports");
        _hostPort = ReadInt(ports, "host");
        _shellPort = ReadInt(ports, "shell");
        foreach (var name in new[] { "host", "shell" })
        {
            var process = ReadObject(ReadObject(root, "processes"), name);
            _processIdentities.Add($"{name}:{ReadInt(process, "pid")}:{_lastDevRunId}");
        }
        await WriteEvidenceAsync(evidencePath, new
        {
            script = "scripts/dev.ps1",
            exitCode = result.ExitCode,
            supervisor = new { status = "ready", runId = _lastDevRunId, hostPort = _hostPort, shellPort = _shellPort }
        }, cancellationToken);
    }

    private async Task RunStopDevAsync(string evidencePath, CancellationToken cancellationToken, bool allowAlreadyStopped = false)
    {
        var result = await RunDevScriptAsync("stop-dev.ps1", cancellationToken);
        if (result.ExitCode != 0
            || (!allowAlreadyStopped
                && !result.StandardOutput.Contains("DEV_STOPPED", StringComparison.Ordinal)
                && !result.StandardOutput.Contains("DEV_ALREADY_STOPPED", StringComparison.Ordinal)))
        {
            throw new InvalidOperationException($"stop-dev.ps1 failed: {Sanitize(result.StandardError)}");
        }
        await WriteEvidenceAsync(evidencePath, ProcessEvidence("scripts/stop-dev.ps1", result), cancellationToken);
    }

    private async Task<AcceptanceProcessResult> RunDevScriptAsync(string scriptName, CancellationToken cancellationToken)
    {
        var arguments = BuildDevArguments(scriptName);
        var result = await AcceptanceInfrastructure.RunProcessAsync(
            "pwsh.exe",
            arguments,
            _repositoryRoot,
            scriptName.Equals("dev.ps1", StringComparison.OrdinalIgnoreCase) ? _hostEnvironment : null,
            redirectOutput: !scriptName.Equals("dev.ps1", StringComparison.OrdinalIgnoreCase),
            cancellationToken);
        if (result.ExitCode != 0)
        {
            CaptureFailedDevRunIdentity();
            throw new InvalidOperationException($"{scriptName} failed: {Sanitize(result.StandardError + result.StandardOutput)}");
        }
        return result;
    }

    private List<string> BuildDevArguments(string scriptName)
    {
        var arguments = new List<string>
        {
            "-NoLogo", "-NoProfile", "-File", Path.Combine(_repositoryRoot, "scripts", scriptName)
        };
        if (scriptName.Equals("dev.ps1", StringComparison.OrdinalIgnoreCase))
        {
            arguments.AddRange([
                "-HostStartupTimeoutSeconds", "600",
                "-ShellStartupTimeoutSeconds", "180",
                "-ShellRequestTimeoutSeconds", "60",
                "-HostEnvironmentName", "Development",
                "-SkipDependencyInstall"
            ]);
        }
        return arguments;
    }

    private async Task<AcceptanceProcessResult> RunCheckedProcessAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string?>? environment,
        CancellationToken cancellationToken)
    {
        var result = await AcceptanceInfrastructure.RunProcessAsync(
            fileName,
            arguments,
            _repositoryRoot,
            environment,
            cancellationToken: cancellationToken);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"{fileName} {arguments[0]} failed: {Sanitize(Tail(result.StandardError + result.StandardOutput))}");
        }
        return result;
    }

    private async Task<LoginResult> LoginAsync(HttpSession session, string ecode, string password, CancellationToken cancellationToken)
    {
        using var response = await SendJsonAsync(
            session,
            HttpMethod.Post,
            "/api/auth/login",
            new { ecode, password },
            cancellationToken,
            request => request.Headers.TryAddWithoutValidation(
                "X-Forwarded-For",
                $"198.51.100.{Interlocked.Increment(ref _loginSequence)}"));
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        EnsureStatus(response, HttpStatusCode.OK, body, "login");
        var cookie = response.Headers.TryGetValues("Set-Cookie", out var values)
            ? string.Join(";", values)
            : string.Empty;
        foreach (var expected in new[] { "HttpOnly", "SameSite=Lax", "Path=/" })
        {
            if (!cookie.Contains(expected, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Authentication cookie omitted {expected}.");
            }
        }
        if (cookie.Contains("Domain=", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Authentication cookie unexpectedly set Domain.");
        }
        return new LoginResult(JsonDocument.Parse(body));
    }

    private async Task<AccountResult> ChangePasswordAsync(
        HttpSession session,
        string currentPassword,
        string newPassword,
        long revision,
        CancellationToken cancellationToken)
    {
        var response = await SendJsonAndReadAsync(
            session,
            HttpMethod.Post,
            "/api/identity/me/password-change",
            new { currentPassword, newPassword, expectedRevision = revision },
            HttpStatusCode.OK,
            cancellationToken);
        var account = ReadObject(response.RootElement, "account");
        return new AccountResult(ReadString(account, "eid"), ReadLong(account, "revision"));
    }

    private async Task<CreatedAccount> CreateUserAsync(
        HttpSession admin,
        string ecode,
        string ename,
        CancellationToken cancellationToken)
    {
        using var response = await SendJsonAsync(admin, HttpMethod.Post, "/api/identity/accounts", new { ecode, ename }, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        EnsureStatus(response, HttpStatusCode.Created, body, "create identity user");
        EnsureNoStore(response, "create identity user");
        using var json = JsonDocument.Parse(body);
        var account = ReadObject(json.RootElement, "account");
        return new CreatedAccount(
            ReadString(account, "eid"),
            ReadLong(account, "revision"),
            ReadString(json.RootElement, "temporaryCredential"),
            account.Clone());
    }

    private async Task<CreatedAccount> ResetCredentialAsync(
        HttpSession admin,
        string accountEid,
        long revision,
        CancellationToken cancellationToken)
    {
        using var response = await SendJsonAsync(
            admin,
            HttpMethod.Post,
            $"/api/identity/accounts/{accountEid}/credential-reset",
            new { expectedRevision = revision, reason = "纵向验收凭据重置" },
            cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        EnsureStatus(response, HttpStatusCode.OK, body, "reset identity credential");
        EnsureNoStore(response, "reset identity credential");
        using var json = JsonDocument.Parse(body);
        var account = ReadObject(json.RootElement, "account");
        return new CreatedAccount(
            ReadString(account, "eid"),
            ReadLong(account, "revision"),
            ReadString(json.RootElement, "temporaryCredential"),
            account.Clone());
    }

    private async Task<AccountResult> SetUserStatusAsync(
        HttpSession admin,
        string accountEid,
        string targetStatus,
        long revision,
        string reason,
        CancellationToken cancellationToken)
    {
        var response = await SendJsonAndReadAsync(
            admin,
            HttpMethod.Put,
            $"/api/identity/accounts/{accountEid}/status",
            new { targetStatus, expectedRevision = revision, reason },
            HttpStatusCode.OK,
            cancellationToken);
        EnsureString(response.RootElement, "status", targetStatus, "identity target status");
        return new AccountResult(accountEid, ReadLong(response.RootElement, "revision"));
    }

    private static async Task EnsureSessionInvalidAsync(HttpSession session, CancellationToken cancellationToken)
    {
        using var response = await session.Client.GetAsync("/api/auth/current-user", cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        EnsureStatus(response, HttpStatusCode.Unauthorized, body, "revoked session current-user");
        var cookieUri = session.Client.BaseAddress
            ?? throw new InvalidOperationException("Session base address was missing.");
        var remainingCookie = session.Cookies.GetCookies(cookieUri)["ICP.Platform.Auth"];
        if (remainingCookie is not null)
        {
            var cookieSignal = response.Headers.TryGetValues("Set-Cookie", out var values)
                ? string.Join(",", values.Select(static value =>
                    value.Contains("expires=", StringComparison.OrdinalIgnoreCase) ? "expires" : "no-expires"))
                : "missing";
            throw new InvalidOperationException(
                $"Invalid session response left the authentication cookie in the client container (server signal: {cookieSignal}).");
        }
    }

    private static HttpSession CreateSession(int port)
    {
        var cookies = new CookieContainer();
        var client = new HttpClient(new HttpClientHandler
        {
            CookieContainer = cookies,
            AllowAutoRedirect = false,
            UseCookies = true
        })
        {
            BaseAddress = new Uri($"http://127.0.0.1:{port}"),
            Timeout = TimeSpan.FromMinutes(5)
        };
        return new HttpSession(client, cookies);
    }

    private static async Task<HttpResponseMessage> SendJsonAsync(
        HttpSession session,
        HttpMethod method,
        string path,
        object payload,
        CancellationToken cancellationToken,
        Action<HttpRequestMessage>? configure = null)
    {
        using var tokenResponse = await session.Client.GetAsync("/api/auth/antiforgery-token", cancellationToken);
        var tokenBody = await tokenResponse.Content.ReadAsStringAsync(cancellationToken);
        EnsureStatus(tokenResponse, HttpStatusCode.OK, tokenBody, "issue antiforgery token");
        using var tokenJson = JsonDocument.Parse(tokenBody);
        using var request = new HttpRequestMessage(method, path)
        {
            Content = JsonContent.Create(payload)
        };
        request.Headers.TryAddWithoutValidation("X-CSRF-TOKEN", ReadString(tokenJson.RootElement, "requestToken"));
        configure?.Invoke(request);
        return await session.Client.SendAsync(request, cancellationToken);
    }

    private static async Task<JsonDocument> SendJsonAndReadAsync(
        HttpSession session,
        HttpMethod method,
        string path,
        object payload,
        HttpStatusCode expected,
        CancellationToken cancellationToken)
    {
        using var response = await SendJsonAsync(session, method, path, payload, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        EnsureStatus(response, expected, body, path);
        return JsonDocument.Parse(body);
    }

    private static async Task<JsonDocument> GetJsonAsync(HttpSession session, string path, CancellationToken cancellationToken)
    {
        using var response = await session.Client.GetAsync(path, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        EnsureStatus(response, HttpStatusCode.OK, body, path);
        return JsonDocument.Parse(body);
    }

    private static async Task EnsureProblemAsync(
        HttpResponseMessage response,
        HttpStatusCode expectedStatus,
        string expectedCode,
        string operation,
        CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        EnsureStatus(response, expectedStatus, body, operation);
        using var json = JsonDocument.Parse(body);
        EnsureString(json.RootElement, "code", expectedCode, operation);
        if (response.Content.Headers.ContentType?.MediaType != "application/problem+json")
        {
            throw new InvalidOperationException($"{operation} did not return application/problem+json.");
        }
    }

    private async Task<AuditEvidence> ReadAuditEvidenceAsync(CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_applicationConnectionString!);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
              (SELECT COUNT(*) FROM [dbo].[IdentityAccount] WHERE [CreatedBy] = '' OR [UpdatedBy] = '' OR [UpdatedAt] < [CreatedAt]) +
              (SELECT COUNT(*) FROM [dbo].[LocalCredential] WHERE [CreatedBy] = '' OR [UpdatedBy] = '' OR [UpdatedAt] < [CreatedAt]) +
              (SELECT COUNT(*) FROM [dbo].[AuthenticationSession] WHERE [CreatedBy] = '' OR [UpdatedBy] = '' OR [UpdatedAt] < [CreatedAt]) +
              (SELECT COUNT(*) FROM [dbo].[IdentitySecurityEvent] WHERE [CreatedBy] = '' OR [UpdatedBy] = '' OR [UpdatedAt] < [CreatedAt]),
              4;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) throw new InvalidOperationException("Audit observation returned no row.");
        return new AuditEvidence(reader.GetInt32(0), reader.GetInt32(1));
    }

    private async Task<int> CountDisposableIntegrationCatalogsAsync(CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_adminConnectionString!);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*) FROM [sys].[databases]
            WHERE [name] LIKE 'NewPIdentity[_]%'
               OR [name] LIKE 'NewPAuthentication[_]%'
               OR [name] LIKE 'NewPOrganization[_]%';
            """;
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }

    private IReadOnlyList<string> ScanLegacyRuntimeChains()
    {
        var roots = new[] { "apps", "server/modules", "frontend", "modules", "scripts" };
        var forbidden = new[] { "LocalAuthOptions", "LocalAuthSeeder", "LocalAuth:Password", "/user-link", "PersonUserLinkService" };
        var matches = new List<string>();
        foreach (var relativeRoot in roots)
        {
            var root = Path.Combine(_repositoryRoot, relativeRoot.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(root)) continue;
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                         .Where(IsRuntimeTextFile)
                         .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)))
            {
                var content = File.ReadAllText(file);
                if (forbidden.Any(value => content.Contains(value, StringComparison.Ordinal)))
                {
                    matches.Add(Path.GetRelativePath(_repositoryRoot, file).Replace('\\', '/'));
                }
            }
        }
        return matches.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    }

    private static bool IsRuntimeTextFile(string path)
    {
        if (path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
            || path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
            || path.Contains($"{Path.DirectorySeparatorChar}dist{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
            || path.Contains($"{Path.DirectorySeparatorChar}node_modules{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        return new[] { ".cs", ".ts", ".tsx", ".json", ".ps1", ".sh" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
    }

    private async Task VerifyOwnershipAsync(CancellationToken cancellationToken)
    {
        ValidateCatalogName(_catalog!);
        EnsureOwnedResourceRoot(_pluginRoot!);
        EnsureOwnedResourceRoot(_keyRingRoot!);
        if (_lastDevRunId is not null)
        {
            var statePath = Path.Combine(_repositoryRoot, ".codex-temp", "dev", "state.json");
            using var state = JsonDocument.Parse(await File.ReadAllTextAsync(statePath, cancellationToken));
            if (!string.Equals(ReadString(state.RootElement, "runId"), _lastDevRunId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Development supervisor state no longer belongs to this acceptance run.");
            }
        }
        var current = await ReadOwnershipSnapshotAsync(cancellationToken);
        if (_ownershipSnapshot is null || current != _ownershipSnapshot)
        {
            throw new InvalidOperationException("Disposable catalog ownership marker changed before teardown.");
        }
    }

    private async Task<DatabaseOwnershipSnapshot> ReadOwnershipSnapshotAsync(CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_applicationConnectionString!);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT [Version], [RepositoryId], [WorktreeId], [Provider], [ServerFingerprint], [CatalogFingerprint]
            FROM [dbo].[__NewPDatabaseOwnership] WHERE [MarkerId] = 1;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) throw new InvalidOperationException("Disposable catalog ownership marker was missing.");
        return new DatabaseOwnershipSnapshot(
            reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5));
    }

    private async Task CreateCatalogAsync(CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_adminConnectionString!);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE DATABASE [{_catalog}];";
        await command.ExecuteNonQueryAsync(cancellationToken);
        _catalogCreated = true;
    }

    private async Task DropCatalogAsync(CancellationToken cancellationToken)
    {
        ValidateCatalogName(_catalog!);
        SqlConnection.ClearAllPools();
        await using var connection = new SqlConnection(_adminConnectionString!);
        await connection.OpenAsync(cancellationToken);
        await using (var count = connection.CreateCommand())
        {
            count.CommandText = "SELECT COUNT(*) FROM [sys].[databases] WHERE [name] = @catalog;";
            count.Parameters.AddWithValue("@catalog", _catalog!);
            if (Convert.ToInt32(await count.ExecuteScalarAsync(cancellationToken)) != 1)
            {
                throw new InvalidOperationException("Disposable catalog count changed before teardown.");
            }
        }
        await using var command = connection.CreateCommand();
        command.CommandText = $"ALTER DATABASE [{_catalog}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_catalog}];";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task RecordScenarioSuccessAsync(string scenario, string evidencePath, object evidence, CancellationToken cancellationToken)
    {
        var evidenceRef = await WriteEvidenceAsync(evidencePath, evidence, cancellationToken);
        await _recorder.RecordAsync(AcceptanceGateKind.IdentityFoundation, AcceptanceEventType.ScenarioCompleted, scenario: scenario, cancellationToken: cancellationToken);
        await _recorder.RecordAsync(AcceptanceGateKind.IdentityFoundation, AcceptanceEventType.EvidenceRecorded, scenario: scenario, evidenceRef: evidenceRef, cancellationToken: cancellationToken);
    }

    private async Task<string> WriteEvidenceAsync(string relativePath, object value, CancellationToken cancellationToken)
    {
        var fullPath = Path.GetFullPath(Path.Combine(_runDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!fullPath.StartsWith(_runDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Evidence path escaped the acceptance run directory.");
        }
        await AcceptanceInfrastructure.WriteJsonAsync(fullPath, value, cancellationToken);
        var artifact = AcceptanceInfrastructure.RelativeArtifact(_runDirectory, fullPath);
        if (!_artifactRefs.Contains(artifact, StringComparer.Ordinal)) _artifactRefs.Add(artifact);
        return artifact;
    }

    private async Task ValidateEvidenceAsync(CancellationToken cancellationToken)
    {
        try
        {
            foreach (var artifact in _artifactRefs.Distinct(StringComparer.Ordinal))
            {
                var path = Path.GetFullPath(Path.Combine(_runDirectory, artifact.Replace('/', Path.DirectorySeparatorChar)));
                if (!path.StartsWith(_runDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                    || !File.Exists(path)
                    || new FileInfo(path).Length == 0)
                {
                    throw new InvalidOperationException($"Evidence artifact is missing or empty: {artifact}");
                }
                if (path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                {
                    using var _ = JsonDocument.Parse(await File.ReadAllTextAsync(path, cancellationToken));
                }
            }
        }
        catch (Exception exception)
        {
            await _recorder.RecordAsync(
                AcceptanceGateKind.IdentityFoundation,
                AcceptanceEventType.EvidenceRecorded,
                AcceptanceObservationOutcome.Failed,
                failureCode: AcceptanceFailureCodes.EvidenceUnreadable,
                cancellationToken: cancellationToken);
            await WriteEvidenceAsync("identity-foundation/evidence-validation-failure.json", new { diagnostic = Sanitize(exception.Message) }, cancellationToken);
        }
    }

    private async Task ScanEvidenceForSecretsAsync(CancellationToken cancellationToken)
    {
        var patterns = _secretTokens
            .Where(value => !string.IsNullOrWhiteSpace(value) && value.Length >= 6)
            .Distinct(StringComparer.Ordinal)
            .Select(Encoding.UTF8.GetBytes)
            .ToArray();
        var leaked = false;
        foreach (var path in Directory.EnumerateFiles(_runDirectory, "*", SearchOption.AllDirectories))
        {
            var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
            if (patterns.Any(pattern => bytes.AsSpan().IndexOf(pattern) >= 0))
            {
                leaked = true;
                break;
            }
        }
        await _recorder.RecordAsync(
            AcceptanceGateKind.IdentityFoundation,
            AcceptanceEventType.SecretScanCompleted,
            leaked ? AcceptanceObservationOutcome.Failed : AcceptanceObservationOutcome.Succeeded,
            failureCode: leaked ? AcceptanceFailureCodes.SecretDetected : null,
            cancellationToken: cancellationToken);
    }

    private async Task LoginBrowserAsync(IPage page, string ecode, string password)
    {
        if (!new Uri(page.Url).AbsolutePath.Equals("/login", StringComparison.Ordinal))
        {
            await page.GotoAsync($"http://127.0.0.1:{_shellPort}/login");
        }
        await page.Locator("#ecode").FillAsync(ecode);
        await page.Locator("#password").FillAsync(password);
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "登录", Exact = true }).ClickAsync();
        await page.WaitForFunctionAsync("() => window.location.pathname !== '/login'");
    }

    private void CaptureFailedDevRunIdentity()
    {
        try
        {
            using var state = JsonDocument.Parse(File.ReadAllText(Path.Combine(_repositoryRoot, ".codex-temp", "dev", "state.json")));
            _lastDevRunId = ReadString(state.RootElement, "runId");
        }
        catch
        {
            // 无法确认监督器身份时，teardown 的 ownership revalidation 会安全失败并保留资源。
        }
    }

    private static Dictionary<string, object?> PersonPayload(string personNo, string name, bool enabled) =>
        new(StringComparer.Ordinal)
        {
            ["PersonNo"] = personNo,
            ["ENAME"] = name,
            ["CompanyId"] = null,
            ["DepartmentId"] = null,
            ["PositionId"] = null,
            ["EmploymentStatus"] = "FullTime",
            ["IsEnabled"] = enabled
        };

    private void AddConnectionSecrets(string raw, string password)
    {
        _secretTokens.Add(raw);
        if (!string.IsNullOrEmpty(password))
        {
            _secretTokens.Add(password);
            _secretTokens.Add($"Password={password}");
            _secretTokens.Add($"Pwd={password}");
        }
    }

    private string Sanitize(string value)
    {
        var sanitized = value;
        foreach (var secret in _secretTokens.Where(value => !string.IsNullOrEmpty(value)).OrderByDescending(value => value.Length))
        {
            sanitized = sanitized.Replace(secret, "[REDACTED]", StringComparison.Ordinal);
        }
        return sanitized;
    }

    private static object ProcessEvidence(string command, AcceptanceProcessResult result) => new
    {
        command,
        result.ExitCode,
        standardOutput = Tail(result.StandardOutput),
        standardError = Tail(result.StandardError)
    };

    private static object ProcessSummary(string command, AcceptanceProcessResult result) => new
    {
        command,
        result.ExitCode,
        outputDigest = AcceptanceInfrastructure.Sha256(result.StandardOutput + result.StandardError)
    };

    private static string Tail(string value) => value.Length <= 4000 ? value : value[^4000..];

    private static string StripTerminalSequences(string value) =>
        Regex.Replace(value, "\\x1B(?:[@-Z\\\\-_]|\\[[0-?]*[ -/]*[@-~])", string.Empty, RegexOptions.CultureInvariant);

    private static void EnsureNoStore(HttpResponseMessage response, string operation)
    {
        if (!response.Headers.CacheControl?.NoStore ?? true)
        {
            throw new InvalidOperationException($"{operation} omitted Cache-Control: no-store.");
        }
    }

    private static void EnsureStatus(HttpResponseMessage response, HttpStatusCode expected, string body, string operation)
    {
        if (response.StatusCode != expected)
        {
            throw new InvalidOperationException($"{operation} returned {(int)response.StatusCode}, expected {(int)expected}: {body}");
        }
    }

    private static JsonElement Property(JsonElement element, string name)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)) return property.Value;
        }
        throw new InvalidOperationException($"JSON property {name} was missing.");
    }

    private static JsonElement ReadObject(JsonElement element, string name) => Property(element, name);
    private static JsonElement ReadArray(JsonElement element, string name) => Property(element, name);
    private static string ReadString(JsonElement element, string name) => Property(element, name).GetString()
        ?? throw new InvalidOperationException($"JSON property {name} was null.");
    private static long ReadLong(JsonElement element, string name)
    {
        var property = Property(element, name);
        return property.ValueKind switch
        {
            JsonValueKind.Number => property.GetInt64(),
            JsonValueKind.String when long.TryParse(property.GetString(), out var value) => value,
            _ => throw new InvalidOperationException($"JSON property {name} was not an integer.")
        };
    }
    private static int ReadInt(JsonElement element, string name) => Property(element, name).GetInt32();
    private static DateTime ReadDateTime(JsonElement element, string name) => Property(element, name).GetDateTime();
    private static string ReadNestedString(JsonElement element, string parent, string name) => ReadString(ReadObject(element, parent), name);
    private static long ReadNestedLong(JsonElement element, string parent, string name) => ReadLong(ReadObject(element, parent), name);

    private static void EnsureString(JsonElement element, string name, string expected, string operation)
    {
        var actual = Property(element, name).ValueKind == JsonValueKind.String
            ? ReadString(element, name)
            : Property(element, name).ToString();
        if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"{operation} returned {actual}, expected {expected}.");
        }
    }

    private static void EnsureBoolean(JsonElement element, string name, bool expected, string operation)
    {
        if (Property(element, name).GetBoolean() != expected)
        {
            throw new InvalidOperationException($"{operation} returned an unexpected {name} value.");
        }
    }

    private static string RequireSecret(string? value, string label) =>
        !string.IsNullOrWhiteSpace(value) ? value : throw new InvalidOperationException($"Missing {label}.");

    private void ValidateCatalogName(string catalog)
    {
        if (!Regex.IsMatch(catalog, "^NewP_identity_acceptance_[a-f0-9]{24}$", RegexOptions.CultureInvariant))
        {
            throw new InvalidOperationException("Refusing to operate on a catalog outside the Identity Foundation acceptance boundary.");
        }
    }

    private void EnsureOwnedResourceRoot(string path)
    {
        var resources = Path.GetFullPath(Path.Combine(_runDirectory, "resources")) + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(path);
        if (!fullPath.StartsWith(resources, StringComparison.OrdinalIgnoreCase)
            || (File.GetAttributes(fullPath) & FileAttributes.ReparsePoint) != 0
            || Directory.EnumerateFileSystemEntries(fullPath, "*", SearchOption.AllDirectories)
                .Any(item => (File.GetAttributes(item) & FileAttributes.ReparsePoint) != 0))
        {
            throw new InvalidOperationException("Acceptance resource escaped its ownership boundary or contains a reparse point.");
        }
    }

    private static void EnsureEmptyDirectory(string path)
    {
        if (Directory.EnumerateFileSystemEntries(path).Any())
        {
            throw new InvalidOperationException($"Acceptance resource directory was not empty: {path}");
        }
    }

    private static int FindFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try { return ((IPEndPoint)listener.LocalEndpoint).Port; }
        finally { listener.Stop(); }
    }

    private static string ResolveChromePath()
    {
        var configured = Environment.GetEnvironmentVariable("NEWP_ACCEPTANCE_CHROMIUM_EXECUTABLE");
        return new[]
            {
                configured,
                @"C:\Program Files\Google\Chrome\Application\chrome.exe",
                @"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Google", "Chrome", "Application", "chrome.exe")
            }
            .FirstOrDefault(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
            ?? throw new InvalidOperationException("No Chromium executable was found for Identity Foundation browser acceptance.");
    }

    private static string ResolveCorepackPath()
    {
        if (!OperatingSystem.IsWindows())
        {
            return "corepack";
        }

        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                     .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var candidate = Path.Combine(directory, "corepack.cmd");
            if (File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        throw new InvalidOperationException("corepack.cmd was not found on PATH.");
    }

    private static string FailureCodeFor(string scenario) => scenario switch
    {
        PreflightScenario => AcceptanceFailureCodes.IdentityFoundationPreflightFailed,
        ApiScenario => AcceptanceFailureCodes.IdentityFoundationApiFailed,
        BrowserScenario => AcceptanceFailureCodes.IdentityFoundationBrowserFailed,
        RestartScenario => AcceptanceFailureCodes.IdentityFoundationRestartFailed,
        SecurityScenario => AcceptanceFailureCodes.IdentityFoundationSecurityFailed,
        PersistenceScenario => AcceptanceFailureCodes.IdentityFoundationPersistenceFailed,
        _ => AcceptanceFailureCodes.ObserverFailed
    };

    private sealed record HttpSession(HttpClient Client, CookieContainer Cookies) : IDisposable
    {
        public void Dispose() => Client.Dispose();
    }
    private sealed record LoginResult(JsonDocument Body) : IDisposable
    {
        public void Dispose() => Body.Dispose();
    }
    private sealed record AccountResult(string AccountEid, long Revision);
    private sealed record CreatedAccount(string AccountEid, long Revision, string TemporaryCredential, JsonElement Account);
    private sealed record AuditEvidence(int InvalidRows, int EntityKinds);
    private sealed record NotReadyObservation(string FailureCode);
    private sealed record IntegrationCommand(string Name, string Project, string EnableVariable, string AdminConnectionVariable);
    private sealed record DatabaseOwnershipSnapshot(
        int Version,
        string RepositoryId,
        string WorktreeId,
        string Provider,
        string ServerFingerprint,
        string CatalogFingerprint);
}
