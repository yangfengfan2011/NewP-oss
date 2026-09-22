using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.Playwright;

namespace ICP.Platform.Acceptance;

internal sealed record PersonnelProductionColdStartExecution(
    string? DatabaseFingerprint,
    IReadOnlyList<string> ProcessIdentities,
    IReadOnlyList<string> ArtifactRefs,
    IReadOnlyList<ProductionColdStartSample> Samples,
    ProductionColdStartCapabilityRecoveryProbe? CapabilityRecovery,
    string ChromiumVersion,
    AcceptanceCleanupResult Cleanup,
    string? FailureDiagnostic);

internal sealed class PersonnelAcceptanceRunner : AcceptanceRunner
{
    internal const string PreflightScenario = "personnel.preflight";
    internal const string ApiScenario = "personnel.api";
    internal const string BrowserScenario = "personnel.browser";
    internal const string RestartScenario = "personnel.restart";

    private const string AuthenticationCookieName = "ICP.Platform.PersonnelAcceptance";
    private const string PersonnelBirthDate = "2000-02-29";
    internal static IReadOnlyList<(string Label, string Value)> PersonnelTemporalFormInputs { get; } =
    [
        ("出生日期", PersonnelBirthDate),
        ("任职开始时间", "2030-01-02 08:04:05"),
        ("任职结束时间", "2030-06-30 17:30:00")
    ];
    internal const string PersonnelTemporalInputExitKey = "Tab";
    private static readonly JsonSerializerOptions WebJsonOptions = new(JsonSerializerDefaults.Web);
    private readonly List<string> _artifactRefs = [];
    private readonly List<string> _processIdentities = [];
    private readonly Dictionary<string, string?> _devEnvironment = new(StringComparer.OrdinalIgnoreCase);
    private string? _adminConnectionString;
    private string? _adminConnectionSource;
    private string? _applicationConnectionString;
    private string? _catalog;
    private string? _pluginRoot;
    private string? _keyRingRoot;
    private string? _lastDevRunId;
    private int _hostPort;
    private AcceptanceFeatureCatalog? _features;
    private int _shellPort;
    private bool _catalogCreated;
    private FixtureData? _fixture;
    private PersonData? _person;
    private CompanyData? _company;
    private DepartmentData? _department;
    private PositionData? _position;
    private string? _roleId;
    private string? _readerRoleId;
    private OrdinaryActor? _maintainer;
    private OrdinaryActor? _reader;
    private sealed record OrdinaryActor(string Id, string ECode, string Password);
    private string? _adminPassword;
    private DatabaseOwnershipSnapshot? _ownershipSnapshot;
    private bool _secretLeakDetected;

    public PersonnelAcceptanceRunner(
        string repositoryRoot,
        string runDirectory,
        string runId)
        : base(repositoryRoot, runDirectory, runId)
    {
    }

    protected override async Task RunScenariosAsync(CancellationToken cancellationToken)
    {
        await RunScenarioAsync(PreflightScenario, RunPreflightScenarioAsync, cancellationToken);
        await RunScenarioAsync(ApiScenario, RunApiScenarioAsync, cancellationToken);
        await RunScenarioAsync(
            BrowserScenario,
            token => RunBrowserScenarioAsync("initial", token),
            cancellationToken);
        await RunScenarioAsync(RestartScenario, RunRestartScenarioAsync, cancellationToken);
    }

    // 现场快照与 preflight 同属一个场景：写快照失败也该记到 preflight 头上，
    // 所以它跟着 preflight 一起进 RunScenarioAsync，而不是裸挂在场景序列里。
    private async Task RunPreflightScenarioAsync(CancellationToken cancellationToken)
    {
        await RunPreflightAsync(cancellationToken);
        await RecordScenarioSuccessAsync(PreflightScenario, "personnel/preflight.json", new
        {
            status = "passed",
            connectionSource = _adminConnectionSource,
            databaseFingerprint = AcceptanceInfrastructure.Sha256(_catalog!),
            pluginRootFingerprint = AcceptanceInfrastructure.Sha256(_pluginRoot!),
            hostPort = _hostPort,
            shellPort = _shellPort
        }, cancellationToken);
    }

    /// <summary>
    /// 复用 Personnel 一次性 Host/catalog/actor ownership seam，只运行 production cold-start 真实浏览器观察。
    /// </summary>
    public async Task<PersonnelProductionColdStartExecution> RunProductionColdStartAsync(
        string assetsRoot,
        int samplesPerScenario,
        ProductionColdStartThrottleProfile throttle,
        IReadOnlyList<string> targetCapabilityAssets,
        string rawSamplesPath,
        CancellationToken cancellationToken)
    {
        AcceptanceCleanupResult cleanup = new("not-started", AcceptanceFailureCodes.CleanupIncomplete);
        ProductionColdStartBrowserExecution? browserExecution = null;
        string? failureDiagnostic = null;

        try
        {
            await RunPreflightAsync(cancellationToken);
            var unauthorized = await CreateUnauthorizedActorAsync(cancellationToken);
            browserExecution = await new ProductionColdStartBrowserRunner().RunAsync(
                new ProductionColdStartBrowserRequest(
                    _repositoryRoot,
                    _runDirectory,
                    _runId,
                    assetsRoot,
                    _hostPort,
                    "admin",
                    _adminPassword ?? throw new InvalidOperationException("Administrator password was not prepared."),
                    unauthorized.ECode,
                    unauthorized.Password,
                    samplesPerScenario,
                    throttle,
                    targetCapabilityAssets,
                    rawSamplesPath),
                cancellationToken);
            failureDiagnostic = browserExecution.FailureDiagnostic
                ?? browserExecution.CapabilityRecovery?.Diagnostic;
            if (!string.Equals(browserExecution.ProductionServerIdentity, "unavailable", StringComparison.Ordinal))
            {
                _processIdentities.Add(browserExecution.ProductionServerIdentity);
            }
            foreach (var path in new[]
                     {
                         rawSamplesPath,
                         Path.Combine(_runDirectory, "capability-recovery.json"),
                         Path.Combine(_runDirectory, "production-shell", "ready.json"),
                         Path.Combine(_runDirectory, "production-shell", "stdout.log"),
                         Path.Combine(_runDirectory, "production-shell", "stderr.log")
                     }.Where(path => File.Exists(path) && new FileInfo(path).Length > 0))
            {
                var artifact = AcceptanceInfrastructure.RelativeArtifact(_runDirectory, path);
                if (!_artifactRefs.Contains(artifact, StringComparer.Ordinal))
                {
                    _artifactRefs.Add(artifact);
                }
            }
        }
        catch (Exception exception)
        {
            failureDiagnostic = Sanitize(exception.Message);
            await WriteEvidenceAsync(
                "production-cold-start/browser-failure.json",
                new
                {
                    status = "failed",
                    failureCode = AcceptanceFailureCodes.ProductionColdStartRequestEvidenceMissing,
                    diagnostic = failureDiagnostic
                },
                cancellationToken);
        }
        finally
        {
            cleanup = await TeardownAsync(CancellationToken.None);
            _secretLeakDetected = await ScanEvidenceForSecretsAsync(CancellationToken.None);
        }

        if (browserExecution?.ProductionServerCleanup.Status is not "completed")
        {
            cleanup = browserExecution?.ProductionServerCleanup
                ?? new AcceptanceCleanupResult("failed", AcceptanceFailureCodes.CleanupIncomplete);
        }
        if (_secretLeakDetected)
        {
            cleanup = new AcceptanceCleanupResult("failed", AcceptanceFailureCodes.SecretDetected);
        }

        return new PersonnelProductionColdStartExecution(
            _catalog is null ? null : AcceptanceInfrastructure.Sha256(_catalog),
            _processIdentities,
            _artifactRefs,
            browserExecution?.Samples ?? [],
            browserExecution?.CapabilityRecovery,
            browserExecution?.ChromiumVersion ?? "unavailable",
            cleanup,
            failureDiagnostic);
    }

    private async Task RunPreflightAsync(CancellationToken cancellationToken)
    {
        var adminConnection = AcceptanceSqlServerConnectionResolver.Resolve(_repositoryRoot);
        var adminBuilder = new SqlConnectionStringBuilder(adminConnection.AdminConnectionString);
        _adminConnectionString = adminConnection.AdminConnectionString;
        _adminConnectionSource = adminConnection.SourceName;
        AddConnectionSecrets(adminConnection.ConfiguredConnectionString, adminConnection.Password);

        _catalog = $"NewP_acceptance_{_runId}";
        ValidateCatalogName(_catalog);
        var appBuilder = new SqlConnectionStringBuilder(adminBuilder.ConnectionString)
        {
            InitialCatalog = _catalog,
            Pooling = false
        };
        _applicationConnectionString = appBuilder.ConnectionString;

        _pluginRoot = Path.Combine(_runDirectory, "resources", "plugins");
        _keyRingRoot = Path.Combine(_runDirectory, "resources", "key-ring");
        Directory.CreateDirectory(_pluginRoot);
        Directory.CreateDirectory(_keyRingRoot);
        if (Directory.EnumerateFileSystemEntries(_pluginRoot).Any())
        {
            throw new InvalidOperationException("Personnel gate plugin root was not empty before startup.");
        }

        var fixtureSecret = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32))
            .ToLowerInvariant();
        RegisterSecret(fixtureSecret);
        _devEnvironment["NEWP_DEV_SQLSERVER_CONNECTION_STRING"] = _applicationConnectionString;
        _devEnvironment["ConnectionStrings__SqlServer"] = _applicationConnectionString;
        _devEnvironment["Platform__PluginInstallRoot"] = _pluginRoot;
        _devEnvironment["Authentication__Host__KeyRingPath"] = _keyRingRoot;
        _devEnvironment["Authentication__Host__CookieName"] = AuthenticationCookieName;
        _devEnvironment["Authentication__Host__AllowInsecureDevelopmentCookie"] = "true";
        _devEnvironment["Identity__LocalAdministrator__ECODE"] = "admin";
        _devEnvironment["Identity__LocalAdministrator__ENAME"] = "Personnel Acceptance Administrator";
        _devEnvironment["ASPNETCORE_ENVIRONMENT"] = "Development";
        _devEnvironment["Acceptance__Enabled"] = "true";
        _devEnvironment["Acceptance__RunId"] = _runId;
        _devEnvironment["Acceptance__Secret"] = fixtureSecret;
        _devEnvironment["Logging__LogLevel__Default"] = "Information";

        await RunDevScriptAsync("stop-dev.ps1", "personnel/preflight-stop-existing.json", cancellationToken, allowAlreadyStopped: true);
        await CreateCatalogAsync(cancellationToken);
        await ObserveBootstrapRequiredDevAsync(cancellationToken);
        var bootstrap = await FormalIdentityBootstrap.RunAsync(
            _repositoryRoot,
            Path.Combine(_runDirectory, "resources"),
            _devEnvironment,
            cancellationToken);
        RegisterSecret(bootstrap.TemporaryCredential);
        await StartDevAsync("personnel/preflight-dev-start.json", cancellationToken);
        _ownershipSnapshot = await ReadOwnershipSnapshotAsync(cancellationToken);

        using var administrator = CreateClient(_hostPort);
        using (var restrictedLogin = await LoginAsync(
                   administrator,
                   "admin",
                   bootstrap.TemporaryCredential,
                   cancellationToken))
        {
            var revision = ReadLong(restrictedLogin.RootElement, "revision");
            _adminPassword = $"Adm-{_runId}-Pass";
            RegisterSecret(_adminPassword);
            _ = await ChangePasswordAsync(
                administrator,
                bootstrap.TemporaryCredential,
                _adminPassword,
                revision,
                cancellationToken);
        }

        using (var response = await SendJsonWithAntiforgeryAsync(
                   administrator,
                   HttpMethod.Post,
                   "/api/acceptance/personnel/prepare",
                   new { },
                   cancellationToken,
                   request => request.Headers.Add("X-NewP-Acceptance-Secret", fixtureSecret)))
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            EnsureStatus(response, HttpStatusCode.OK, body, "fixture prepare");
            _fixture = JsonSerializer.Deserialize<FixtureData>(body, WebJsonOptions)
                ?? throw new InvalidOperationException("Personnel fixture response was empty.");
        }
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

        if (!string.Equals(_fixture.RunId, _runId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Personnel fixture run identity did not match the acceptance run.");
        }
    }

    // 无权限场景通过正式 Identity API 创建一次性普通账号，不直接修改数据库或伪造认证状态。
    private async Task<(string ECode, string Password)> CreateUnauthorizedActorAsync(CancellationToken cancellationToken)
    {
        var actor = await CreateOrdinaryActorAsync("denied", cancellationToken);
        return (actor.ECode, actor.Password);
    }

    // 普通账号先有可用组织及永久绑定再首次登录；denied仍不赋角色，不能把准入失败冒充权限拒绝。
    private async Task<OrdinaryActor> CreateOrdinaryActorAsync(string purpose, CancellationToken cancellationToken)
    {
        var ecode = $"acceptance.{purpose}.{_runId[..8]}";
        var password = $"Denied-{_runId}-Pass";
        using var administrator = CreateClient(_hostPort);
        await LoginAdminAsync(administrator, cancellationToken);
        using var created = await SendJsonWithAntiforgeryAsync(
            administrator,
            HttpMethod.Post,
            "/api/identity/accounts",
            new { ecode, ename = $"Personnel {purpose} {_runId[..8]}" },
            cancellationToken, request => request.Headers.Add("X-NewP-Feature",
                _features!.Action(_features.Behavior("Identity", "identity.account.read"), "create", "list.pageActions").Position));
        var createdBody = await created.Content.ReadAsStringAsync(cancellationToken);
        EnsureStatus(created, HttpStatusCode.Created, createdBody, "create unauthorized acceptance account");
        using var createdJson = JsonDocument.Parse(createdBody);
        var temporaryCredential = ReadString(createdJson.RootElement, "temporaryCredential");
        RegisterSecret(temporaryCredential);
        RegisterSecret(password);

        var fixture = _fixture ?? throw new InvalidOperationException("Personnel fixture was not prepared.");
        var account = createdJson.RootElement.GetProperty("account");
        var accountId = ReadString(account, "eid");
        var person = await CreatePersonAsync(administrator, $"ACC-LOGIN-{purpose}-{_runId[..8]}",
            $"Personnel login {purpose} {_runId[..8]}", fixture.CompanyId, fixture.DepartmentId, cancellationToken);
        using (var binding = await SendJsonWithAntiforgeryAsync(administrator, HttpMethod.Put,
            $"/api/organization/persons/{person.Id}/user-binding",
            new { accountEid = accountId, expectedRevision = ReadLong(account, "revision"), reason = "验收账号登录前准备" }, cancellationToken,
            request => request.Headers.Add("X-NewP-Feature", _features!.Action(_features.OrganizationTab("persons"), "linkUser", "detail.custom").Position)))
        {
            var body = await binding.Content.ReadAsStringAsync(cancellationToken);
            EnsureStatus(binding, HttpStatusCode.OK, body, "prepare acceptance account organization binding");
        }

        using var unauthorized = CreateClient(_hostPort);
        using var restrictedLogin = await LoginAsync(
            unauthorized,
            ecode,
            temporaryCredential,
            cancellationToken);
        _ = await ChangePasswordAsync(
            unauthorized,
            temporaryCredential,
            password,
            ReadLong(restrictedLogin.RootElement, "revision"),
            cancellationToken);
        return new OrdinaryActor(accountId, ecode, password);
    }

    // API 场景只准备浏览器旅程所需的真实人员；字段语义、权限和错误矩阵留在低层集成测试。
    private async Task RunApiScenarioAsync(CancellationToken cancellationToken)
    {
        var fixture = _fixture ?? throw new InvalidOperationException("Personnel fixture was not prepared.");
        using var admin = CreateClient(_hostPort);
        await LoginAdminAsync(admin, cancellationToken);

        _maintainer = await CreateOrdinaryActorAsync("writer", cancellationToken);
        _reader = await CreateOrdinaryActorAsync("reader", cancellationToken);
        var personNo = $"ACC-{_runId[..10]}";
        var personName = $"Personnel Gate Person {_runId[..8]}";
        _person = await CreatePersonAsync(
            admin,
            personNo,
            personName,
            fixture.CompanyId,
            fixture.DepartmentId,
            cancellationToken);

        await RecordScenarioSuccessAsync(ApiScenario, "personnel/api.json", new
        {
            status = "passed",
            purpose = "browser-journey-seed",
            person = new { id = _person.Id, personNo, name = personName },
            organization = new { fixture.CompanyId, fixture.CompanyName, fixture.DepartmentId, fixture.DepartmentName }
        }, cancellationToken);
    }

    // 浏览器场景只通过正式 Shell 路由和 API 完成一条人员管理主旅程。
    private async Task RunBrowserScenarioAsync(string phase, CancellationToken cancellationToken)
    {
        var person = _person ?? throw new InvalidOperationException("Personnel API scenario did not create a person.");
        var adminPassword = _adminPassword ?? throw new InvalidOperationException("Administrator password was not prepared.");
        var screenshotPath = Path.Combine(_runDirectory, "personnel", $"browser-{phase}.png");
        Directory.CreateDirectory(Path.GetDirectoryName(screenshotPath)!);

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true,
            ExecutablePath = ResolveChromePath()
        });
        var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            Locale = "zh-CN",
            ViewportSize = new ViewportSize { Width = 1440, Height = 1000 }
        });
        var page = await context.NewPageAsync();
        var administratorPage = page;
        await using var maintainerContext = await browser.NewContextAsync(new BrowserNewContextOptions { Locale = "zh-CN", ViewportSize = new ViewportSize { Width = 1440, Height = 1000 } });
        await using var readerContext = await browser.NewContextAsync(new BrowserNewContextOptions { Locale = "zh-CN", ViewportSize = new ViewportSize { Width = 1440, Height = 1000 } });
        var maintainerPage = await maintainerContext.NewPageAsync();
        var readerPage = await readerContext.NewPageAsync();
        // 每个 actor 页面绑定一个页面驱动；切换 actor 就是换驱动实例。
        var administratorDriver = new StandardCrudPageDriver(administratorPage);
        var maintainerDriver = new StandardCrudPageDriver(maintainerPage);
        var readerDriver = new StandardCrudPageDriver(readerPage);
        var driver = administratorDriver;
        var browserErrors = new System.Collections.Concurrent.ConcurrentQueue<string>();
        var failedResponses = new System.Collections.Concurrent.ConcurrentQueue<IResponse>();
        var apiResponses = new System.Collections.Concurrent.ConcurrentQueue<object>();
        void ObservePage(IPage observedPage)
        {
            observedPage.PageError += (_, message) => browserErrors.Enqueue(message);
            observedPage.RequestFailed += (_, request) => browserErrors.Enqueue($"{request.Method} {request.Url}: {request.Failure}");
            observedPage.Response += (_, response) =>
            {
                if (response.Status >= 400) failedResponses.Enqueue(response);
                var path = new Uri(response.Url).AbsolutePath;
                if (path.StartsWith("/api/organization/", StringComparison.Ordinal) || path.StartsWith("/api/audit/", StringComparison.Ordinal) || path.StartsWith("/api/authorization/", StringComparison.Ordinal))
                    apiResponses.Enqueue(new { observedAtUtc = DateTimeOffset.UtcNow, method = response.Request.Method, path, query = new Uri(response.Url).Query, status = response.Status });
            };
            observedPage.SetDefaultTimeout(30_000);
            observedPage.SetDefaultNavigationTimeout(120_000);
        }
        foreach (var observedPage in new[] { page, maintainerPage, readerPage }) ObservePage(observedPage);
        async Task CapturePresentationAsync(string name)
        {
            var path = Path.Combine(_runDirectory, "personnel", name + ".png");
            await page.ScreenshotAsync(new PageScreenshotOptions { Path = path, FullPage = true });
            _artifactRefs.Add(AcceptanceInfrastructure.RelativeArtifact(_runDirectory, path));
        }
        try
        {
            await new ShellPageDriver(page, $"http://127.0.0.1:{_shellPort}").LoginAsync("admin", adminPassword);

            var browserAssertions = new List<string>();

            var maintainer = _maintainer ?? throw new InvalidOperationException("Maintainer was not prepared.");
            var reader = _reader ?? throw new InvalidOperationException("Reader was not prepared.");
            if (phase == "initial")
            {
                _roleId = await CreateRoleFromBrowserAndConfigureUsingApiAsync(driver, "M", true, cancellationToken);
                _readerRoleId = await CreateRoleFromBrowserAndConfigureUsingApiAsync(driver, "R", false, cancellationToken);
                await AssignAccountRoleFromBrowserAsync(driver, _roleId, "M", maintainer);
                await AssignAccountRoleFromBrowserAsync(driver, _readerRoleId, "R", reader);
                await new DataScopeRulesPageDriver(page).CreatePersonRuleAsync(
                    "人员旅程读取规则", $"Personnel Gate Role M {_runId[..8]}");
            }
            await OpenAuditDetailFromBrowserAsync(driver, maintainer.Id, "user.roles.changed", $"maintainer-{phase}");
            await OpenAuditDetailFromBrowserAsync(driver, reader.Id, "user.roles.changed", $"reader-{phase}");
            browserAssertions.Add("administrator-assigned-two-ordinary-account-roles-and-opened-change-details");
            await OpenAuditDetailFromBrowserAsync(driver,
                _roleId ?? throw new InvalidOperationException("Role configuration did not create a role."),
                "role.features.changed", phase);
            browserAssertions.Add("administrator-configured-role-and-opened-permission-change-detail");

            if (phase == "initial")
            {
                var company = await CreateCompanyFromBrowserAsync(driver, cancellationToken);
                var department = await CreateDepartmentFromBrowserAsync(driver, company, cancellationToken);
                await MaintainDepartmentFromBrowserAsync(driver, cancellationToken);
                await LocateDepartmentAndRestoreAddressAsync(driver, department, cancellationToken);
                var position = await CreatePositionFromBrowserAsync(driver, cancellationToken);
                var destination = await CreateDepartmentFromBrowserAsync(driver, company, cancellationToken, "B");
                await LoginBrowserActorAsync(maintainerPage, maintainer);
                await LoginBrowserActorAsync(readerPage, reader);
                page = maintainerPage;
                driver = maintainerDriver;
                person = await CreatePersonFromBrowserAsync(driver, company, department);
                _person = person;
                page = readerPage;
                driver = readerDriver;
                await OpenPersonnelFromShellAsync(driver);
                await AssertPersonListAndDrawerAsync(driver, person, company.Name, department.Name, null);
                await page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(_runDirectory, "personnel", "reader-person.png"), FullPage = true });
                page = maintainerPage;
                driver = maintainerDriver;
                person = await UpdatePersonFromBrowserAsync(driver, person, company, destination, position, cancellationToken, sameCompany: true);
                _company = company;
                _department = destination;
                _position = position;
                _person = person;
                await AssertPersonBirthDateRangeAsync(driver, person);
                browserAssertions.AddRange(
                [
                    "maintainer-created-person-reader-viewed-and-maintainer-moved-department",
                    "company-created",
                    "department-created-in-tree-and-edited-from-context-menu",
                    "temporary-department-moved-from-global-list-and-deleted-in-tree",
                    "department-search-location-restored-after-refresh-and-shared-address",
                    "position-created",
                    "person-organization-affiliation-saved-through-standard-drawer",
                    "person-moved-between-departments-in-same-company",
                    "birth-date-range-finds-person"
                ]);
            }

            if (phase != "initial")
            {
                await LoginBrowserActorAsync(maintainerPage, maintainer);
                await LoginBrowserActorAsync(readerPage, reader);
                page = readerPage;
                driver = readerDriver;
                await OpenPersonnelFromShellAsync(driver);
                await AssertPersonListAndDrawerAsync(driver, person, _company?.Name, _department?.Name, _position?.Name);
            }
            page = administratorPage;
            driver = administratorDriver;
            if (phase == "initial")
            {
                await DisableAndRestoreCompanyFromBrowserAsync(driver,
                    _company ?? throw new InvalidOperationException("Browser company was not created."), cancellationToken);
                browserAssertions.Add("administrator-disabled-and-restored-company-with-existing-departments-and-person");
            }
            await OpenAuditDetailFromBrowserAsync(driver, person.Id, "person.created", phase);
            browserAssertions.Add("administrator-opened-person-business-change-detail");
            await OpenAuditDetailFromBrowserAsync(driver, person.Id, "person.updated", phase);
            browserAssertions.Add("administrator-opened-person-update-difference-detail");
            if (phase == "initial")
            {
                page = maintainerPage;
                driver = maintainerDriver;
                var originalDrawer = await OpenPersonEditDrawerAsync(driver, person);
                await originalDrawer.GetByLabel("姓名", new LocatorGetByLabelOptions { Exact = true }).FillAsync(person.Name + " denied");
                page = administratorPage;
                driver = administratorDriver;
                await RevokeUpdateUsingApiAsync(_roleId!, cancellationToken);
                page = maintainerPage;
                driver = maintainerDriver;
                // 拒绝路径保持原有等待：Drawer 不会关闭，不能走驱动的提交生命周期。
                var rejected = page.WaitForResponseAsync(response => response.Request.Method == "PUT"
                    && new Uri(response.Url).AbsolutePath == $"/api/organization/persons/{person.Id}");
                await originalDrawer.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = StandardCrudPageDriver.SaveChangesLabel, Exact = true }).ClickAsync();
                var denial = await rejected;
                if (denial.Status != 403) throw new InvalidOperationException($"Original maintainer session write returned {denial.Status}, expected refusal.");
                await page.GetByText("当前操作已被拒绝，请核对当前权限。", new PageGetByTextOptions { Exact = true }).First
                    .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
                await driver.TableRow(person.PersonNo)
                    .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
                await Assertions.Expect(page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "编辑", Exact = true })).ToHaveCountAsync(0);
                await Assertions.Expect(page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "新建人员", Exact = true })).ToBeVisibleAsync();
                var deniedScreenshot = Path.Combine(_runDirectory, "personnel", "maintainer-original-session-denied.png");
                await page.ScreenshotAsync(new PageScreenshotOptions { Path = deniedScreenshot, FullPage = true });
                _artifactRefs.Add(AcceptanceInfrastructure.RelativeArtifact(_runDirectory, deniedScreenshot));
                browserAssertions.Add("original-maintainer-session-refused-after-admin-revoked-update-and-actions-refreshed");
                await originalDrawer.Locator(".ant-drawer-close").ClickAsync();
                var closed = originalDrawer.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden });
                var discard = page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "放弃更改", Exact = true });
                var confirmation = discard.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
                if (await Task.WhenAny(closed, confirmation) == confirmation)
                {
                    await confirmation;
                    await discard.ClickAsync();
                }
                await closed;
            }
            page = maintainerPage;
            driver = maintainerDriver;

            await driver.OpenOrganizationTabAsync("组织结构", "structure");
            var personTree = page.GetByRole(AriaRole.Complementary, new PageGetByRoleOptions { Name = "结构树" });
            await personTree.GetByLabel("搜索节点", new LocatorGetByLabelOptions { Exact = true }).FillAsync(person.Name);
            await personTree.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = $"定位节点 {person.Name}", Exact = true }).ClickAsync();
            await page.GetByRole(AriaRole.Region, new PageGetByRoleOptions { Name = "节点内容" })
                .GetByText("基础信息", new LocatorGetByTextOptions { Exact = true }).First
                .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
            var treeScreenshot = Path.Combine(_runDirectory, "personnel", $"tree-person-{phase}.png");
            await page.ScreenshotAsync(new PageScreenshotOptions { Path = treeScreenshot, FullPage = true });
            _artifactRefs.Add(AcceptanceInfrastructure.RelativeArtifact(_runDirectory, treeScreenshot));
            browserAssertions.Add("person-located-in-organization-tree-and-detail-opened");
            if (phase == "initial")
            {
                await page.SetViewportSizeAsync(900, 1000);
                await CapturePresentationAsync("tree-person-narrow");
                await page.SetViewportSizeAsync(1440, 1000);
            }
            // 旧人员书签仍进入同一个组合页面，且完整详情保留独立受控地址。
            await page.GotoAsync($"http://127.0.0.1:{_shellPort}/organization/persons");
            await page.WaitForURLAsync(url => new Uri(url).AbsolutePath == "/organization/structure" && StandardCrudPageDriver.HasQueryValue(new Uri(url), "tab", "persons"));
            await AssertPersonListAndDrawerAsync(
                driver,
                person,
                _company?.Name,
                _department?.Name,
                _position?.Name);
            await driver.VisibleDrawer().GetByRole(AriaRole.Button,
                new LocatorGetByRoleOptions { Name = "查看完整详情", Exact = true }).ClickAsync();
            await page.WaitForURLAsync($"http://127.0.0.1:{_shellPort}/organization/persons/{person.Id}");
            await page.GetByText(person.Name, new PageGetByTextOptions { Exact = true }).First
                .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
            browserAssertions.Add(phase == "initial"
                ? "list-and-detail-show-updated-person"
                : "restart-list-and-detail-show-persisted-person");

            await page.ScreenshotAsync(new PageScreenshotOptions { Path = screenshotPath, FullPage = true });
            _artifactRefs.Add(AcceptanceInfrastructure.RelativeArtifact(_runDirectory, screenshotPath));
            if (phase == "initial")
            {
                // 仅采样既有详情的直达/刷新呈现，不重复写入或新增字段语义断言。
                var detailPage = page;
                var address = detailPage.Url;
                page = await maintainerContext.NewPageAsync();
                ObservePage(page);
                driver = new StandardCrudPageDriver(page);
                await page.GotoAsync(address);
                await page.GetByText("基础信息", new PageGetByTextOptions { Exact = true }).WaitForAsync();
                await CapturePresentationAsync("person-detail-direct");
                await page.ReloadAsync();
                await page.GetByText("基础信息", new PageGetByTextOptions { Exact = true }).WaitForAsync();
                await CapturePresentationAsync("person-detail-refresh");
                // 失败时保留当前 page 供外层原始诊断；context 的现有释放路径兜底关闭。
                await page.CloseAsync();

                page = administratorPage;
                driver = administratorDriver;
                await page.GotoAsync($"http://127.0.0.1:{_shellPort}/authorization/roles/{_roleId}");
                await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "配置权限", Exact = true }).ClickAsync();
                var workbench = new AuthorizationWorkbenchPageDriver(page);
                await workbench.WaitForRoleButtonEnabledAsync(
                    $"Personnel Gate Role M {_runId[..8]}",
                    $"PersonnelGateRoleM{_runId[..8]}");
                await workbench.ExpandAllAsync();
                await CapturePresentationAsync("workbench-expanded-groups");
                await workbench.SearchFeaturesAsync("编辑");
                await CapturePresentationAsync("workbench-filtered-groups");
                page = detailPage;
                driver = maintainerDriver;
            }
            await WriteEvidenceAsync($"personnel/browser-{phase}.json", new
            {
                status = "passed",
                phase,
                url = page.Url,
                browserAssertions,
                screenshot = AcceptanceInfrastructure.RelativeArtifact(_runDirectory, screenshotPath)
            }, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // 排错失败不能覆盖原始场景异常；正文只经当前 runner 的凭据脱敏进入现有产物。
            var diagnostics = new List<string>(browserErrors);
            try
            {
                diagnostics.Add(await driver.ReadPageDiagnosticAsync());
                diagnostics.Add($"Visible DOM: {await page.Locator("body").InnerTextAsync()}");
                foreach (var overlay in await page.Locator(".ant-drawer-content:visible, .ant-modal:visible, [role=dialog]:visible, [role=alert]:visible").AllAsync())
                    diagnostics.Add($"Visible overlay: {await overlay.InnerTextAsync()}");
                foreach (var response in failedResponses)
                {
                    string body;
                    try { body = await response.TextAsync(); }
                    catch (PlaywrightException failure) { body = $"Response body unavailable: {failure.Message}"; }
                    diagnostics.Add($"{response.Request.Method} {response.Url}: {response.Status} {body}");
                }
                await WriteEvidenceAsync($"personnel/browser-{phase}-failure.json", new
                {
                    url = Sanitize(page.Url),
                    diagnostic = Sanitize(exception.ToString()),
                    apiResponses = apiResponses.ToArray(),
                    browserDiagnostics = diagnostics.Select(Sanitize).ToArray()
                }, CancellationToken.None);
                // 失败图片只观察当前页面与弹层；输入控件统一遮罩以免凭据出现在像素中。
                await page.ScreenshotAsync(new PageScreenshotOptions
                {
                    Path = Path.Combine(_runDirectory, "personnel", $"browser-{phase}-failure.png"),
                    FullPage = true, Mask = [page.Locator("input, textarea")]
                });
            }
            catch (Exception diagnosticFailure)
            {
                diagnostics.Add($"Browser diagnostic capture failed: {diagnosticFailure.Message}");
            }
            throw new InvalidOperationException(Sanitize($"{exception.Message}; URL: {page.Url}; {string.Join("; ", diagnostics)}"), exception);
        }
    }

    private static Task OpenPersonnelFromShellAsync(StandardCrudPageDriver driver) => driver.OpenOrganizationTabAsync("人员", "persons");

    private static Task OpenPositionManagementFromShellAsync(StandardCrudPageDriver driver) => driver.OpenOrganizationTabAsync("岗位", "positions");

    // 同一浏览器主旅程从组织结构树工具栏进入公司标准 Drawer，建立后续部门与人员归属的真实前置资源。
    private async Task<CompanyData> CreateCompanyFromBrowserAsync(
        StandardCrudPageDriver driver,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await driver.OpenOrganizationTabAsync("组织结构", "structure");
        var page = driver.Page;
        var tree = page.GetByRole(AriaRole.Complementary, new PageGetByRoleOptions { Name = "结构树" });
        var company = new CompanyData(
            $"ACC-BC-{_runId[..8]}",
            $"Personnel Gate Browser Company {_runId[..8]}");
        var drawer = await driver.OpenCreateDrawerAsync("新建公司");
        await StandardCrudPageDriver.FillDrawerFieldAsync(drawer, "公司编码", company.Code);
        await StandardCrudPageDriver.FillDrawerFieldAsync(drawer, "公司名称", company.Name);
        await StandardCrudPageDriver.FillDrawerFieldAsync(drawer, "排序码", "0100");
        await driver.SaveAndCloseAsync(
            drawer,
            new ApiExpectation("POST", "/api/organization/companies", (int)HttpStatusCode.Created, "company create"));
        await tree.GetByText(company.Name, new LocatorGetByTextOptions { Exact = true })
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await driver.OpenOrganizationTabAsync("公司", "companies");
        await driver.WaitForRowAsync(company.Code, company.Name);
        await OpenAndCloseDirectoryDetailAsync(driver, company.Name);
        return company;
    }

    // 公司已有真实部门和人员后，用LocalAdmin走同一编辑入口完成停用和恢复；下级状态与会话语义留在SQL测试。
    private async Task DisableAndRestoreCompanyFromBrowserAsync(StandardCrudPageDriver driver, CompanyData company,
        CancellationToken cancellationToken)
    {
        await driver.OpenOrganizationTabAsync("公司", "companies");
        foreach (var enabled in new[] { false, true })
        {
            cancellationToken.ThrowIfCancellationRequested();
            var row = await driver.WaitForRowAsync(company.Code, company.Name);
            await row.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "编辑", Exact = true }).ClickAsync();
            var drawer = await driver.WaitForVisibleDrawerAsync("company enablement editor");
            await StandardCrudPageDriver.SetDrawerSwitchAsync(drawer, "启用状态", enabled);
            await driver.SaveChangesAsync(drawer,
                new ApiExpectation("PUT", "/api/organization/companies/", 200,
                    enabled ? "company restore" : "company disable", PathIsPrefix: true));
            await driver.WaitForRowAsync(company.Code, company.Name);
        }
        await OpenAndCloseDirectoryDetailAsync(driver, company.Name);
    }

    // 在真实组织树直属列表创建部门，沿自动选择的节点右键编辑，再消费更新后的结果。
    private async Task<DepartmentData> CreateDepartmentFromBrowserAsync(
        StandardCrudPageDriver driver,
        CompanyData company,
        CancellationToken cancellationToken,
        string suffix = "A")
    {
        cancellationToken.ThrowIfCancellationRequested();
        var page = driver.Page;
        await driver.OpenOrganizationTabAsync("组织结构", "structure");
        var tree = page.GetByRole(AriaRole.Complementary, new PageGetByRoleOptions { Name = "结构树" });
        await tree.GetByText(company.Name, new LocatorGetByTextOptions { Exact = true }).ClickAsync();
        var content = page.GetByRole(AriaRole.Region, new PageGetByRoleOptions { Name = "节点内容" });
        await content.GetByRole(AriaRole.Tab, new LocatorGetByRoleOptions { Name = "直属部门", Exact = true }).ClickAsync();
        var department = new DepartmentData(
            $"ACC-BD-{suffix}-{_runId[..8]}",
            $"Personnel Gate Browser Department {suffix} {_runId[..8]}");
        var drawer = await driver.OpenCreateDrawerAsync("新建部门");
        await StandardCrudPageDriver.FillDrawerFieldAsync(drawer, "部门编码", department.Code);
        await StandardCrudPageDriver.FillDrawerFieldAsync(drawer, "部门名称", department.Name);
        await StandardCrudPageDriver.FillDrawerFieldAsync(drawer, "排序码", "0100");
        await driver.SaveAndCloseAsync(
            drawer,
            new ApiExpectation("POST", "/api/organization/departments", (int)HttpStatusCode.Created, "department create"));
        var node = tree.GetByText(department.Name, new LocatorGetByTextOptions { Exact = true });
        await node.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await content.GetByRole(AriaRole.Tab, new LocatorGetByRoleOptions { Name = "基本信息", Selected = true })
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await content.GetByText(department.Name, new LocatorGetByTextOptions { Exact = true }).First
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await node.ClickAsync(new LocatorClickOptions { Button = MouseButton.Right });
        await page.GetByRole(AriaRole.Menuitem, new PageGetByRoleOptions { Name = "编辑", Exact = true }).ClickAsync();
        drawer = driver.VisibleDrawer();
        var updated = department with { Name = department.Name + " Updated" };
        await StandardCrudPageDriver.FillDrawerFieldAsync(drawer, "部门名称", updated.Name);
        await driver.SaveChangesAsync(
            drawer,
            new ApiExpectation("PUT", "/api/organization/departments/", (int)HttpStatusCode.OK, "tree department edit", PathIsPrefix: true));
        await tree.GetByText(updated.Name, new LocatorGetByTextOptions { Exact = true })
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await content.GetByText(updated.Name, new LocatorGetByTextOptions { Exact = true }).First
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        return updated;
    }

    // 沿当前部门创建临时子部门，在非活动树之外换父，再回树删除，复用同一人员主旅程。
    private async Task MaintainDepartmentFromBrowserAsync(StandardCrudPageDriver driver, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var page = driver.Page;
        var name = $"Personnel Gate Moved Department {_runId[..8]}";
        var tree = page.GetByRole(AriaRole.Complementary, new PageGetByRoleOptions { Name = "结构树" });
        var content = page.GetByRole(AriaRole.Region, new PageGetByRoleOptions { Name = "节点内容" });
        await content.GetByRole(AriaRole.Tab, new LocatorGetByRoleOptions { Name = "直属子部门", Exact = true }).ClickAsync();
        await driver.ClickPageActionAsync("新建部门");
        var drawer = driver.VisibleDrawer();
        await StandardCrudPageDriver.FillDrawerFieldAsync(drawer, "部门编码", $"ACC-M-{_runId[..8]}");
        await StandardCrudPageDriver.FillDrawerFieldAsync(drawer, "部门名称", name);
        await StandardCrudPageDriver.FillDrawerFieldAsync(drawer, "排序码", "0200");
        await driver.SaveAndCloseAsync(
            drawer,
            new ApiExpectation("POST", "/api/organization/departments", 201, "temporary department create"));
        await tree.GetByText(name, new LocatorGetByTextOptions { Exact = true })
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        // 直接切外层标签保留已打开的树实例，不能点击菜单重新装载来掩盖延迟刷新缺陷。
        await page.GetByRole(AriaRole.Tab, new PageGetByRoleOptions { Name = "部门", Exact = true }).ClickAsync();
        var row = driver.VisibleRow(name);
        await row.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "编辑", Exact = true }).ClickAsync();
        drawer = driver.VisibleDrawer();
        var parentField = drawer.Locator(".ant-form-item:has(#ParentDepartmentId)");
        await parentField.HoverAsync();
        await parentField.Locator(".ant-select-clear").ClickAsync();
        await driver.SaveChangesAsync(
            drawer,
            new ApiExpectation("PUT", "/api/organization/departments/", 200, "temporary department move", PathIsPrefix: true));
        await page.GetByRole(AriaRole.Tab, new PageGetByRoleOptions { Name = "组织结构", Exact = true }).ClickAsync();
        await tree.GetByText(name, new LocatorGetByTextOptions { Exact = true }).ClickAsync(new LocatorClickOptions { Button = MouseButton.Right });
        await page.GetByRole(AriaRole.Menuitem, new PageGetByRoleOptions { Name = "删除", Exact = true }).ClickAsync();
        var confirm = driver.ConfirmDialogWithText("确认删除该部门吗？");
        await driver.ConfirmAsync(
            confirm,
            new ApiExpectation("DELETE", "/api/organization/departments/", null, "temporary department delete", PathIsPrefix: true));
        await tree.GetByText(name, new LocatorGetByTextOptions { Exact = true })
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden });
    }

    // 在现有组织维护旅程内搜索尚未展开的部门，并通过当前地址恢复可继续操作的节点内容。
    private async Task LocateDepartmentAndRestoreAddressAsync(StandardCrudPageDriver driver, DepartmentData department, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var page = driver.Page;
        // 正式菜单地址重新进入树，避免只在已加载分支中命中结果。
        await page.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = StandardCrudPageDriver.OrganizationManagementTitle, Exact = true }).First.ClickAsync();
        await page.ReloadAsync();
        var tree = page.GetByRole(AriaRole.Complementary, new PageGetByRoleOptions { Name = "结构树" });
        await tree.GetByLabel("搜索节点", new LocatorGetByLabelOptions { Exact = true }).FillAsync(department.Name);
        await tree.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = $"定位节点 {department.Name}", Exact = true }).ClickAsync();
        var content = page.GetByRole(AriaRole.Region, new PageGetByRoleOptions { Name = "节点内容" });
        await content.GetByText(department.Name, new LocatorGetByTextOptions { Exact = true }).First
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await content.GetByRole(AriaRole.Tab, new LocatorGetByRoleOptions { Name = "直属子部门", Exact = true }).ClickAsync();
        var address = page.Url;
        await page.ReloadAsync();
        await page.GetByRole(AriaRole.Region, new PageGetByRoleOptions { Name = "节点内容" })
            .GetByRole(AriaRole.Tab, new LocatorGetByRoleOptions { Name = "直属子部门", Selected = true })
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await tree.GetByText(department.Name, new LocatorGetByTextOptions { Exact = true })
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        var sharedPage = await page.Context.NewPageAsync();
        try
        {
            await sharedPage.GotoAsync(address);
            await sharedPage.GetByRole(AriaRole.Region, new PageGetByRoleOptions { Name = "节点内容" })
                .GetByRole(AriaRole.Tab, new LocatorGetByRoleOptions { Name = "直属子部门", Selected = true })
                .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
            await sharedPage.GetByRole(AriaRole.Complementary, new PageGetByRoleOptions { Name = "结构树" })
                .GetByText(department.Name, new LocatorGetByTextOptions { Exact = true })
                .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        }
        finally
        {
            await sharedPage.CloseAsync();
        }
    }

    // 同一 Personnel 浏览器场景通过标准 Drawer 创建岗位，不把字段边界语义提升到浏览器层。
    private async Task<PositionData> CreatePositionFromBrowserAsync(
        StandardCrudPageDriver driver,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await OpenPositionManagementFromShellAsync(driver);
        var name = $"Personnel Gate Position {_runId[..8]}";
        var description = "Personnel Gate browser journey";
        var drawer = await driver.OpenCreateDrawerAsync("新建岗位");
        await StandardCrudPageDriver.FillDrawerFieldAsync(drawer, "岗位名称", name);
        await StandardCrudPageDriver.FillDrawerFieldAsync(drawer, "岗位描述", description);
        await driver.SaveAndCloseAsync(
            drawer,
            new ApiExpectation("POST", "/api/organization/positions", (int)HttpStatusCode.Created, "position create"));
        await driver.WaitForRowAsync(name, description);
        await OpenAndCloseDirectoryDetailAsync(driver, name);
        return new PositionData(name);
    }

    // 沿已创建对象的同一旅程打开只读详情；字段语义与审计排序由下层测试验证。
    private static async Task OpenAndCloseDirectoryDetailAsync(StandardCrudPageDriver driver, string name)
    {
        var row = driver.VisibleRow(name);
        await row.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = name, Exact = true })
            .ClickAsync();
        var drawer = driver.VisibleDrawer();
        await drawer.GetByText(name, new LocatorGetByTextOptions { Exact = true })
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await driver.CloseDrawerAsync(drawer);
    }

    // 角色和权限集合均通过正式管理页面维护，记录值与事务语义留在 SQL/API 层。
    private async Task<string> CreateRoleFromBrowserAndConfigureUsingApiAsync(StandardCrudPageDriver driver, string suffix, bool maintenance, CancellationToken cancellationToken)
    {
        var page = driver.Page;
        await page.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "角色管理", Exact = true }).First.ClickAsync();
        await page.WaitForURLAsync($"http://127.0.0.1:{_shellPort}/authorization/roles");
        await driver.WaitForTableReadyAsync();
        var code = $"PersonnelGateRole{suffix}{_runId[..8]}";
        await driver.ClickPageActionAsync("新建角色");
        var drawer = driver.VisibleDrawer();
        await StandardCrudPageDriver.FillDrawerFieldAsync(drawer, "角色代码", code);
        await StandardCrudPageDriver.FillDrawerFieldAsync(drawer, "角色名称", $"Personnel Gate Role {suffix} {_runId[..8]}");
        await StandardCrudPageDriver.FillDrawerFieldAsync(drawer, "说明", "Personnel acceptance role configuration");
        var created = await driver.SaveAndCloseAsync(
            drawer,
            new ApiExpectation("POST", "/api/authorization/roles", 201, "role creation"));
        using var payload = JsonDocument.Parse(await created.TextAsync());
        var id = payload.RootElement.GetProperty("id").GetString() ?? throw new InvalidOperationException("Role creation omitted id.");
        using var administrator = CreateClient(_hostPort);
        await LoginAdminAsync(administrator, cancellationToken);
        var features = _features!;
        var entries = new[] { "structure", "companies", "departments", "positions", "persons" }.Select(features.OrganizationTab).ToList();
        if (maintenance)
        {
            var persons = features.OrganizationTab("persons");
            entries.Add(features.Action(persons, "create", "list.pageActions"));
            entries.Add(features.Action(persons, "edit", "list.rowActions"));
        }
        await SetRoleFeaturesUsingApiAsync(administrator, id, features.GrantSet(entries.ToArray()), cancellationToken);
        return id;
    }

    // 账号选择和角色集合保存使用授权模块的受控动作，不直接写关系表。
    private async Task AssignAccountRoleFromBrowserAsync(StandardCrudPageDriver driver, string roleId, string suffix, OrdinaryActor actor)
    {
        var page = driver.Page;
        await page.GotoAsync($"http://127.0.0.1:{_shellPort}/authorization/roles/{roleId}");
        var accountRoles = page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "账号角色", Exact = true });
        await accountRoles.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        var roleScreenshot = Path.Combine(_runDirectory, "personnel", $"role-detail-{suffix}.png");
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = roleScreenshot, FullPage = true });
        _artifactRefs.Add(AcceptanceInfrastructure.RelativeArtifact(_runDirectory, roleScreenshot));
        await accountRoles.ClickAsync();
        var modal = driver.VisibleModal();
        var account = modal.GetByRole(AriaRole.Combobox, new LocatorGetByRoleOptions { Name = "账号", Exact = true });
        await account.FillAsync(actor.ECode);
        await page.Locator(".ant-select-dropdown:visible").Last.GetByText(
            $"Personnel {(suffix == "M" ? "writer" : "reader")} {_runId[..8]} ({actor.ECode})",
            new LocatorGetByTextOptions { Exact = true }).ClickAsync();
        var role = modal.GetByRole(AriaRole.Checkbox, new LocatorGetByRoleOptions
        {
            Name = $"Personnel Gate Role {suffix} {_runId[..8]} (PersonnelGateRole{suffix}{_runId[..8]})", Exact = true
        });
        await SelectControlledCheckboxAsync(role);
        await driver.ConfirmAsync(
            modal,
            new ApiExpectation("PUT", $"/api/authorization/users/{actor.Id}/roles", 204, "account roles"));
    }

    // Checkbox.Group 在 effect 中同步受控值，等待实际状态而非点击后的同步快照。
    private static async Task SelectControlledCheckboxAsync(ILocator checkbox)
    {
        if (!await checkbox.IsCheckedAsync()) await checkbox.ClickAsync();
        await Assertions.Expect(checkbox).ToBeCheckedAsync();
    }

    // 同一条旅程从统一入口读取已提交事件；字段差异的语义由 SQL/API 与呈现测试验证。
    private async Task OpenAuditDetailFromBrowserAsync(StandardCrudPageDriver driver, string targetId, string eventType, string phase)
    {
        var page = driver.Page;
        await page.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "业务变更记录", Exact = true }).First.ClickAsync();
        await page.WaitForURLAsync($"http://127.0.0.1:{_shellPort}/audit/business-changes");
        await driver.WaitForTableReadyAsync();
        await page.GetByLabel("目标标识", new PageGetByLabelOptions { Exact = true }).FillAsync(targetId);
        await page.GetByLabel("事件类型", new PageGetByLabelOptions { Exact = true }).FillAsync(eventType);
        var queryResponse = page.WaitForResponseAsync(response =>
            response.Request.Method == "GET"
            && new Uri(response.Url).AbsolutePath == "/api/audit/business-changes"
            && StandardCrudPageDriver.HasQueryValue(new Uri(response.Url), "EventType", eventType));
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "搜索", Exact = true }).ClickAsync();
        if (!(await queryResponse).Ok) throw new InvalidOperationException("Business change lookup failed.");
        var auditRow = page.Locator(".ant-table-tbody tr.ant-table-row").First;
        await auditRow.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await auditRow.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = eventType, Exact = true }).ClickAsync();
        await driver.VisibleDrawer().GetByRole(AriaRole.Button,
            new LocatorGetByRoleOptions { Name = "查看完整详情", Exact = true }).ClickAsync();
        await page.WaitForURLAsync(url => new Uri(url).AbsolutePath.StartsWith("/audit/business-changes/", StringComparison.Ordinal));
        await page.GetByText("变化明细", new PageGetByTextOptions { Exact = true }).First
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        var auditScreenshot = Path.Combine(_runDirectory, "personnel", $"audit-detail-{eventType}-{phase}.png");
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = auditScreenshot, FullPage = true });
        _artifactRefs.Add(AcceptanceInfrastructure.RelativeArtifact(_runDirectory, auditScreenshot));
    }

    private Task LoginBrowserActorAsync(IPage page, OrdinaryActor actor) =>
        new ShellPageDriver(page, $"http://127.0.0.1:{_shellPort}").LoginAsync(actor.ECode, actor.Password);

    // 维护者使用正式创建表单，人员与登录账号保持不同对象。
    private async Task<PersonData> CreatePersonFromBrowserAsync(StandardCrudPageDriver driver, CompanyData company, DepartmentData department)
    {
        var page = driver.Page;
        await OpenPersonnelFromShellAsync(driver);
        await driver.ClickPageActionAsync("新建人员");
        var drawer = driver.VisibleDrawer();
        var number = $"ACT-{_runId[..10]}";
        var name = $"Maintainer Created Person {_runId[..8]}";
        await StandardCrudPageDriver.FillDrawerFieldAsync(drawer, "人员编号", number);
        await StandardCrudPageDriver.FillDrawerFieldAsync(drawer, "姓名", name);
        await SelectOrganizationLookupAsync(page, drawer, "所属公司", company.Name, "/api/organization/person-lookups/companies");
        await SelectOrganizationLookupAsync(page, drawer, "所属部门", department.Name, "/api/organization/person-lookups/departments");
        var created = await driver.SaveAndCloseAsync(
            drawer,
            new ApiExpectation("POST", "/api/organization/persons", 201, "maintainer create"));
        return ReadPerson(await created.TextAsync(), number, name);
    }

    private static async Task<ILocator> OpenPersonEditDrawerAsync(StandardCrudPageDriver driver, PersonData person)
    {
        await OpenPersonnelFromShellAsync(driver);
        await SubmitPersonnelKeywordAsync(driver.Page, person.PersonNo);
        var row = await driver.WaitForRowAsync(person.PersonNo, person.Name);
        await row.GetByRole(
                AriaRole.Button,
                new LocatorGetByRoleOptions { Name = "编辑", Exact = true })
            .ClickAsync();
        return await driver.WaitForVisibleDrawerAsync("person edit drawer");
    }

    // 只撤销本旅程实际使用的人员列表编辑位置，保留树和其他入口的独立授权。
    private async Task RevokeUpdateUsingApiAsync(string roleId, CancellationToken cancellationToken)
    {
        using var administrator = CreateClient(_hostPort);
        await LoginAdminAsync(administrator, cancellationToken);
        var features = _features!;
        var edit = features.Action(features.OrganizationTab("persons"), "edit", "list.rowActions").Position;
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/authorization/workbench/roles/{roleId}");
        request.Headers.Add("X-NewP-Feature", features.Behavior("Authorization", "authorization.workbench.read").Position);
        using var response = await administrator.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        EnsureStatus(response, HttpStatusCode.OK, body, "read role features before revocation");
        using var payload = JsonDocument.Parse(body);
        var positions = payload.RootElement.GetProperty("positions").EnumerateArray()
            .Select(value => value.GetString() ?? throw new InvalidOperationException("Role feature position was null.")).ToArray();
        if (!positions.Contains(edit, StringComparer.Ordinal))
            throw new InvalidOperationException("Revocation prerequisite did not grant the personnel list edit position.");
        await SetRoleFeaturesUsingApiAsync(administrator, roleId,
            positions.Where(position => position != edit).ToArray(), cancellationToken);
    }

    private async Task SetRoleFeaturesUsingApiAsync(HttpClient administrator, string roleId, string[] positions, CancellationToken cancellationToken)
    {
        using var response = await SendJsonWithAntiforgeryAsync(administrator, HttpMethod.Put,
            $"/api/authorization/workbench/roles/{roleId}", new { positions }, cancellationToken,
            request => request.Headers.Add("X-NewP-Feature", _features!.Behavior("Authorization", "authorization.workbench.save").Position));
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        EnsureStatus(response, HttpStatusCode.NoContent, body, "configure Personnel prerequisite features");
    }

    // 一次标准编辑同时维护岗位与时间字段，避免把同一主旅程拆成两次独立提交。
    private async Task<PersonData> UpdatePersonFromBrowserAsync(
        StandardCrudPageDriver driver,
        PersonData person,
        CompanyData company,
        DepartmentData department,
        PositionData position,
        CancellationToken cancellationToken,
        bool departmentOnly = false,
        bool sameCompany = false)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var page = driver.Page;
        var drawer = await OpenPersonEditDrawerAsync(driver, person);
        if (!departmentOnly && !sameCompany)
        {
            await SelectOrganizationLookupAsync(
                page,
                drawer,
                "所属公司",
                company.Name,
                "/api/organization/person-lookups/companies");
        }
        await SelectOrganizationLookupAsync(
            page,
            drawer,
            "所属部门",
            department.Name,
            "/api/organization/person-lookups/departments");
        if (!departmentOnly)
        {
            var positionLookup = drawer.GetByLabel(
                "岗位",
                new LocatorGetByLabelOptions { Exact = true });
            await positionLookup.ClickAsync();
            var lookupResponseTask = page.WaitForResponseAsync(response =>
            {
                if (response.Request.Method != "GET")
                {
                    return false;
                }

                var uri = new Uri(response.Url);
                return uri.AbsolutePath.Equals("/api/organization/positions/options", StringComparison.Ordinal)
                       && StandardCrudPageDriver.HasQueryValue(uri, "keyword", position.Name);
            });
            await positionLookup.FillAsync(position.Name);
            var lookupResponse = await lookupResponseTask;
            if (lookupResponse.Status != (int)HttpStatusCode.OK)
            {
                throw new InvalidOperationException(
                    $"Browser position lookup returned {lookupResponse.Status}, expected 200.");
            }
            await page.Locator(".ant-select-dropdown:visible").Last
                .GetByText(position.Name, new LocatorGetByTextOptions { Exact = true })
                .ClickAsync();

            foreach (var (label, value) in PersonnelTemporalFormInputs)
            {
                var input = drawer.GetByLabel(label, new LocatorGetByLabelOptions { Exact = true });
                await input.FillAsync(value);
                await input.PressAsync(PersonnelTemporalInputExitKey);
            }
        }

        var personApiPath = $"/api/organization/persons/{Uri.EscapeDataString(person.Id)}";
        var response = await driver.SaveChangesAsync(
            drawer,
            new ApiExpectation("PUT", personApiPath, (int)HttpStatusCode.OK, "person update"));
        var updated = ReadPerson(await response.TextAsync(), person.PersonNo, person.Name);
        await driver.WaitForRowAsync(person.PersonNo, position.Name);
        return updated;
    }

    // Organization 私有分页 Lookup 必须在正式表单中完成搜索与选择，不能由 fixture 注入原始标识。
    private static async Task SelectOrganizationLookupAsync(
        IPage page,
        ILocator drawer,
        string label,
        string option,
        string apiPath)
    {
        var lookup = drawer.GetByLabel(label, new LocatorGetByLabelOptions { Exact = true });
        var responseTask = page.WaitForResponseAsync(response =>
        {
            if (response.Request.Method != "GET")
            {
                return false;
            }

            var uri = new Uri(response.Url);
            return uri.AbsolutePath.Equals(apiPath, StringComparison.Ordinal)
                   && StandardCrudPageDriver.HasQueryValue(uri, "keyword", option);
        });
        await lookup.FillAsync(option);
        var response = await responseTask;
        if (response.Status != (int)HttpStatusCode.OK)
        {
            throw new InvalidOperationException(
                $"Browser {label} lookup returned {response.Status}, expected 200.");
        }

        await page.Locator(".ant-select-dropdown:visible").Last
            .GetByText(option, new LocatorGetByTextOptions { Exact = true })
            .ClickAsync();
    }

    // 高级筛选只观察应用后仍能找到目标；内嵌页面不向外层 URL 写入列表筛选。
    private static async Task AssertPersonBirthDateRangeAsync(StandardCrudPageDriver driver, PersonData person)
    {
        var page = driver.Page;
        await page.GetByRole(
                AriaRole.Button,
                new PageGetByRoleOptions { Name = "高级筛选", Exact = true })
            .ClickAsync();
        var drawer = await driver.WaitForVisibleDrawerAsync("advanced filter drawer");
        var rangeInputs = drawer.Locator(".ant-picker-range input");
        if (await rangeInputs.CountAsync() != 2)
        {
            throw new InvalidOperationException("Personnel advanced filter did not expose one CalendarDate range control.");
        }

        await rangeInputs.Nth(0).FillAsync(PersonnelBirthDate);
        await rangeInputs.Nth(0).PressAsync("Enter");
        await rangeInputs.Nth(1).FillAsync(PersonnelBirthDate);
        await rangeInputs.Nth(1).PressAsync("Enter");
        var responseTask = page.WaitForResponseAsync(response =>
        {
            if (response.Request.Method != "GET")
            {
                return false;
            }

            var uri = new Uri(response.Url);
            return uri.AbsolutePath.Equals("/api/organization/persons", StringComparison.Ordinal)
                   && StandardCrudPageDriver.HasQueryValue(uri, "birthDateFrom", PersonnelBirthDate)
                   && StandardCrudPageDriver.HasQueryValue(uri, "birthDateTo", PersonnelBirthDate);
        });
        await drawer.GetByRole(
                AriaRole.Button,
                new LocatorGetByRoleOptions { Name = "应用筛选", Exact = true })
            .ClickAsync();
        var response = await responseTask;
        if (response.Status != (int)HttpStatusCode.OK)
        {
            throw new InvalidOperationException($"Browser person birth date range returned {response.Status}, expected 200.");
        }

        await driver.WaitForRowAsync(person.PersonNo, person.Name);
    }

    // 列表与查看 Drawer 只证明更新后的人员能够从正式用户入口重新读到。
    private static async Task AssertPersonListAndDrawerAsync(
        StandardCrudPageDriver driver,
        PersonData person,
        string? companyName,
        string? departmentName,
        string? positionName)
    {
        await SubmitPersonnelKeywordAsync(driver.Page, person.PersonNo);
        var row = driver.Page.Locator("tr:visible", new PageLocatorOptions { HasTextString = person.PersonNo })
            .Filter(new LocatorFilterOptions { HasTextString = person.Name });
        if (!string.IsNullOrWhiteSpace(positionName))
        {
            row = row.Filter(new LocatorFilterOptions { HasTextString = positionName });
        }
        if (!string.IsNullOrWhiteSpace(companyName))
        {
            row = row.Filter(new LocatorFilterOptions { HasTextString = companyName });
        }
        if (!string.IsNullOrWhiteSpace(departmentName))
        {
            row = row.Filter(new LocatorFilterOptions { HasTextString = departmentName });
        }

        row = row.Last;
        await row.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await row.GetByRole(
                AriaRole.Button,
                new LocatorGetByRoleOptions { Name = person.Name, Exact = true })
            .ClickAsync();

        var drawer = await driver.WaitForVisibleDrawerAsync("person view drawer");
        await drawer.GetByText(person.PersonNo, new LocatorGetByTextOptions { Exact = true }).First
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await drawer.GetByText(person.Name, new LocatorGetByTextOptions { Exact = true }).First
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        if (!string.IsNullOrWhiteSpace(positionName))
        {
            await drawer.GetByText(positionName, new LocatorGetByTextOptions { Exact = true }).First
                .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        }
        if (!string.IsNullOrWhiteSpace(companyName))
        {
            await drawer.GetByText(companyName, new LocatorGetByTextOptions { Exact = true }).First
                .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        }
        if (!string.IsNullOrWhiteSpace(departmentName))
        {
            await drawer.GetByText(departmentName, new LocatorGetByTextOptions { Exact = true }).First
                .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        }
    }

    private static async Task SubmitPersonnelKeywordAsync(IPage page, string keywordValue)
    {
        var keyword = page.GetByLabel("关键字", new PageGetByLabelOptions { Exact = true }).And(page.Locator(":visible"));
        await keyword.FillAsync(keywordValue);
        await keyword.PressAsync("Enter");

    }

    private async Task RunRestartScenarioAsync(CancellationToken cancellationToken)
    {
        await RunDevScriptAsync("stop-dev.ps1", "personnel/restart-stop.json", cancellationToken);
        await StartDevAsync("personnel/restart-dev-start.json", cancellationToken);

        // 重启后的浏览器使用全新 context 重新登录，避免旧 cookie 把持久化与恢复缺陷遮住。
        await RunBrowserScenarioAsync("restart", cancellationToken);
    }

    protected override async Task<AcceptanceCleanupResult> TeardownAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (_catalogCreated)
            {
                await VerifyOwnershipAsync(cancellationToken);
            }
        }
        catch (Exception exception)
        {
            // 归属校验失败时保留资源，交由人工确认这套库/目录到底属于谁。
            return new AcceptanceCleanupResult(
                "failed",
                AcceptanceFailureCodes.OwnershipMismatch,
                $"一次性资源归属校验失败，资源已保留：{Sanitize(exception.Message)}");
        }

        try
        {
            await RunDevScriptAsync("stop-dev.ps1", "personnel/teardown-stop.json", cancellationToken, allowAlreadyStopped: true);
            if (_catalogCreated)
            {
                await DropCatalogAsync(cancellationToken);
                _catalogCreated = false;
            }
            foreach (var root in new[] { _pluginRoot, _keyRingRoot })
            {
                if (root is not null && Directory.Exists(root))
                {
                    EnsureSafeResourceRoot(root);
                    Directory.Delete(root, recursive: true);
                }
            }

            return new AcceptanceCleanupResult("completed", null);
        }
        catch (Exception exception)
        {
            return new AcceptanceCleanupResult(
                "failed",
                AcceptanceFailureCodes.CleanupFailed,
                Sanitize(exception.Message));
        }
    }

    private async Task VerifyOwnershipAsync(CancellationToken cancellationToken)
    {
        if (_catalog is null || _applicationConnectionString is null || _pluginRoot is null || _keyRingRoot is null)
        {
            throw new InvalidOperationException("Acceptance resources were not fully identified.");
        }
        ValidateCatalogName(_catalog);
        EnsureSafeResourceRoot(_pluginRoot);
        EnsureSafeResourceRoot(_keyRingRoot);
        if (!Directory.Exists(_pluginRoot) || !Directory.Exists(_keyRingRoot))
        {
            throw new InvalidOperationException("Acceptance owned resource root disappeared before teardown.");
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

    // 场景通过时留一份可读的现场快照；它是排错用的转储，不再是裁决输入。
    private Task RecordScenarioSuccessAsync(
        string scenario,
        string evidencePath,
        object evidence,
        CancellationToken cancellationToken)
    {
        _ = scenario;
        return WriteEvidenceAsync(evidencePath, evidence, cancellationToken);
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

    private async Task ObserveBootstrapRequiredDevAsync(CancellationToken cancellationToken)
    {
        var arguments = new List<string>
        {
            "-NoLogo",
            "-NoProfile",
            "-File",
            Path.Combine(_repositoryRoot, "scripts", "dev.ps1"),
            "-HostStartupTimeoutSeconds",
            "600",
            "-ShellStartupTimeoutSeconds",
            "180",
            "-ShellRequestTimeoutSeconds",
            "60",
            "-HostEnvironmentName",
            "Acceptance",
            "-SkipDependencyInstall"
        };
        var result = await AcceptanceInfrastructure.RunProcessAsync(
            "pwsh.exe",
            arguments,
            _repositoryRoot,
            _devEnvironment,
            redirectOutput: false,
            cancellationToken);
        CaptureFailedDevRunIdentity();
        var resultPath = Path.Combine(_repositoryRoot, ".codex-temp", "dev", "current-result.json");
        if (!File.Exists(resultPath))
        {
            throw new InvalidOperationException("Failed bootstrap-required startup did not write current-result.json.");
        }

        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(resultPath, cancellationToken));
        var status = ReadString(json.RootElement, "status");
        var failureCode = ReadString(json.RootElement, "failureCode");
        var diagnostic = ReadString(json.RootElement, "diagnostic");
        if (result.ExitCode == 0
            || !string.Equals(status, "failed", StringComparison.Ordinal)
            || !string.Equals(failureCode, "DEV_HOST_READINESS_FAILED", StringComparison.Ordinal)
            || !diagnostic.Contains("IDENTITY_BOOTSTRAP_REQUIRED", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Empty Personnel catalog startup returned exit={result.ExitCode}, status={status}, failure={failureCode}; diagnostic={Sanitize(diagnostic)}; devRun={_lastDevRunId}.");
        }

        // 只有预期启动阶段已经完成持久化初始化，才读取其 ownership；提前退出应保留 supervisor 首因。
        _ownershipSnapshot = await ReadOwnershipSnapshotAsync(cancellationToken);

        await WriteEvidenceAsync("personnel/bootstrap-required.json", new
        {
            status = 503,
            failureCode = "IDENTITY_BOOTSTRAP_REQUIRED",
            supervisorFailureCode = failureCode,
            endpoint = "/api/health/ready"
        }, cancellationToken);
        await RunDevScriptAsync(
            "stop-dev.ps1",
            "personnel/bootstrap-required-stop.json",
            cancellationToken,
            allowAlreadyStopped: true);
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
        using var user = await LoginAsync(
            client,
            "admin",
            _adminPassword ?? throw new InvalidOperationException("Administrator password was not prepared."),
            cancellationToken);
        _features ??= await AcceptanceFeatureCatalog.LoadAsync(client, cancellationToken);
        EnsurePersonnelJourneyFeatures(user.RootElement);
    }

    // 浏览器动作资格来自同一认证快照；按正式位置在 API 层先给出缺失授权的精确信号。
    private void EnsurePersonnelJourneyFeatures(JsonElement user)
    {
        if (!user.TryGetProperty("featurePositions", out var permissions)
            || permissions.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("Administrator authentication response did not contain feature positions.");
        }

        var granted = permissions.EnumerateArray()
            .Where(static permission => permission.ValueKind == JsonValueKind.String)
            .Select(static permission => permission.GetString())
            .Where(static permission => !string.IsNullOrWhiteSpace(permission))
            .ToHashSet(StringComparer.Ordinal);
        var required = new[]
        {
            _features!.Action(_features.OrganizationTab("structure"), "createCompany", "treeActions").Position,
            _features.Action(_features.OrganizationTab("departments"), "create", "list.pageActions").Position,
            _features.Action(_features.OrganizationTab("persons"), "edit", "list.rowActions").Position
        };
        var missing = required.Where(permission => !granted.Contains(permission)).ToArray();
        if (missing.Length > 0)
        {
            throw new InvalidOperationException(
                $"Administrator authentication response is missing Personnel journey positions: {string.Join(", ", missing)}.");
        }
    }

    private static async Task<JsonDocument> LoginAsync(
        HttpClient client,
        string ecode,
        string password,
        CancellationToken cancellationToken)
    {
        using var response = await SendJsonWithAntiforgeryAsync(
            client,
            HttpMethod.Post,
            "/api/auth/login",
            new { ecode, password },
            cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        EnsureStatus(response, HttpStatusCode.OK, body, "identity login");
        CaptureAuthenticationCookie(client, response);
        return JsonDocument.Parse(body);
    }

    private static async Task<long> ChangePasswordAsync(
        HttpClient client,
        string currentPassword,
        string newPassword,
        long expectedRevision,
        CancellationToken cancellationToken)
    {
        using var response = await SendJsonWithAntiforgeryAsync(
            client,
            HttpMethod.Post,
            "/api/identity/me/password-change",
            new { currentPassword, newPassword, expectedRevision },
            cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        EnsureStatus(response, HttpStatusCode.OK, body, "change identity password");
        CaptureAuthenticationCookie(client, response);
        using var result = JsonDocument.Parse(body);
        return ReadLong(ReadObject(result.RootElement, "account"), "revision");
    }

    private static void CaptureAuthenticationCookie(HttpClient client, HttpResponseMessage response)
    {
        var cookiePrefix = $"{AuthenticationCookieName}=";
        var cookie = response.Headers.TryGetValues("Set-Cookie", out var values)
            ? values.Select(static value => value.Split(';', 2)[0])
                .SingleOrDefault(value => value.StartsWith(cookiePrefix, StringComparison.Ordinal))
            : null;
        if (string.IsNullOrWhiteSpace(cookie))
        {
            throw new InvalidOperationException("Identity response did not issue the expected authentication cookie.");
        }

        // Acceptance 环境必须保持 Secure cookie；门禁的 loopback HTTP client 显式回放该一次性会话，
        // 不放宽 Host 的生产/验收 cookie 安全策略。
        client.DefaultRequestHeaders.Remove("Cookie");
        client.DefaultRequestHeaders.TryAddWithoutValidation("Cookie", cookie);
    }

    private static async Task<HttpResponseMessage> SendJsonWithAntiforgeryAsync(
        HttpClient client,
        HttpMethod method,
        string path,
        object payload,
        CancellationToken cancellationToken,
        Action<HttpRequestMessage>? configure = null)
    {
        using var tokenResponse = await client.GetAsync("/api/auth/antiforgery-token", cancellationToken);
        var tokenBody = await tokenResponse.Content.ReadAsStringAsync(cancellationToken);
        EnsureStatus(tokenResponse, HttpStatusCode.OK, tokenBody, "issue antiforgery token");
        using var tokenJson = JsonDocument.Parse(tokenBody);
        var requestToken = ReadString(tokenJson.RootElement, "requestToken");
        using var request = new HttpRequestMessage(method, path)
        {
            Content = JsonContent.Create(payload)
        };
        request.Headers.TryAddWithoutValidation("X-CSRF-TOKEN", requestToken);
        configure?.Invoke(request);
        return await client.SendAsync(request, cancellationToken);
    }

    private async Task<PersonData> CreatePersonAsync(
        HttpClient client,
        string personNo,
        string name,
        string? companyId,
        string? departmentId,
        CancellationToken cancellationToken)
    {
        var payload = PersonPayload(personNo, name, companyId, departmentId);
        using var response = await SendJsonWithAntiforgeryAsync(
            client,
            HttpMethod.Post,
            "/api/organization/persons",
            payload,
            cancellationToken,
            request => request.Headers.Add("X-NewP-Feature", _features!.Action(_features.OrganizationTab("persons"), "create", "list.pageActions").Position));
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        EnsureStatus(response, HttpStatusCode.Created, body, $"create person {personNo}");
        return ReadPerson(body, personNo, name);
    }

    private static PersonData ReadPerson(string body, string personNo, string name)
    {
        using var json = JsonDocument.Parse(body);
        var fields = ReadObject(json.RootElement, "fields");
        return new PersonData(ReadString(fields, "EID"), personNo, name);
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

    private static void EnsureStatus(HttpResponseMessage response, HttpStatusCode expected, string body, string operation)
    {
        if (response.StatusCode != expected)
        {
            throw new InvalidOperationException(
                $"{operation} returned {(int)response.StatusCode}, expected {(int)expected}: {body}");
        }
    }

    private static string ReadString(JsonElement element, string property) =>
        AcceptanceInfrastructure.ReadRequiredString(element, property);

    private static JsonElement ReadObject(JsonElement element, string property) =>
        AcceptanceInfrastructure.ReadRequiredObject(element, property);

    private static long ReadLong(JsonElement element, string property) =>
        AcceptanceInfrastructure.ReadRequiredInt64(element, property);

    private void ValidateCatalogName(string catalog)
    {
        const string pattern = "^NewP_acceptance_[a-f0-9]{24}$";
        if (!Regex.IsMatch(catalog, pattern, RegexOptions.CultureInvariant))
        {
            throw new InvalidOperationException("Refusing to operate on a catalog outside the Personnel acceptance naming boundary.");
        }
    }

    // teardown 只接受当前 run/resources 下预先声明的 plugins 或 key-ring 根，并拒绝任何 reparse point。
    private void EnsureSafeResourceRoot(string resourceRoot)
    {
        var fullPath = Path.GetFullPath(resourceRoot);
        var allowed = new[]
        {
            Path.GetFullPath(Path.Combine(_runDirectory, "resources", "plugins")),
            Path.GetFullPath(Path.Combine(_runDirectory, "resources", "key-ring"))
        };
        // Personnel 只删这两个预声明的根，比共享的前缀边界更严，该额外强度保留在本地。
        if (!allowed.Contains(fullPath, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Acceptance resource root escaped its owned run directory.");
        }

        AcceptanceInfrastructure.EnsureOwnedResourceRoot(_runDirectory, resourceRoot);
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

    private sealed record FixtureData(
        string RunId,
        string AdminUserId,
        string CompanyId,
        string CompanyName,
        string DepartmentId,
        string DepartmentName);
    private sealed record PersonData(
        string Id,
        string PersonNo,
        string Name);
    private sealed record CompanyData(string Code, string Name);
    private sealed record DepartmentData(string Code, string Name);
    private sealed record PositionData(string Name);
    private sealed record DatabaseOwnershipSnapshot(
        int Version,
        string RepositoryId,
        string WorktreeId,
        string Provider,
        string ServerFingerprint,
        string CatalogFingerprint);
}
