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
    private int _shellPort;
    private bool _catalogCreated;
    private FixtureData? _fixture;
    private PersonData? _person;
    private CompanyData? _company;
    private DepartmentData? _department;
    private PositionData? _position;
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
    private async Task<(string ECode, string Password)> CreateUnauthorizedActorAsync(
        CancellationToken cancellationToken)
    {
        var ecode = $"acceptance.denied.{_runId[..8]}";
        var password = $"Denied-{_runId}-Pass";
        using var administrator = CreateClient(_hostPort);
        await LoginAdminAsync(administrator, cancellationToken);
        using var created = await SendJsonWithAntiforgeryAsync(
            administrator,
            HttpMethod.Post,
            "/api/identity/accounts",
            new { ecode, ename = "Production Cold Start Unauthorized" },
            cancellationToken);
        var createdBody = await created.Content.ReadAsStringAsync(cancellationToken);
        EnsureStatus(created, HttpStatusCode.Created, createdBody, "create unauthorized acceptance account");
        using var createdJson = JsonDocument.Parse(createdBody);
        var temporaryCredential = ReadString(createdJson.RootElement, "temporaryCredential");
        RegisterSecret(temporaryCredential);
        RegisterSecret(password);

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
        return (ecode, password);
    }

    // API 场景只准备浏览器旅程所需的真实人员；字段语义、权限和错误矩阵留在低层集成测试。
    private async Task RunApiScenarioAsync(CancellationToken cancellationToken)
    {
        var fixture = _fixture ?? throw new InvalidOperationException("Personnel fixture was not prepared.");
        using var admin = CreateClient(_hostPort);
        await LoginAdminAsync(admin, cancellationToken);

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
        page.SetDefaultTimeout(30_000);
        page.SetDefaultNavigationTimeout(120_000);
        await page.GotoAsync($"http://127.0.0.1:{_shellPort}/login", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.DOMContentLoaded
        });
        await page.Locator("#ecode").FillAsync("admin");
        await page.Locator("#password").FillAsync(adminPassword);
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "登录", Exact = true }).ClickAsync();
        await WaitForPostLoginNavigationAsync(page);

        var browserAssertions = new List<string>();
        if (phase == "initial")
        {
            var company = await CreateCompanyFromBrowserAsync(page, cancellationToken);
            var department = await CreateDepartmentFromBrowserAsync(page, company, cancellationToken);
            var position = await CreatePositionFromBrowserAsync(page, cancellationToken);
            person = await UpdatePersonFromBrowserAsync(
                page,
                person,
                company,
                department,
                position,
                cancellationToken);
            _company = company;
            _department = department;
            _position = position;
            _person = person;
            await AssertPersonBirthDateRangeAsync(page, person);
            browserAssertions.AddRange(
            [
                "company-created",
                "department-created",
                "position-created",
                "person-organization-affiliation-saved-through-standard-drawer",
                "birth-date-range-finds-person"
            ]);
        }

        await OpenPersonnelFromShellAsync(page);
        await AssertPersonListAndDrawerAsync(
            page,
            person,
            _company?.Name,
            _department?.Name,
            _position?.Name);
        browserAssertions.Add(phase == "initial"
            ? "list-and-detail-show-updated-person"
            : "restart-list-and-detail-show-persisted-person");

        await page.ScreenshotAsync(new PageScreenshotOptions { Path = screenshotPath, FullPage = true });
        _artifactRefs.Add(AcceptanceInfrastructure.RelativeArtifact(_runDirectory, screenshotPath));
        await WriteEvidenceAsync($"personnel/browser-{phase}.json", new
        {
            status = "passed",
            phase,
            url = page.Url,
            browserAssertions,
            screenshot = AcceptanceInfrastructure.RelativeArtifact(_runDirectory, screenshotPath)
        }, cancellationToken);
    }

    private async Task WaitForPostLoginNavigationAsync(IPage page)
    {
        await page.WaitForURLAsync(
            $"http://127.0.0.1:{_shellPort}/",
            new PageWaitForURLOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.GetByRole(
                AriaRole.Button,
                new PageGetByRoleOptions { Name = "退出登录", Exact = true })
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
    }

    private async Task OpenPersonnelFromShellAsync(IPage page)
    {
        var personnelEntry = page.GetByRole(
            AriaRole.Link,
            new PageGetByRoleOptions { Name = "人员中心", Exact = true }).First;
        await personnelEntry.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await personnelEntry.ClickAsync();
        await page.WaitForURLAsync(
            $"http://127.0.0.1:{_shellPort}/organization/persons",
            new PageWaitForURLOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
    }

    // 公司管理只通过 Shell 导航进入唯一标准列表，覆盖活动候选、导航和权限装配。
    private async Task OpenCompanyManagementFromShellAsync(IPage page)
    {
        var companyEntry = page.GetByRole(
            AriaRole.Link,
            new PageGetByRoleOptions { Name = "公司管理", Exact = true }).First;
        await companyEntry.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await companyEntry.ClickAsync();
        await page.WaitForURLAsync(
            $"http://127.0.0.1:{_shellPort}/organization/companies",
            new PageWaitForURLOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
    }

    // 部门管理同样从 Shell 导航进入，避免直达 URL 绕过模块候选与权限路径。
    private async Task OpenDepartmentManagementFromShellAsync(IPage page)
    {
        var departmentEntry = page.GetByRole(
            AriaRole.Link,
            new PageGetByRoleOptions { Name = "部门管理", Exact = true }).First;
        await departmentEntry.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await departmentEntry.ClickAsync();
        await page.WaitForURLAsync(
            $"http://127.0.0.1:{_shellPort}/organization/departments",
            new PageWaitForURLOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
    }

    // 岗位管理只通过 Shell 导航进入现有标准列表，不用直达 URL 绕过候选导航与权限装配。
    private async Task OpenPositionManagementFromShellAsync(IPage page)
    {
        var positionEntry = page.GetByRole(
            AriaRole.Link,
            new PageGetByRoleOptions { Name = "岗位管理", Exact = true }).First;
        await positionEntry.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await positionEntry.ClickAsync();
        await page.WaitForURLAsync(
            $"http://127.0.0.1:{_shellPort}/organization/positions",
            new PageWaitForURLOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
    }

    // 同一浏览器主旅程先通过公司标准 Drawer 建立后续部门与人员归属的真实前置资源。
    private async Task<CompanyData> CreateCompanyFromBrowserAsync(
        IPage page,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await OpenCompanyManagementFromShellAsync(page);
        var company = new CompanyData(
            $"ACC-BC-{_runId[..8]}",
            $"Personnel Gate Browser Company {_runId[..8]}");
        await ClickStandardCrudCreateActionAsync(page, "新建公司");
        var drawer = page.Locator(".ant-drawer-content:visible").Last;
        await drawer.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await drawer.GetByLabel("公司编码", new LocatorGetByLabelOptions { Exact = true }).FillAsync(company.Code);
        await drawer.GetByLabel("公司名称", new LocatorGetByLabelOptions { Exact = true }).FillAsync(company.Name);
        await drawer.GetByLabel("排序码", new LocatorGetByLabelOptions { Exact = true }).FillAsync("0100");

        var responseTask = page.WaitForResponseAsync(static response =>
            response.Request.Method == "POST"
            && new Uri(response.Url).AbsolutePath.Equals(
                "/api/organization/companies",
                StringComparison.Ordinal));
        await drawer.GetByRole(
                AriaRole.Button,
                new LocatorGetByRoleOptions { Name = "保存并关闭", Exact = true })
            .ClickAsync();
        var response = await responseTask;
        var body = await response.TextAsync();
        if (response.Status != (int)HttpStatusCode.Created)
        {
            throw new InvalidOperationException(
                $"Browser company create returned {response.Status}, expected 201: {body}");
        }

        await drawer.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden });
        await page.Locator("tr:visible", new PageLocatorOptions { HasTextString = company.Code })
            .Filter(new LocatorFilterOptions { HasTextString = company.Name })
            .Last
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        return company;
    }

    // 部门创建消费刚由浏览器建立的公司，并保持父部门为空来证明最小支持路径。
    private async Task<DepartmentData> CreateDepartmentFromBrowserAsync(
        IPage page,
        CompanyData company,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await OpenDepartmentManagementFromShellAsync(page);
        var department = new DepartmentData(
            $"ACC-BD-{_runId[..8]}",
            $"Personnel Gate Browser Department {_runId[..8]}");
        await ClickStandardCrudCreateActionAsync(page, "新建部门");
        var drawer = page.Locator(".ant-drawer-content:visible").Last;
        await drawer.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await SelectOrganizationLookupAsync(
            page,
            drawer,
            "所属公司",
            company.Name,
            "/api/organization/person-lookups/companies");
        await drawer.GetByLabel("部门编码", new LocatorGetByLabelOptions { Exact = true }).FillAsync(department.Code);
        await drawer.GetByLabel("部门名称", new LocatorGetByLabelOptions { Exact = true }).FillAsync(department.Name);
        await drawer.GetByLabel("排序码", new LocatorGetByLabelOptions { Exact = true }).FillAsync("0100");

        var responseTask = page.WaitForResponseAsync(static response =>
            response.Request.Method == "POST"
            && new Uri(response.Url).AbsolutePath.Equals(
                "/api/organization/departments",
                StringComparison.Ordinal));
        await drawer.GetByRole(
                AriaRole.Button,
                new LocatorGetByRoleOptions { Name = "保存并关闭", Exact = true })
            .ClickAsync();
        var response = await responseTask;
        var body = await response.TextAsync();
        if (response.Status != (int)HttpStatusCode.Created)
        {
            throw new InvalidOperationException(
                $"Browser department create returned {response.Status}, expected 201: {body}");
        }

        await drawer.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden });
        await page.Locator("tr:visible", new PageLocatorOptions { HasTextString = department.Code })
            .Filter(new LocatorFilterOptions { HasTextString = department.Name })
            .Last
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        return department;
    }

    // 同一 Personnel 浏览器场景通过标准 Drawer 创建岗位，不把字段边界语义提升到浏览器层。
    private async Task<PositionData> CreatePositionFromBrowserAsync(
        IPage page,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await OpenPositionManagementFromShellAsync(page);
        var name = $"Personnel Gate Position {_runId[..8]}";
        var description = "Personnel Gate browser journey";
        await ClickStandardCrudCreateActionAsync(page, "新建岗位");
        var drawer = page.Locator(".ant-drawer-content:visible").Last;
        await drawer.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await drawer.GetByLabel("岗位名称", new LocatorGetByLabelOptions { Exact = true }).FillAsync(name);
        await drawer.GetByLabel("岗位描述", new LocatorGetByLabelOptions { Exact = true }).FillAsync(description);

        var responseTask = page.WaitForResponseAsync(static response =>
            response.Request.Method == "POST"
            && new Uri(response.Url).AbsolutePath.Equals(
                "/api/organization/positions",
                StringComparison.Ordinal));
        await drawer.GetByRole(
                AriaRole.Button,
                new LocatorGetByRoleOptions { Name = "保存并关闭", Exact = true })
            .ClickAsync();
        var response = await responseTask;
        var body = await response.TextAsync();
        if (response.Status != (int)HttpStatusCode.Created)
        {
            throw new InvalidOperationException(
                $"Browser position create returned {response.Status}, expected 201: {body}");
        }

        await drawer.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden });
        var row = page.Locator("tr:visible", new PageLocatorOptions { HasTextString = name })
            .Filter(new LocatorFilterOptions { HasTextString = description })
            .Last;
        await row.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        return new PositionData(name);
    }

    // 一次标准编辑同时维护岗位与时间字段，避免把同一主旅程拆成两次独立提交。
    private async Task<PersonData> UpdatePersonFromBrowserAsync(
        IPage page,
        PersonData person,
        CompanyData company,
        DepartmentData department,
        PositionData position,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await OpenPersonnelFromShellAsync(page);
        await SubmitPersonnelKeywordAsync(page, person.PersonNo);
        var row = page.Locator("tr:visible", new PageLocatorOptions { HasTextString = person.PersonNo })
            .Filter(new LocatorFilterOptions { HasTextString = person.Name })
            .Last;
        await row.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await row.GetByRole(
                AriaRole.Button,
                new LocatorGetByRoleOptions { Name = "编辑", Exact = true })
            .ClickAsync();

        var drawer = page.Locator(".ant-drawer-content:visible").Last;
        await drawer.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await SelectOrganizationLookupAsync(
            page,
            drawer,
            "所属公司",
            company.Name,
            "/api/organization/person-lookups/companies");
        await SelectOrganizationLookupAsync(
            page,
            drawer,
            "所属部门",
            department.Name,
            "/api/organization/person-lookups/departments");
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
                   && HasQueryValue(uri, "keyword", position.Name);
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

        var personApiPath = $"/api/organization/persons/{Uri.EscapeDataString(person.Id)}";
        var updateResponseTask = page.WaitForResponseAsync(response =>
            response.Request.Method == "PUT"
            && new Uri(response.Url).AbsolutePath.Equals(personApiPath, StringComparison.Ordinal));
        await drawer.GetByRole(
                AriaRole.Button,
                new LocatorGetByRoleOptions { Name = "保存修改", Exact = true })
            .ClickAsync();
        var response = await updateResponseTask;
        var body = await response.TextAsync();
        if (response.Status != (int)HttpStatusCode.OK)
        {
            throw new InvalidOperationException(
                $"Browser person update returned {response.Status}, expected 200: {body}");
        }

        await drawer.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden });
        var updated = ReadPerson(body, person.PersonNo, person.Name);
        await page.Locator("tr:visible", new PageLocatorOptions { HasTextString = person.PersonNo })
            .Filter(new LocatorFilterOptions { HasTextString = position.Name })
            .Last
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
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
        await lookup.ClickAsync();
        var responseTask = page.WaitForResponseAsync(response =>
        {
            if (response.Request.Method != "GET")
            {
                return false;
            }

            var uri = new Uri(response.Url);
            return uri.AbsolutePath.Equals(apiPath, StringComparison.Ordinal)
                   && HasQueryValue(uri, "keyword", option);
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

    // 标准页面动作缺失时优先暴露 capability/局部错误，而不是只留下无上下文的 locator 超时。
    private static async Task ClickStandardCrudCreateActionAsync(IPage page, string actionLabel)
    {
        var action = page.GetByRole(
            AriaRole.Button,
            new PageGetByRoleOptions { Name = actionLabel, Exact = true });
        try
        {
            await action.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        }
        catch (TimeoutException exception)
        {
            var diagnostic = await ReadStandardCrudPageDiagnosticAsync(page);
            throw new InvalidOperationException(
                $"Standard CRUD page did not expose '{actionLabel}'. {diagnostic}",
                exception);
        }

        await action.ClickAsync();
    }

    private static async Task<string> ReadStandardCrudPageDiagnosticAsync(IPage page)
    {
        var capabilityError = page.Locator("[data-route-capability-error]").First;
        if (await capabilityError.IsVisibleAsync())
        {
            var code = await capabilityError.GetAttributeAsync("data-route-capability-error");
            return $"Route capability error: {code ?? "unknown"}.";
        }

        var localError = page.Locator(".sh-error-state:visible").First;
        if (await localError.IsVisibleAsync())
        {
            return $"Page error: {NormalizeDiagnostic(await localError.InnerTextAsync())}.";
        }

        var loading = page.GetByRole(AriaRole.Status).First;
        if (await loading.IsVisibleAsync())
        {
            return $"Page status: {NormalizeDiagnostic(await loading.InnerTextAsync())}.";
        }

        var body = NormalizeDiagnostic(await page.Locator("body").InnerTextAsync());
        return $"URL: {page.Url}; no route capability, page error, or loading state was visible. Visible body: {body}.";
    }

    private static string NormalizeDiagnostic(string value)
    {
        var normalized = value.ReplaceLineEndings(" ").Trim();
        return normalized.Length <= 500 ? normalized : normalized[..500];
    }

    // 高级筛选只观察用户旅程：应用显式两端后 URL 与数据库列表共同定位目标人员。
    private static async Task AssertPersonBirthDateRangeAsync(IPage page, PersonData person)
    {
        await page.GetByRole(
                AriaRole.Button,
                new PageGetByRoleOptions { Name = "高级筛选", Exact = true })
            .ClickAsync();
        var drawer = page.Locator(".ant-drawer-content:visible").Last;
        await drawer.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
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
                   && HasQueryValue(uri, "birthDateFrom", PersonnelBirthDate)
                   && HasQueryValue(uri, "birthDateTo", PersonnelBirthDate);
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

        await page.Locator("tr:visible", new PageLocatorOptions { HasTextString = person.PersonNo })
            .Filter(new LocatorFilterOptions { HasTextString = person.Name })
            .Last
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        var currentUri = new Uri(page.Url);
        if (!HasQueryValue(currentUri, "birthDateFrom", PersonnelBirthDate)
            || !HasQueryValue(currentUri, "birthDateTo", PersonnelBirthDate))
        {
            throw new InvalidOperationException("Personnel applied birth date range was not preserved in the browser URL.");
        }
    }

    // 浏览器请求查询值按 application/x-www-form-urlencoded 解码后做精确比较，避免空格编码差异伪造搜索证据。
    private static bool HasQueryValue(Uri uri, string key, string expectedValue)
    {
        foreach (var segment in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = segment.Split('=', 2);
            if (pair.Length == 2
                && string.Equals(Uri.UnescapeDataString(pair[0]), key, StringComparison.Ordinal)
                && string.Equals(
                    Uri.UnescapeDataString(pair[1].Replace('+', ' ')),
                    expectedValue,
                    StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    // 列表与查看 Drawer 只证明更新后的人员能够从正式用户入口重新读到。
    private static async Task AssertPersonListAndDrawerAsync(
        IPage page,
        PersonData person,
        string? companyName,
        string? departmentName,
        string? positionName)
    {
        await SubmitPersonnelKeywordAsync(page, person.PersonNo);
        var row = page.Locator("tr:visible", new PageLocatorOptions { HasTextString = person.PersonNo })
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

        var drawer = page.Locator(".ant-drawer-content:visible").Last;
        await drawer.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
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
        var keyword = page.GetByLabel("关键字", new PageGetByLabelOptions { Exact = true });
        await keyword.FillAsync(keywordValue);
        await keyword.PressAsync("Enter");
        await page.WaitForFunctionAsync(
            "value => new URL(window.location.href).searchParams.get('keyword') === value",
            keywordValue);
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
        _ownershipSnapshot = await ReadOwnershipSnapshotAsync(cancellationToken);
        if (result.ExitCode == 0
            || !string.Equals(status, "failed", StringComparison.Ordinal)
            || !string.Equals(failureCode, "DEV_HOST_READINESS_FAILED", StringComparison.Ordinal)
            || !diagnostic.Contains("IDENTITY_BOOTSTRAP_REQUIRED", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Empty Personnel catalog startup returned exit={result.ExitCode}, status={status}, failure={failureCode}.");
        }

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
        EnsurePersonnelJourneyPermissions(user.RootElement);
    }

    // 浏览器动作权限来自同一认证快照；在 API 层先给出缺失 permission 的精确失败信号。
    private static void EnsurePersonnelJourneyPermissions(JsonElement user)
    {
        if (!user.TryGetProperty("permissions", out var permissions)
            || permissions.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("Administrator authentication response did not contain permissions.");
        }

        var granted = permissions.EnumerateArray()
            .Where(static permission => permission.ValueKind == JsonValueKind.String)
            .Select(static permission => permission.GetString())
            .Where(static permission => !string.IsNullOrWhiteSpace(permission))
            .ToHashSet(StringComparer.Ordinal);
        var required = new[]
        {
            "Organization.Company.Create",
            "Organization.Department.Create",
            "Organization.Person.Update"
        };
        var missing = required.Where(permission => !granted.Contains(permission)).ToArray();
        if (missing.Length > 0)
        {
            throw new InvalidOperationException(
                $"Administrator authentication response is missing Personnel journey permissions: {string.Join(", ", missing)}.");
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

    private static async Task<PersonData> CreatePersonAsync(
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
            cancellationToken);
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
