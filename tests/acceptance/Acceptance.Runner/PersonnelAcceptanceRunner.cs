using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.Playwright;

namespace ICP.Platform.Acceptance;

internal sealed record PersonnelAcceptanceExecution(
    string? DatabaseFingerprint,
    IReadOnlyList<string> ProcessIdentities,
    IReadOnlyList<string> ArtifactRefs,
    AcceptanceCleanupResult Cleanup);

internal sealed class PersonnelAcceptanceRunner
{
    internal const string PreflightScenario = "personnel.preflight";
    internal const string ApiScenario = "personnel.api";
    internal const string BrowserScenario = "personnel.browser";
    internal const string RestartScenario = "personnel.restart";

    private static readonly JsonSerializerOptions WebJsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _repositoryRoot;
    private readonly string _runDirectory;
    private readonly string _runId;
    private readonly AcceptanceObservationRecorder _recorder;
    private readonly List<string> _artifactRefs = [];
    private readonly List<string> _processIdentities = [];
    private readonly List<string> _secretTokens = [];
    private readonly Dictionary<string, string?> _devEnvironment = new(StringComparer.OrdinalIgnoreCase);
    private string? _adminConnectionString;
    private string? _applicationConnectionString;
    private string? _catalog;
    private string? _pluginRoot;
    private string? _lastDevRunId;
    private int _hostPort;
    private int _shellPort;
    private bool _catalogCreated;
    private string _activeScenario = PreflightScenario;
    private FixtureData? _fixture;
    private PersonData? _person;
    private LocalCredentials? _credentials;
    private DatabaseOwnershipSnapshot? _ownershipSnapshot;

    public PersonnelAcceptanceRunner(
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

    public async Task<PersonnelAcceptanceExecution> RunAsync(
        string trackedTreeBefore,
        CancellationToken cancellationToken)
    {
        AcceptanceCleanupResult cleanup = new("not-started", AcceptanceFailureCodes.CleanupIncomplete);

        try
        {
            await RunPreflightAsync(cancellationToken);
            await RecordScenarioSuccessAsync(PreflightScenario, "personnel/preflight.json", new
            {
                status = "passed",
                databaseFingerprint = AcceptanceInfrastructure.Sha256(_catalog!),
                pluginRootFingerprint = AcceptanceInfrastructure.Sha256(_pluginRoot!),
                hostPort = _hostPort,
                shellPort = _shellPort
            }, cancellationToken);

            _activeScenario = ApiScenario;
            await RunApiScenarioAsync(cancellationToken);

            _activeScenario = BrowserScenario;
            await RunBrowserScenarioAsync("initial", cancellationToken);

            _activeScenario = RestartScenario;
            await RunRestartScenarioAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            var failureCode = FailureCodeFor(_activeScenario);
            var evidenceRef = await WriteEvidenceAsync(
                $"personnel/{_activeScenario.Replace('.', '-')}-failure.json",
                new { status = "failed", failureCode, diagnostic = Sanitize(exception.Message) },
                cancellationToken);
            await _recorder.RecordAsync(
                AcceptanceGateKind.Personnel,
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
            await VerifyTrackedTreeAsync(trackedTreeBefore, cancellationToken);
            await ValidateEvidenceAsync(cancellationToken);
            await ScanEvidenceForSecretsAsync(cancellationToken);
        }

        return new PersonnelAcceptanceExecution(
            _catalog is null ? null : AcceptanceInfrastructure.Sha256(_catalog),
            _processIdentities,
            _artifactRefs,
            cleanup);
    }

    private async Task RunPreflightAsync(CancellationToken cancellationToken)
    {
        var configuredAdmin = Environment.GetEnvironmentVariable("NEWP_ACCEPTANCE_SQLSERVER_ADMIN_CONNECTION");
        if (string.IsNullOrWhiteSpace(configuredAdmin))
        {
            throw new InvalidOperationException(
                "NEWP_ACCEPTANCE_SQLSERVER_ADMIN_CONNECTION is required for the Personnel gate.");
        }

        var adminBuilder = new SqlConnectionStringBuilder(configuredAdmin)
        {
            InitialCatalog = "master",
            Pooling = false
        };
        _adminConnectionString = adminBuilder.ConnectionString;
        AddConnectionSecrets(configuredAdmin, adminBuilder.Password);

        _catalog = $"NewP_acceptance_{_runId}";
        ValidateCatalogName(_catalog);
        var appBuilder = new SqlConnectionStringBuilder(adminBuilder.ConnectionString)
        {
            InitialCatalog = _catalog,
            Pooling = false
        };
        _applicationConnectionString = appBuilder.ConnectionString;

        _pluginRoot = Path.Combine(_runDirectory, "resources", "plugins");
        Directory.CreateDirectory(_pluginRoot);
        if (Directory.EnumerateFileSystemEntries(_pluginRoot).Any())
        {
            throw new InvalidOperationException("Personnel gate plugin root was not empty before startup.");
        }

        _credentials = ReadLocalCredentials();
        _secretTokens.Add(_credentials.Password);
        var fixtureSecret = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32))
            .ToLowerInvariant();
        _secretTokens.Add(fixtureSecret);
        _devEnvironment["NEWP_DEV_SQLSERVER_CONNECTION_STRING"] = _applicationConnectionString;
        _devEnvironment["Platform__PluginInstallRoot"] = _pluginRoot;
        _devEnvironment["Acceptance__Enabled"] = "true";
        _devEnvironment["Acceptance__RunId"] = _runId;
        _devEnvironment["Acceptance__Secret"] = fixtureSecret;
        _devEnvironment["Logging__LogLevel__Default"] = "Information";

        await RunDevScriptAsync("stop-dev.ps1", "personnel/preflight-stop-existing.json", cancellationToken, allowAlreadyStopped: true);
        await CreateCatalogAsync(cancellationToken);
        await StartDevAsync("personnel/preflight-dev-start.json", cancellationToken);
        _ownershipSnapshot = await ReadOwnershipSnapshotAsync(cancellationToken);
        await WriteEvidenceAsync("resource-ownership.json", new
        {
            schemaVersion = "1.0",
            runId = _runId,
            catalog = _catalog,
            pluginRoot = _pluginRoot,
            repositoryFingerprint = AcceptanceInfrastructure.Sha256(_ownershipSnapshot.RepositoryId),
            worktreeFingerprint = AcceptanceInfrastructure.Sha256(_ownershipSnapshot.WorktreeId),
            serverFingerprint = _ownershipSnapshot.ServerFingerprint,
            catalogFingerprint = _ownershipSnapshot.CatalogFingerprint
        }, cancellationToken);

        using var fixtureClient = CreateClient(_hostPort);
        using var prepare = new HttpRequestMessage(HttpMethod.Post, "/api/acceptance/personnel/prepare")
        {
            Content = JsonContent.Create(new { })
        };
        prepare.Headers.Add("X-NewP-Acceptance-Secret", fixtureSecret);
        using var response = await fixtureClient.SendAsync(prepare, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        EnsureStatus(response, HttpStatusCode.OK, body, "fixture prepare");
        _fixture = JsonSerializer.Deserialize<FixtureData>(body, WebJsonOptions)
            ?? throw new InvalidOperationException("Personnel fixture response was empty.");
        if (!string.Equals(_fixture.RunId, _runId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Personnel fixture run identity did not match the acceptance run.");
        }
    }

    private async Task RunApiScenarioAsync(CancellationToken cancellationToken)
    {
        var fixture = _fixture ?? throw new InvalidOperationException("Personnel fixture was not prepared.");
        var checks = new List<object>();

        using (var anonymous = CreateClient(_hostPort))
        using (var response = await anonymous.GetAsync("/api/organization/persons", cancellationToken))
        {
            EnsureStatus(response, HttpStatusCode.Unauthorized, await response.Content.ReadAsStringAsync(cancellationToken), "anonymous list");
            checks.Add(new { check = "anonymous-list", status = 401 });
        }

        using (var readOnly = CreateClient(_hostPort))
        {
            using var login = new HttpRequestMessage(HttpMethod.Post, "/api/acceptance/personnel/read-only-login")
            {
                Content = JsonContent.Create(new { })
            };
            login.Headers.Add("X-NewP-Acceptance-Secret", _devEnvironment["Acceptance__Secret"]!);
            using var loginResponse = await readOnly.SendAsync(login, cancellationToken);
            EnsureStatus(loginResponse, HttpStatusCode.OK, await loginResponse.Content.ReadAsStringAsync(cancellationToken), "read-only login");
            using var denied = await readOnly.PostAsJsonAsync(
                "/api/organization/persons",
                PersonPayload("ACC-DENIED", "Denied Person", fixture.CompanyId, fixture.DepartmentId),
                cancellationToken);
            EnsureStatus(denied, HttpStatusCode.Forbidden, await denied.Content.ReadAsStringAsync(cancellationToken), "read-only create");
            checks.Add(new { check = "read-only-create", status = 403 });
        }

        using var admin = CreateClient(_hostPort);
        await LoginAdminAsync(admin, cancellationToken);

        var secondCompany = await PostAndReadIdAsync(admin, "/api/organization/companies", new
        {
            code = $"ACC-C2-{_runId[..8]}",
            displayName = $"Personnel Gate Company Two {_runId[..8]}",
            isEnabled = true,
            sortOrder = "20"
        }, cancellationToken);
        var secondDepartment = await PostAndReadIdAsync(admin, "/api/organization/departments", new
        {
            code = $"ACC-D2-{_runId[..8]}",
            displayName = $"Personnel Gate Department Two {_runId[..8]}",
            companyId = secondCompany.Id,
            parentDepartmentId = (string?)null,
            isEnabled = true,
            sortOrder = "20"
        }, cancellationToken);

        await AssertPersonErrorAsync(admin, "required-company", PersonPayload("ACC-E01", "Required Company", null, fixture.DepartmentId), "Organization.Person.CompanyRequired", checks, cancellationToken);
        await AssertPersonErrorAsync(admin, "company-not-found", PersonPayload("ACC-E02", "Missing Company", "Company_missing", fixture.DepartmentId), "Organization.Person.CompanyNotFound", checks, cancellationToken);
        await AssertPersonErrorAsync(admin, "department-not-found", PersonPayload("ACC-E03", "Missing Department", fixture.CompanyId, "Department_missing"), "Organization.Person.DepartmentNotFound", checks, cancellationToken);
        await AssertPersonErrorAsync(admin, "department-company-mismatch", PersonPayload("ACC-E04", "Mismatch Department", fixture.CompanyId, secondDepartment.Id), "Organization.Person.DepartmentCompanyMismatch", checks, cancellationToken);

        await PutCompanyAsync(admin, fixture.CompanyId, fixture.CompanyName, false, cancellationToken);
        await AssertPersonErrorAsync(admin, "company-disabled", PersonPayload("ACC-E05", "Disabled Company", fixture.CompanyId, fixture.DepartmentId), "Organization.Person.CompanyDisabled", checks, cancellationToken);
        await PutCompanyAsync(admin, fixture.CompanyId, fixture.CompanyName, true, cancellationToken);
        await PutDepartmentAsync(admin, fixture.DepartmentId, fixture.CompanyId, fixture.DepartmentName, false, cancellationToken);
        await AssertPersonErrorAsync(admin, "department-disabled", PersonPayload("ACC-E06", "Disabled Department", fixture.CompanyId, fixture.DepartmentId), "Organization.Person.DepartmentDisabled", checks, cancellationToken);
        await PutDepartmentAsync(admin, fixture.DepartmentId, fixture.CompanyId, fixture.DepartmentName, true, cancellationToken);

        var personNo = $"ACC-{_runId[..10]}";
        var personName = $"Personnel Gate Person {_runId[..8]}";
        using var created = await admin.PostAsJsonAsync(
            "/api/organization/persons",
            PersonPayload(personNo, personName, fixture.CompanyId, fixture.DepartmentId),
            cancellationToken);
        var createdBody = await created.Content.ReadAsStringAsync(cancellationToken);
        EnsureStatus(created, HttpStatusCode.Created, createdBody, "admin create person");
        using var createdJson = JsonDocument.Parse(createdBody);
        var personId = ReadNestedString(createdJson.RootElement, "fields", "EID");
        _person = new PersonData(personId, personNo, personName);

        using var detail = await admin.GetAsync($"/api/organization/persons/{Uri.EscapeDataString(personId)}", cancellationToken);
        var detailBody = await detail.Content.ReadAsStringAsync(cancellationToken);
        EnsureStatus(detail, HttpStatusCode.OK, detailBody, "admin detail");
        AssertPersonBody(detailBody, _person, fixture);
        using var list = await admin.GetAsync($"/api/organization/persons?keyword={Uri.EscapeDataString(personNo)}&pageIndex=1&pageSize=20", cancellationToken);
        var listBody = await list.Content.ReadAsStringAsync(cancellationToken);
        EnsureStatus(list, HttpStatusCode.OK, listBody, "admin list");
        if (!listBody.Contains(personId, StringComparison.Ordinal) || !listBody.Contains(personNo, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Personnel list did not contain the created person.");
        }

        checks.Add(new { check = "admin-create-detail-list", status = 201, personId, personNo });
        await RecordScenarioSuccessAsync(ApiScenario, "personnel/api.json", new
        {
            status = "passed",
            checks,
            person = new { id = personId, personNo, name = personName },
            organization = new { fixture.CompanyId, fixture.CompanyName, fixture.DepartmentId, fixture.DepartmentName }
        }, cancellationToken);
    }

    private async Task RunBrowserScenarioAsync(string phase, CancellationToken cancellationToken)
    {
        var person = _person ?? throw new InvalidOperationException("Personnel API scenario did not create a person.");
        var fixture = _fixture ?? throw new InvalidOperationException("Personnel fixture was not prepared.");
        var credentials = _credentials ?? throw new InvalidOperationException("Local credentials were not loaded.");
        var chromePath = ResolveChromePath();
        var screenshotPath = Path.Combine(_runDirectory, "personnel", $"browser-{phase}.png");
        Directory.CreateDirectory(Path.GetDirectoryName(screenshotPath)!);

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
        await page.GotoAsync($"http://127.0.0.1:{_shellPort}/login", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.DOMContentLoaded
        });
        await page.Locator("#username").FillAsync(credentials.Username);
        await page.Locator("#password").FillAsync(credentials.Password);
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "登录", Exact = true }).ClickAsync();
        await page.WaitForFunctionAsync("() => window.location.pathname !== '/login'");
        await page.GotoAsync($"http://127.0.0.1:{_shellPort}/organization/persons", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.DOMContentLoaded
        });

        await AssertPersonListAndDrawerAsync(page, person, fixture);
        if (phase == "initial")
        {
            await page.ReloadAsync(new PageReloadOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
            await AssertPersonListAndDrawerAsync(page, person, fixture);
        }
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = screenshotPath, FullPage = true });
        _artifactRefs.Add(AcceptanceInfrastructure.RelativeArtifact(_runDirectory, screenshotPath));

        var evidenceRef = await WriteEvidenceAsync($"personnel/browser-{phase}.json", new
        {
            status = "passed",
            phase,
            url = page.Url,
            assertions = new[]
            {
                "canonical-list-row-visible",
                "canonical-view-drawer-visible",
                "company-display-name-readable",
                "department-display-name-readable",
                phase == "initial" ? "hard-refresh-canonical-list-and-drawer" : "restart-relogin-canonical-list-and-drawer"
            },
            screenshot = AcceptanceInfrastructure.RelativeArtifact(_runDirectory, screenshotPath)
        }, cancellationToken);

        var scenario = phase == "initial" ? BrowserScenario : RestartScenario;
        await _recorder.RecordAsync(AcceptanceGateKind.Personnel, AcceptanceEventType.ScenarioCompleted, scenario: scenario, cancellationToken: cancellationToken);
        await _recorder.RecordAsync(AcceptanceGateKind.Personnel, AcceptanceEventType.EvidenceRecorded, scenario: scenario, evidenceRef: evidenceRef, cancellationToken: cancellationToken);
    }

    private static async Task AssertPersonListAndDrawerAsync(
        IPage page,
        PersonData person,
        FixtureData fixture)
    {
        var row = page.Locator("tr", new PageLocatorOptions { HasTextString = person.PersonNo }).Last;
        await row.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        var rowText = await row.InnerTextAsync();
        AssertContains(rowText, person.PersonNo, "person number in list row");
        AssertContains(rowText, person.Name, "person name in list row");
        AssertContains(rowText, fixture.CompanyName, "company display name in list row");
        AssertContains(rowText, fixture.DepartmentName, "department display name in list row");

        var drawer = page.Locator(".ant-drawer-content").Last;
        // 硬刷新会按 URL 恢复已打开的查看抽屉；此时遮罩仍在，不能再次点击行内入口。
        if (!await drawer.IsVisibleAsync())
        {
            await row.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "查看" }).ClickAsync();
        }
        await drawer.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await page.WaitForFunctionAsync(
            "args => document.querySelector(args[0])?.value === args[1]",
            new[] { ".ant-drawer-content #PersonNo", person.PersonNo });
        await page.WaitForFunctionAsync(
            "args => document.querySelector(args[0])?.value === args[1]",
            new[] { ".ant-drawer-content #ENAME", person.Name });
        foreach (var expected in new[] { fixture.CompanyName, fixture.DepartmentName })
        {
            await drawer.GetByText(expected, new LocatorGetByTextOptions { Exact = true }).First
                .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        }
        AssertContains(await drawer.Locator("#PersonNo").InputValueAsync(), person.PersonNo, "person number in view drawer");
        AssertContains(await drawer.Locator("#ENAME").InputValueAsync(), person.Name, "person name in view drawer");
        var drawerText = await drawer.InnerTextAsync();
        AssertContains(drawerText, fixture.CompanyName, "company display name in view drawer");
        AssertContains(drawerText, fixture.DepartmentName, "department display name in view drawer");
    }

    private async Task RunRestartScenarioAsync(CancellationToken cancellationToken)
    {
        var person = _person ?? throw new InvalidOperationException("Personnel API scenario did not create a person.");
        var fixture = _fixture ?? throw new InvalidOperationException("Personnel fixture was not prepared.");
        await RunDevScriptAsync("stop-dev.ps1", "personnel/restart-stop.json", cancellationToken);
        await StartDevAsync("personnel/restart-dev-start.json", cancellationToken);

        using (var admin = CreateClient(_hostPort))
        {
            await LoginAdminAsync(admin, cancellationToken);
            using var response = await admin.GetAsync($"/api/organization/persons/{Uri.EscapeDataString(person.Id)}", cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            EnsureStatus(response, HttpStatusCode.OK, body, "detail after restart");
            AssertPersonBody(body, person, fixture);
        }

        // 重启后的浏览器使用全新 context 重新登录，避免旧 cookie 把持久化与恢复缺陷遮住。
        await RunBrowserScenarioAsync("restart", cancellationToken);
    }

    private async Task<AcceptanceCleanupResult> TeardownAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (_catalogCreated)
            {
                await VerifyOwnershipAsync(cancellationToken);
            }
            await _recorder.RecordAsync(AcceptanceGateKind.Personnel, AcceptanceEventType.OwnershipVerified, cancellationToken: cancellationToken);
        }
        catch (Exception exception)
        {
            await WriteEvidenceAsync("personnel/ownership-failure.json", new
            {
                status = "failed",
                failureCode = AcceptanceFailureCodes.OwnershipMismatch,
                diagnostic = Sanitize(exception.Message),
                resourcesPreserved = true
            }, cancellationToken);
            await _recorder.RecordAsync(
                AcceptanceGateKind.Personnel,
                AcceptanceEventType.OwnershipVerified,
                AcceptanceObservationOutcome.Failed,
                failureCode: AcceptanceFailureCodes.OwnershipMismatch,
                cancellationToken: cancellationToken);
            return new AcceptanceCleanupResult("failed", AcceptanceFailureCodes.OwnershipMismatch);
        }

        try
        {
            await RunDevScriptAsync("stop-dev.ps1", "personnel/teardown-stop.json", cancellationToken, allowAlreadyStopped: true);
            if (_catalogCreated)
            {
                await DropCatalogAsync(cancellationToken);
                _catalogCreated = false;
            }
            if (_pluginRoot is not null && Directory.Exists(_pluginRoot))
            {
                EnsureSafePluginRoot(_pluginRoot);
                Directory.Delete(_pluginRoot, recursive: true);
            }

            await _recorder.RecordAsync(AcceptanceGateKind.Personnel, AcceptanceEventType.CleanupCompleted, cancellationToken: cancellationToken);
            return new AcceptanceCleanupResult("completed", null);
        }
        catch (Exception exception)
        {
            await WriteEvidenceAsync("personnel/cleanup-failure.json", new
            {
                status = "failed",
                failureCode = AcceptanceFailureCodes.CleanupFailed,
                diagnostic = Sanitize(exception.Message)
            }, cancellationToken);
            await _recorder.RecordAsync(
                AcceptanceGateKind.Personnel,
                AcceptanceEventType.CleanupCompleted,
                AcceptanceObservationOutcome.Failed,
                failureCode: AcceptanceFailureCodes.CleanupFailed,
                cancellationToken: cancellationToken);
            return new AcceptanceCleanupResult("failed", AcceptanceFailureCodes.CleanupFailed);
        }
    }

    private async Task VerifyOwnershipAsync(CancellationToken cancellationToken)
    {
        if (_catalog is null || _applicationConnectionString is null || _pluginRoot is null)
        {
            throw new InvalidOperationException("Acceptance resources were not fully identified.");
        }
        ValidateCatalogName(_catalog);
        EnsureSafePluginRoot(_pluginRoot);
        if (!Directory.Exists(_pluginRoot))
        {
            throw new InvalidOperationException("Acceptance plugin root disappeared before teardown.");
        }

        var statePath = Path.Combine(_repositoryRoot, ".codex-temp", "dev", "state.json");
        using (var state = JsonDocument.Parse(await File.ReadAllTextAsync(statePath, cancellationToken)))
        {
            var stateRunId = state.RootElement.GetProperty("runId").GetString();
            if (!string.Equals(stateRunId, _lastDevRunId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Development supervisor state no longer belongs to the acceptance-started run.");
            }
        }

        var expectedOwnership = _ownershipSnapshot
            ?? throw new InvalidOperationException("Acceptance catalog ownership was not captured after startup.");
        var currentOwnership = await ReadOwnershipSnapshotAsync(cancellationToken);
        if (currentOwnership != expectedOwnership)
        {
            throw new InvalidOperationException("Acceptance catalog ownership marker changed before teardown.");
        }
    }

    private async Task<DatabaseOwnershipSnapshot> ReadOwnershipSnapshotAsync(CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_applicationConnectionString!);
        await connection.OpenAsync(cancellationToken);
        if (!string.Equals(connection.Database, _catalog, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Connected acceptance catalog identity changed.");
        }

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT [Version], [RepositoryId], [WorktreeId], [Provider], [ServerFingerprint], [CatalogFingerprint]
            FROM [dbo].[__NewPDatabaseOwnership]
            WHERE [MarkerId] = 1;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("Acceptance catalog ownership marker was missing.");
        }

        var snapshot = new DatabaseOwnershipSnapshot(
            reader.GetInt32(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5));
        if (await reader.ReadAsync(cancellationToken)
            || snapshot.Version != 1
            || !string.Equals(snapshot.Provider, "SqlServer", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(snapshot.RepositoryId)
            || string.IsNullOrWhiteSpace(snapshot.WorktreeId)
            || string.IsNullOrWhiteSpace(snapshot.ServerFingerprint)
            || string.IsNullOrWhiteSpace(snapshot.CatalogFingerprint))
        {
            throw new InvalidOperationException("Acceptance catalog ownership marker was invalid.");
        }

        return snapshot;
    }

    private async Task VerifyTrackedTreeAsync(string trackedTreeBefore, CancellationToken cancellationToken)
    {
        var after = await AcceptanceInfrastructure.RunGitAsync(
            _repositoryRoot,
            ["status", "--porcelain", "--untracked-files=no"],
            cancellationToken);
        var matched = string.Equals(trackedTreeBefore, after, StringComparison.Ordinal);
        await _recorder.RecordAsync(
            AcceptanceGateKind.Personnel,
            AcceptanceEventType.TreeVerified,
            matched ? AcceptanceObservationOutcome.Succeeded : AcceptanceObservationOutcome.Failed,
            failureCode: matched ? null : AcceptanceFailureCodes.TreeChanged,
            cancellationToken: cancellationToken);
    }

    private async Task ValidateEvidenceAsync(CancellationToken cancellationToken)
    {
        try
        {
            foreach (var artifactRef in _artifactRefs.Distinct(StringComparer.Ordinal))
            {
                var path = Path.GetFullPath(Path.Combine(_runDirectory, artifactRef.Replace('/', Path.DirectorySeparatorChar)));
                if (!path.StartsWith(_runDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                    || !File.Exists(path)
                    || new FileInfo(path).Length == 0)
                {
                    throw new InvalidOperationException($"Evidence artifact is missing or empty: {artifactRef}");
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
                AcceptanceGateKind.Personnel,
                AcceptanceEventType.EvidenceRecorded,
                AcceptanceObservationOutcome.Failed,
                failureCode: AcceptanceFailureCodes.EvidenceUnreadable,
                cancellationToken: cancellationToken);
            await WriteEvidenceAsync("personnel/evidence-validation-failure.json", new
            {
                status = "failed",
                diagnostic = Sanitize(exception.Message)
            }, cancellationToken);
        }
    }

    private async Task ScanEvidenceForSecretsAsync(CancellationToken cancellationToken)
    {
        var patterns = _secretTokens
            .Where(static value => !string.IsNullOrWhiteSpace(value) && value.Length >= 8)
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
            AcceptanceGateKind.Personnel,
            AcceptanceEventType.SecretScanCompleted,
            leaked ? AcceptanceObservationOutcome.Failed : AcceptanceObservationOutcome.Succeeded,
            failureCode: leaked ? AcceptanceFailureCodes.SecretDetected : null,
            cancellationToken: cancellationToken);
    }

    private async Task RecordScenarioSuccessAsync(
        string scenario,
        string evidencePath,
        object evidence,
        CancellationToken cancellationToken)
    {
        var evidenceRef = await WriteEvidenceAsync(evidencePath, evidence, cancellationToken);
        await _recorder.RecordAsync(AcceptanceGateKind.Personnel, AcceptanceEventType.ScenarioCompleted, scenario: scenario, cancellationToken: cancellationToken);
        await _recorder.RecordAsync(AcceptanceGateKind.Personnel, AcceptanceEventType.EvidenceRecorded, scenario: scenario, evidenceRef: evidenceRef, cancellationToken: cancellationToken);
    }

    private async Task<string> WriteEvidenceAsync(string relativePath, object value, CancellationToken cancellationToken)
    {
        var normalized = relativePath.Replace('/', Path.DirectorySeparatorChar);
        var fullPath = Path.GetFullPath(Path.Combine(_runDirectory, normalized));
        if (!fullPath.StartsWith(_runDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Evidence path escaped the acceptance run directory.");
        }
        await AcceptanceInfrastructure.WriteJsonAsync(fullPath, value, cancellationToken);
        var artifactRef = AcceptanceInfrastructure.RelativeArtifact(_runDirectory, fullPath);
        if (!_artifactRefs.Contains(artifactRef, StringComparer.Ordinal))
        {
            _artifactRefs.Add(artifactRef);
        }
        return artifactRef;
    }

    private async Task StartDevAsync(string evidencePath, CancellationToken cancellationToken)
    {
        await RunDevScriptAsync("dev.ps1", evidencePath, cancellationToken);
        var statePath = Path.Combine(_repositoryRoot, ".codex-temp", "dev", "state.json");
        using var state = JsonDocument.Parse(await File.ReadAllTextAsync(statePath, cancellationToken));
        var root = state.RootElement;
        if (!string.Equals(root.GetProperty("status").GetString(), "ready", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Development supervisor did not reach ready state.");
        }
        _lastDevRunId = root.GetProperty("runId").GetString()
            ?? throw new InvalidOperationException("Development supervisor state omitted runId.");
        _hostPort = root.GetProperty("ports").GetProperty("host").GetInt32();
        _shellPort = root.GetProperty("ports").GetProperty("shell").GetInt32();
        foreach (var name in new[] { "host", "shell" })
        {
            var process = root.GetProperty("processes").GetProperty(name);
            _processIdentities.Add($"{name}:{process.GetProperty("pid").GetInt32()}:{_lastDevRunId}");
        }
    }

    private async Task RunDevScriptAsync(
        string scriptName,
        string evidencePath,
        CancellationToken cancellationToken,
        bool allowAlreadyStopped = false)
    {
        var scriptPath = Path.Combine(_repositoryRoot, "scripts", scriptName);
        var arguments = new List<string> { "-NoLogo", "-NoProfile", "-File", scriptPath };
        if (scriptName.Equals("dev.ps1", StringComparison.OrdinalIgnoreCase))
        {
            // 远端空库首次同步和 LocalAdmin 权限收敛可能超过开发默认值；验收等待上限仍由脚本显式约束。
            arguments.AddRange([
                "-HostStartupTimeoutSeconds", "600",
                "-ShellStartupTimeoutSeconds", "180",
                "-ShellRequestTimeoutSeconds", "60",
                "-HostEnvironmentName", "Acceptance",
                "-SkipDependencyInstall"
            ]);
        }
        var result = await AcceptanceInfrastructure.RunProcessAsync(
            "pwsh.exe",
            arguments,
            _repositoryRoot,
            scriptName.Equals("dev.ps1", StringComparison.OrdinalIgnoreCase) ? _devEnvironment : null,
            redirectOutput: !scriptName.Equals("dev.ps1", StringComparison.OrdinalIgnoreCase),
            cancellationToken);
        var devResult = scriptName.Equals("dev.ps1", StringComparison.OrdinalIgnoreCase)
            ? ReadDevResultEvidence()
            : null;
        await WriteEvidenceAsync(evidencePath, new
        {
            script = scriptName,
            exitCode = result.ExitCode,
            standardOutput = Sanitize(result.StandardOutput),
            standardError = Sanitize(result.StandardError),
            supervisorResult = devResult
        }, cancellationToken);
        if (result.ExitCode != 0)
        {
            if (scriptName.Equals("dev.ps1", StringComparison.OrdinalIgnoreCase))
            {
                CaptureFailedDevRunIdentity();
            }
            var diagnostic = string.IsNullOrWhiteSpace(result.StandardError)
                ? result.StandardOutput
                : result.StandardError;
            throw new InvalidOperationException($"{scriptName} failed: {Sanitize(diagnostic)}");
        }
        if (!allowAlreadyStopped && scriptName.Equals("stop-dev.ps1", StringComparison.OrdinalIgnoreCase)
            && !result.StandardOutput.Contains("DEV_STOPPED", StringComparison.Ordinal)
            && !result.StandardOutput.Contains("DEV_ALREADY_STOPPED", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("stop-dev.ps1 did not report a recognized terminal state.");
        }
    }

    private object? ReadDevResultEvidence()
    {
        try
        {
            var path = Path.Combine(_repositoryRoot, ".codex-temp", "dev", "current-result.json");
            using var json = JsonDocument.Parse(File.ReadAllText(path));
            var root = json.RootElement;
            return new
            {
                status = root.GetProperty("status").GetString(),
                reused = root.TryGetProperty("reused", out var reused) && reused.GetBoolean(),
                failureCode = root.TryGetProperty("failureCode", out var failure) ? failure.GetString() : null,
                durationMs = root.TryGetProperty("durationMs", out var duration) ? duration.GetInt64() : 0
            };
        }
        catch
        {
            return null;
        }
    }

    private void CaptureFailedDevRunIdentity()
    {
        try
        {
            var statePath = Path.Combine(_repositoryRoot, ".codex-temp", "dev", "state.json");
            using var state = JsonDocument.Parse(File.ReadAllText(statePath));
            _lastDevRunId = state.RootElement.GetProperty("runId").GetString();
        }
        catch
        {
            // 失败态若连监督器身份都无法读取，后续归属重验会安全失败并保留资源。
        }
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
                throw new InvalidOperationException("Acceptance catalog count changed before teardown.");
            }
        }
        await using var command = connection.CreateCommand();
        command.CommandText = $"ALTER DATABASE [{_catalog}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_catalog}];";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static HttpClient CreateClient(int port)
    {
        var handler = new HttpClientHandler
        {
            CookieContainer = new CookieContainer(),
            AllowAutoRedirect = false,
            UseCookies = true
        };
        return new HttpClient(handler)
        {
            BaseAddress = new Uri($"http://127.0.0.1:{port}"),
            Timeout = TimeSpan.FromMinutes(5)
        };
    }

    private async Task LoginAdminAsync(HttpClient client, CancellationToken cancellationToken)
    {
        var credentials = _credentials ?? throw new InvalidOperationException("Local credentials were not loaded.");
        using var response = await client.PostAsJsonAsync("/api/auth/login", new
        {
            username = credentials.Username,
            password = credentials.Password
        }, cancellationToken);
        EnsureStatus(response, HttpStatusCode.OK, await response.Content.ReadAsStringAsync(cancellationToken), "admin login");
    }

    private async Task<EntityId> PostAndReadIdAsync(
        HttpClient client,
        string path,
        object payload,
        CancellationToken cancellationToken)
    {
        using var response = await client.PostAsJsonAsync(path, payload, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        EnsureStatus(response, HttpStatusCode.Created, body, path);
        using var json = JsonDocument.Parse(body);
        return new EntityId(ReadString(json.RootElement, "id"));
    }

    private async Task AssertPersonErrorAsync(
        HttpClient client,
        string check,
        IReadOnlyDictionary<string, object?> payload,
        string expectedCode,
        ICollection<object> checks,
        CancellationToken cancellationToken)
    {
        using var response = await client.PostAsJsonAsync("/api/organization/persons", payload, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        EnsureStatus(response, HttpStatusCode.BadRequest, body, check);
        using var json = JsonDocument.Parse(body);
        var code = ReadString(json.RootElement, "code");
        if (!string.Equals(code, expectedCode, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{check} returned {code}, expected {expectedCode}.");
        }
        checks.Add(new { check, status = 400, code });
    }

    private static async Task PutCompanyAsync(
        HttpClient client,
        string id,
        string name,
        bool enabled,
        CancellationToken cancellationToken)
    {
        using var response = await client.PutAsJsonAsync($"/api/organization/companies/{Uri.EscapeDataString(id)}", new
        {
            displayName = name,
            isEnabled = enabled,
            sortOrder = "10"
        }, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        EnsureStatus(response, HttpStatusCode.OK, body, "update company state");
    }

    private static async Task PutDepartmentAsync(
        HttpClient client,
        string id,
        string companyId,
        string name,
        bool enabled,
        CancellationToken cancellationToken)
    {
        using var response = await client.PutAsJsonAsync($"/api/organization/departments/{Uri.EscapeDataString(id)}", new
        {
            displayName = name,
            companyId,
            parentDepartmentId = (string?)null,
            isEnabled = enabled,
            sortOrder = "10"
        }, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        EnsureStatus(response, HttpStatusCode.OK, body, "update department state");
    }

    private static Dictionary<string, object?> PersonPayload(
        string personNo,
        string name,
        string? companyId,
        string? departmentId) =>
        new(StringComparer.Ordinal)
        {
            ["PersonNo"] = personNo,
            ["ENAME"] = name,
            ["CompanyId"] = companyId,
            ["DepartmentId"] = departmentId,
            ["PositionId"] = null,
            ["EmploymentStatus"] = "FullTime",
            ["IsEnabled"] = true
        };

    private static void AssertPersonBody(string body, PersonData person, FixtureData fixture)
    {
        foreach (var expected in new[] { person.Id, person.PersonNo, person.Name, fixture.CompanyName, fixture.DepartmentName })
        {
            if (!body.Contains(expected, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Personnel detail omitted expected value: {expected}");
            }
        }
    }

    private static void EnsureStatus(HttpResponseMessage response, HttpStatusCode expected, string body, string operation)
    {
        if (response.StatusCode != expected)
        {
            throw new InvalidOperationException(
                $"{operation} returned {(int)response.StatusCode}, expected {(int)expected}: {body}");
        }
    }

    private static string ReadString(JsonElement element, string property)
    {
        foreach (var item in element.EnumerateObject())
        {
            if (string.Equals(item.Name, property, StringComparison.OrdinalIgnoreCase))
            {
                return item.Value.GetString()
                    ?? throw new InvalidOperationException($"JSON property {property} was null.");
            }
        }
        throw new InvalidOperationException($"JSON property {property} was missing.");
    }

    private static string ReadNestedString(JsonElement element, string parent, string property)
    {
        foreach (var item in element.EnumerateObject())
        {
            if (string.Equals(item.Name, parent, StringComparison.OrdinalIgnoreCase))
            {
                return ReadString(item.Value, property);
            }
        }
        throw new InvalidOperationException($"JSON property {parent}.{property} was missing.");
    }

    private LocalCredentials ReadLocalCredentials()
    {
        var path = Path.Combine(_repositoryRoot, "apps", "host-api", "appsettings.json");
        using var json = JsonDocument.Parse(File.ReadAllText(path));
        var localAuth = json.RootElement.GetProperty("LocalAuth");
        return new LocalCredentials(
            localAuth.GetProperty("Username").GetString()!,
            localAuth.GetProperty("Password").GetString()!);
    }

    private void AddConnectionSecrets(string rawConnectionString, string password)
    {
        _secretTokens.Add(rawConnectionString);
        if (!string.IsNullOrEmpty(password))
        {
            _secretTokens.Add($"Password={password}");
            _secretTokens.Add($"Pwd={password}");
        }
    }

    private string Sanitize(string value)
    {
        var sanitized = value;
        foreach (var secret in _secretTokens.Where(static item => !string.IsNullOrEmpty(item)).OrderByDescending(static item => item.Length))
        {
            sanitized = sanitized.Replace(secret, "[REDACTED]", StringComparison.Ordinal);
        }
        return sanitized;
    }

    private void ValidateCatalogName(string catalog)
    {
        const string pattern = "^NewP_acceptance_[a-f0-9]{24}$";
        if (!Regex.IsMatch(catalog, pattern, RegexOptions.CultureInvariant))
        {
            throw new InvalidOperationException("Refusing to operate on a catalog outside the Personnel acceptance naming boundary.");
        }
    }

    private void EnsureSafePluginRoot(string pluginRoot)
    {
        var expected = Path.GetFullPath(Path.Combine(_runDirectory, "resources", "plugins"));
        if (!string.Equals(Path.GetFullPath(pluginRoot), expected, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Acceptance plugin root escaped its owned resource directory.");
        }
        if ((File.GetAttributes(pluginRoot) & FileAttributes.ReparsePoint) != 0
            || Directory.EnumerateFileSystemEntries(pluginRoot, "*", SearchOption.AllDirectories)
                .Any(path => (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0))
        {
            throw new InvalidOperationException("Acceptance plugin root contains a reparse point and will be preserved.");
        }
    }

    private static string ResolveChromePath()
    {
        var configured = Environment.GetEnvironmentVariable("NEWP_ACCEPTANCE_CHROMIUM_EXECUTABLE");
        var candidates = new[]
        {
            configured,
            @"C:\Program Files\Google\Chrome\Application\chrome.exe",
            @"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Google", "Chrome", "Application", "chrome.exe")
        };
        return candidates.FirstOrDefault(static path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
            ?? throw new InvalidOperationException("No Chromium executable was found for the Personnel browser observer.");
    }

    private static void AssertContains(string actual, string expected, string label)
    {
        if (!actual.Contains(expected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Browser assertion failed for {label}.");
        }
    }

    private static string FailureCodeFor(string scenario) =>
        scenario switch
        {
            PreflightScenario => AcceptanceFailureCodes.PersonnelPreflightFailed,
            ApiScenario => AcceptanceFailureCodes.PersonnelApiFailed,
            BrowserScenario => AcceptanceFailureCodes.PersonnelBrowserFailed,
            RestartScenario => AcceptanceFailureCodes.PersonnelRestartFailed,
            _ => AcceptanceFailureCodes.ObserverFailed
        };

    private sealed record LocalCredentials(string Username, string Password);
    private sealed record FixtureData(
        string RunId,
        string AdminUserId,
        string ReadOnlyUserId,
        string CompanyId,
        string CompanyName,
        string DepartmentId,
        string DepartmentName);
    private sealed record PersonData(string Id, string PersonNo, string Name);
    private sealed record EntityId(string Id);
    private sealed record DatabaseOwnershipSnapshot(
        int Version,
        string RepositoryId,
        string WorktreeId,
        string Provider,
        string ServerFingerprint,
        string CatalogFingerprint);
}
