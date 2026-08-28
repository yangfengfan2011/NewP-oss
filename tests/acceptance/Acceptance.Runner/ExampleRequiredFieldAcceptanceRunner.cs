using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ICP.Platform.HostApi.Acceptance;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Playwright;

namespace ICP.Platform.Acceptance;

internal sealed record ExampleRequiredFieldAcceptanceExecution(
    string? DatabaseFingerprint,
    IReadOnlyList<string> ProcessIdentities,
    IReadOnlyList<string> ArtifactRefs,
    AcceptanceCleanupResult Cleanup);

internal sealed class ExampleRequiredFieldAcceptanceRunner
{
    internal const string PreflightScenario = "example-required-field.preflight";
    internal const string MigrationScenario = "example-required-field.migration";
    internal const string BackfillRecoveryScenario = "example-required-field.backfill-recovery";
    internal const string ContractPreconditionScenario = "example-required-field.contract-precondition";
    internal const string ApiScenario = "example-required-field.api";
    internal const string BrowserScenario = "example-required-field.browser";
    internal const string RestartScenario = "example-required-field.restart";

    internal static IReadOnlyList<string> RequiredScenarios { get; } =
    [
        PreflightScenario,
        MigrationScenario,
        BackfillRecoveryScenario,
        ContractPreconditionScenario,
        ApiScenario,
        BrowserScenario,
        RestartScenario
    ];

    private const string AuthenticationCookieName = "ICP.Platform.ExampleRequiredFieldAcceptance";
    private const string NullableMigration = "20260828082341_Example_000003_RecordSourceNullable";
    private const string BackfillMigration = "20260828082358_Example_000004_RecordSourceBackfill";
    private const string RequiredMigration = "20260828082459_Example_000005_RecordSourceRequired";
    private static readonly Regex CatalogPattern = new(
        "^NewP_acceptance_example_[0-9a-f]{24}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private readonly string _repositoryRoot;
    private readonly string _runDirectory;
    private readonly string _runId;
    private readonly AcceptanceObservationRecorder _recorder;
    private readonly List<string> _artifactRefs = [];
    private readonly List<string> _processIdentities = [];
    private readonly List<string> _secretTokens = [];
    private readonly Dictionary<string, string?> _devEnvironment = new(StringComparer.OrdinalIgnoreCase);
    private string _activeScenario = PreflightScenario;
    private string? _adminConnectionString;
    private string? _applicationConnectionString;
    private string? _connectionSource;
    private string? _catalog;
    private string? _dataPrefix;
    private string? _pluginRoot;
    private string? _keyRingRoot;
    private string? _lastDevRunId;
    private int _hostPort;
    private int _shellPort;
    private bool _catalogCreated;
    private DatabaseOwnershipSnapshot? _ownershipSnapshot;
    private string? _adminPassword;
    private string? _apiRecordId;
    private string? _browserCode;

    public ExampleRequiredFieldAcceptanceRunner(
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

    /// <summary>
    /// 执行 Example required-field 正式验收旅程，并返回 runner 归属资源的证据索引。
    /// </summary>
    public async Task<ExampleRequiredFieldAcceptanceExecution> RunAsync(CancellationToken cancellationToken)
    {
        AcceptanceCleanupResult cleanup = new("not-started", AcceptanceFailureCodes.CleanupIncomplete);

        try
        {
            await RunPreflightAsync(cancellationToken);
            await RecordScenarioSuccessAsync(
                PreflightScenario,
                "example-required-field/preflight.json",
                new
                {
                    status = "passed",
                    connectionSource = _connectionSource,
                    runId = _runId,
                    catalog = _catalog,
                    dataPrefix = _dataPrefix,
                    checkpoint = "example-required-field/checkpoint.json"
                },
                cancellationToken);

            _activeScenario = BackfillRecoveryScenario;
            await RunBackfillRecoveryScenarioAsync(cancellationToken);

            _activeScenario = ContractPreconditionScenario;
            await RunContractPreconditionScenarioAsync(cancellationToken);

            _activeScenario = MigrationScenario;
            await RunMigrationScenarioAsync(cancellationToken);

            _activeScenario = ApiScenario;
            await RunApiScenarioAsync(cancellationToken);

            _activeScenario = BrowserScenario;
            await RunBrowserScenarioAsync(cancellationToken);

            _activeScenario = RestartScenario;
            await RunRestartScenarioAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            var failureCode = FailureCodeFor(_activeScenario);
            var evidenceRef = await WriteEvidenceAsync(
                $"example-required-field/{_activeScenario.Replace('.', '-')}-failure.json",
                new { status = "failed", failureCode, diagnostic = Sanitize(exception.Message) },
                cancellationToken);
            await _recorder.RecordAsync(
                AcceptanceGateKind.ExampleRequiredField,
                AcceptanceEventType.ScenarioCompleted,
                AcceptanceObservationOutcome.Failed,
                _activeScenario,
                evidenceRef,
                failureCode,
                cancellationToken);
        }
        finally
        {
            cleanup = await TeardownAsync(CancellationToken.None);
            await ScanEvidenceForSecretsAsync(CancellationToken.None);
        }

        return new ExampleRequiredFieldAcceptanceExecution(
            _catalog is null ? null : AcceptanceInfrastructure.Sha256(_catalog),
            _processIdentities,
            _artifactRefs,
            cleanup);
    }

    // Preflight 固定唯一 catalog、数据前缀、Host/Shell 资源根和受支持的 managed dev 入口。
    private async Task RunPreflightAsync(CancellationToken cancellationToken)
    {
        var adminConnection = AcceptanceSqlServerConnectionResolver.Resolve(_repositoryRoot);
        var adminBuilder = new SqlConnectionStringBuilder(adminConnection.AdminConnectionString);
        _adminConnectionString = adminConnection.AdminConnectionString;
        _connectionSource = adminConnection.SourceName;
        AddConnectionSecrets(adminConnection.ConfiguredConnectionString, adminConnection.Password);

        _catalog = $"NewP_acceptance_example_{_runId}";
        ValidateCatalogName(_catalog);
        _dataPrefix = $"ACC140-{_runId[..8]}";
        _applicationConnectionString = new SqlConnectionStringBuilder(adminBuilder.ConnectionString)
        {
            InitialCatalog = _catalog,
            Pooling = false
        }.ConnectionString;
        _pluginRoot = Path.Combine(_runDirectory, "resources", "plugins");
        _keyRingRoot = Path.Combine(_runDirectory, "resources", "key-ring");
        Directory.CreateDirectory(_pluginRoot);
        Directory.CreateDirectory(_keyRingRoot);

        _devEnvironment["NEWP_DEV_SQLSERVER_CONNECTION_STRING"] = _applicationConnectionString;
        _devEnvironment["ConnectionStrings__SqlServer"] = _applicationConnectionString;
        _devEnvironment["Platform__PluginInstallRoot"] = _pluginRoot;
        _devEnvironment["Authentication__Host__KeyRingPath"] = _keyRingRoot;
        _devEnvironment["Authentication__Host__CookieName"] = AuthenticationCookieName;
        _devEnvironment["Authentication__Host__AllowInsecureDevelopmentCookie"] = "true";
        _devEnvironment["Identity__LocalAdministrator__ECODE"] = "admin";
        _devEnvironment["Identity__LocalAdministrator__ENAME"] = "Example Required Field Acceptance Administrator";
        _devEnvironment["ASPNETCORE_ENVIRONMENT"] = "Development";
        _devEnvironment["Acceptance__Enabled"] = "true";
        _devEnvironment["Acceptance__RunId"] = _runId;
        var fixtureSecret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        _secretTokens.Add(fixtureSecret);
        _devEnvironment["Acceptance__Secret"] = fixtureSecret;
        _devEnvironment["Logging__LogLevel__Default"] = "Information";

        await RunDevScriptAsync(
            "stop-dev.ps1",
            "example-required-field/preflight-stop-existing.json",
            cancellationToken,
            allowAlreadyStopped: true);
        var build = await AcceptanceInfrastructure.RunProcessAsync(
            "dotnet",
            ["build", "apps/host-api/HostApi.csproj", "--configuration", "Release", "--no-restore", "--nologo"],
            _repositoryRoot,
            cancellationToken: cancellationToken);
        await WriteEvidenceAsync(
            "example-required-field/preflight-build.json",
            new
            {
                command = "dotnet build apps/host-api/HostApi.csproj --configuration Release --no-restore",
                exitCode = build.ExitCode,
                standardError = Sanitize(build.StandardError)
            },
            cancellationToken);
        if (build.ExitCode != 0)
        {
            throw new InvalidOperationException($"HostApi release build failed: {Sanitize(build.StandardError)}");
        }

        await WriteCheckpointAsync("ready", PreflightScenario, cancellationToken);
    }

    // Backfill 故障由 runner-owned trigger 注入；移除故障后必须从未记录的 backfill 继续并到达 Ready。
    private async Task RunBackfillRecoveryScenarioAsync(CancellationToken cancellationToken)
    {
        await ResetCatalogToHistoricalStageAsync(stage: 3, cancellationToken);
        await SeedStageThreeRowsAsync(cancellationToken);
        await CreateBackfillFailureTriggerAsync(cancellationToken);
        await WriteCheckpointAsync("running", "backfill-failure-injected", cancellationToken);

        var failure = await RunDevExpectFailureAsync(
            "example-required-field/backfill-failure-dev.json",
            "ACC140_BACKFILL_FAILURE",
            cancellationToken);
        var failedState = await ReadExampleStateAsync(cancellationToken);
        AssertState(
            failedState,
            nullableExpected: true,
            expectedLegacySource: null,
            expectedManualSource: "Manual",
            expectedMigrations: [NullableMigration],
            unexpectedMigrations: [BackfillMigration, RequiredMigration]);
        if (!failure.Diagnostic.Contains("ACC140_BACKFILL_FAILURE", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Managed Host failure did not retain the injected backfill diagnostic.");
        }

        await RunDevScriptAsync(
            "stop-dev.ps1",
            "example-required-field/backfill-failure-stop.json",
            cancellationToken,
            allowAlreadyStopped: true);
        await DropTriggerAsync(BackfillFailureTriggerName, cancellationToken);
        await WriteCheckpointAsync("resuming", "backfill-failure-removed", cancellationToken);

        var bootstrapRequired = await RunDevExpectFailureAsync(
            "example-required-field/backfill-resume-dev.json",
            "IDENTITY_BOOTSTRAP_REQUIRED",
            cancellationToken);
        if (!bootstrapRequired.Diagnostic.Contains("IDENTITY_BOOTSTRAP_REQUIRED", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Recovered migration did not reach the identity bootstrap readiness boundary.");
        }
        var recoveredState = await ReadExampleStateAsync(cancellationToken);
        AssertState(
            recoveredState,
            nullableExpected: false,
            expectedLegacySource: "Legacy",
            expectedManualSource: "Manual",
            expectedMigrations: [NullableMigration, BackfillMigration, RequiredMigration],
            unexpectedMigrations: []);
        await RunDevScriptAsync(
            "stop-dev.ps1",
            "example-required-field/backfill-resume-stop.json",
            cancellationToken,
            allowAlreadyStopped: true);

        var bootstrap = await FormalIdentityBootstrap.RunAsync(
            _repositoryRoot,
            Path.Combine(_runDirectory, "resources"),
            _devEnvironment,
            cancellationToken);
        _secretTokens.Add(bootstrap.TemporaryCredential);
        _processIdentities.Add($"identity-bootstrap:{bootstrap.ProcessId}:{_runId}");
        await StartDevAsync("example-required-field/backfill-ready-dev.json", cancellationToken);
        await AssertReadyAsync(cancellationToken);
        _ownershipSnapshot = await ReadOwnershipSnapshotAsync(cancellationToken);

        await RecordScenarioSuccessAsync(
            BackfillRecoveryScenario,
            "example-required-field/backfill-recovery.json",
            new
            {
                status = "passed",
                injectedFailure = "ACC140_BACKFILL_FAILURE",
                failedSupervisorCode = failure.FailureCode,
                failedState,
                resumeCheckpoint = "backfill-failure-removed",
                recoveredState,
                hostReady = true,
                hostPort = _hostPort,
                shellPort = _shellPort
            },
            cancellationToken);
        await WriteCheckpointAsync("passed", BackfillRecoveryScenario, cancellationToken);
    }

    // Contract precondition 通过 no-op UPDATE 保留 NULL，使 required migration 在收窄 schema 前失败。
    private async Task RunContractPreconditionScenarioAsync(CancellationToken cancellationToken)
    {
        await ResetCatalogToHistoricalStageAsync(stage: 3, cancellationToken);
        await SeedStageThreeRowsAsync(cancellationToken);
        await CreateContractPreconditionTriggerAsync(cancellationToken);
        await WriteCheckpointAsync("running", "contract-precondition-injected", cancellationToken);

        var failure = await RunDevExpectFailureAsync(
            "example-required-field/contract-precondition-dev.json",
            expectedDiagnostic: null,
            cancellationToken);
        var state = await ReadExampleStateAsync(cancellationToken);
        AssertState(
            state,
            nullableExpected: true,
            expectedLegacySource: null,
            expectedManualSource: "Manual",
            expectedMigrations: [NullableMigration, BackfillMigration],
            unexpectedMigrations: [RequiredMigration]);
        await RunDevScriptAsync(
            "stop-dev.ps1",
            "example-required-field/contract-precondition-stop.json",
            cancellationToken,
            allowAlreadyStopped: true);

        await RecordScenarioSuccessAsync(
            ContractPreconditionScenario,
            "example-required-field/contract-precondition.json",
            new
            {
                status = "passed",
                supervisorFailureCode = failure.FailureCode,
                hostReady = false,
                nullPreserved = true,
                requiredMigrationRecorded = false,
                state
            },
            cancellationToken);
        await WriteCheckpointAsync("passed", ContractPreconditionScenario, cancellationToken);
    }

    // 成功旅程从 stage 2 旧行开始，经 nullable 插入 Manual，再由真实 managed Host 完成 backfill/contract。
    private async Task RunMigrationScenarioAsync(CancellationToken cancellationToken)
    {
        await ResetCatalogToHistoricalStageAsync(stage: 2, cancellationToken);
        await SeedStageTwoLegacyRowAsync(cancellationToken);
        await ApplyHistoricalStageAsync(stage: 3, cancellationToken);
        await SeedStageThreeManualRowAsync(cancellationToken);
        var nullableState = await ReadExampleStateAsync(cancellationToken);
        AssertState(
            nullableState,
            nullableExpected: true,
            expectedLegacySource: null,
            expectedManualSource: "Manual",
            expectedMigrations: [NullableMigration],
            unexpectedMigrations: [BackfillMigration, RequiredMigration]);
        await WriteCheckpointAsync("running", "three-step-current-host", cancellationToken);

        var bootstrapRequired = await RunDevExpectFailureAsync(
            "example-required-field/migration-dev.json",
            "IDENTITY_BOOTSTRAP_REQUIRED",
            cancellationToken);
        var migratedState = await ReadExampleStateAsync(cancellationToken);
        AssertState(
            migratedState,
            nullableExpected: false,
            expectedLegacySource: "Legacy",
            expectedManualSource: "Manual",
            expectedMigrations: [NullableMigration, BackfillMigration, RequiredMigration],
            unexpectedMigrations: []);
        await RunDevScriptAsync(
            "stop-dev.ps1",
            "example-required-field/migration-bootstrap-required-stop.json",
            cancellationToken,
            allowAlreadyStopped: true);

        var bootstrap = await FormalIdentityBootstrap.RunAsync(
            _repositoryRoot,
            Path.Combine(_runDirectory, "resources"),
            _devEnvironment,
            cancellationToken);
        _secretTokens.Add(bootstrap.TemporaryCredential);
        _processIdentities.Add($"identity-bootstrap:{bootstrap.ProcessId}:{_runId}");
        await StartDevAsync("example-required-field/migration-ready-dev.json", cancellationToken);
        await AssertReadyAsync(cancellationToken);
        _ownershipSnapshot = await ReadOwnershipSnapshotAsync(cancellationToken);
        await PrepareAdministratorPasswordAsync(bootstrap.TemporaryCredential, cancellationToken);
        var readyState = await ReadExampleStateAsync(cancellationToken);
        AssertEquivalentMigrationState(migratedState, readyState, "identity bootstrap restart");

        await RecordScenarioSuccessAsync(
            MigrationScenario,
            "example-required-field/migration.json",
            new
            {
                status = "passed",
                threeStepHistory = new[] { NullableMigration, BackfillMigration, RequiredMigration },
                nullableState,
                migratedState,
                readyState,
                supervisorFailureCode = bootstrapRequired.FailureCode,
                hostReady = true,
                restartNoOp = true
            },
            cancellationToken);
        await WriteCheckpointAsync("passed", MigrationScenario, cancellationToken);
    }

    // 正式 API 证明 create required、Manual/Legacy 输出、PUT partial-update 与 explicit null 语义。
    private async Task RunApiScenarioAsync(CancellationToken cancellationToken)
    {
        using var client = CreateClient(_hostPort);
        await LoginAdminAsync(client, cancellationToken);
        var checks = new List<object>();
        var permissionSnapshot = await ReadAdministratorPermissionSnapshotAsync(cancellationToken);
        await WriteEvidenceAsync(
            "example-required-field/api-permissions.json",
            permissionSnapshot,
            cancellationToken);
        var expectedPermissions = new[]
        {
            "Example.Record.View",
            "Example.Record.Create",
            "Example.Record.Update"
        };
        if (expectedPermissions.Except(permissionSnapshot.PermissionCodes, StringComparer.Ordinal).Any())
        {
            throw new InvalidOperationException("LocalAdmin active permission catalog omitted an Example CRUD permission.");
        }

        using (var legacy = await client.GetAsync(
                   $"/api/example/records/{Uri.EscapeDataString(LegacyId)}",
                   cancellationToken))
        {
            var body = await legacy.Content.ReadAsStringAsync(cancellationToken);
            EnsureStatus(legacy, HttpStatusCode.OK, body, "migrated legacy detail");
            AssertField(body, "RecordSource", "Legacy", "migrated legacy detail");
            checks.Add(new { check = "migrated-legacy-detail", status = 200, recordSource = "Legacy" });
        }

        using (var missing = await SendJsonWithAntiforgeryAsync(
                   client,
                   HttpMethod.Post,
                   "/api/example/records",
                   new Dictionary<string, object?>
                   {
                       ["ECODE"] = $"{_dataPrefix}-API-MISSING",
                       ["ENAME"] = "Missing source"
                   },
                   cancellationToken))
        {
            var body = await missing.Content.ReadAsStringAsync(cancellationToken);
            EnsureProblem(missing, body, HttpStatusCode.BadRequest, "Example.Record.SourceRequired", "missing source");
            checks.Add(new { check = "create-missing-source", status = 400, code = "Example.Record.SourceRequired" });
        }

        var apiCode = $"{_dataPrefix}-API";
        using (var created = await SendJsonWithAntiforgeryAsync(
                   client,
                   HttpMethod.Post,
                   "/api/example/records",
                   new Dictionary<string, object?>
                   {
                       ["ECODE"] = apiCode,
                       ["ENAME"] = "API Manual row",
                       ["RecordSource"] = "Manual"
                   },
                   cancellationToken))
        {
            var body = await created.Content.ReadAsStringAsync(cancellationToken);
            EnsureStatus(created, HttpStatusCode.Created, body, "create explicit Manual row");
            _apiRecordId = ReadCreatedRecordId(body);
            AssertField(body, "RecordSource", "Manual", "created explicit Manual row");
            checks.Add(new { check = "create-explicit-manual", status = 201, recordSource = "Manual" });
        }

        using (var omitted = await SendJsonWithAntiforgeryAsync(
                   client,
                   HttpMethod.Put,
                   $"/api/example/records/{Uri.EscapeDataString(_apiRecordId!)}",
                   new Dictionary<string, object?> { ["ENAME"] = "API partial update" },
                   cancellationToken))
        {
            var body = await omitted.Content.ReadAsStringAsync(cancellationToken);
            EnsureStatus(omitted, HttpStatusCode.OK, body, "PUT omitted RecordSource");
            AssertField(body, "RecordSource", "Manual", "PUT omitted RecordSource");
            checks.Add(new
            {
                check = "put-omitted-source-preserved",
                status = 200,
                recordSource = "Manual"
            });
        }

        using (var explicitNull = await SendJsonWithAntiforgeryAsync(
                   client,
                   HttpMethod.Put,
                   $"/api/example/records/{Uri.EscapeDataString(_apiRecordId!)}",
                   new Dictionary<string, object?> { ["RecordSource"] = null },
                   cancellationToken))
        {
            var body = await explicitNull.Content.ReadAsStringAsync(cancellationToken);
            EnsureProblem(
                explicitNull,
                body,
                HttpStatusCode.BadRequest,
                "Example.Record.SourceRequired",
                "PUT explicit null");
            checks.Add(new { check = "put-explicit-null-rejected", status = 400, code = "Example.Record.SourceRequired" });
        }

        using (var detail = await client.GetAsync(
                   $"/api/example/records/{Uri.EscapeDataString(_apiRecordId!)}",
                   cancellationToken))
        {
            var body = await detail.Content.ReadAsStringAsync(cancellationToken);
            EnsureStatus(detail, HttpStatusCode.OK, body, "detail after rejected explicit null");
            AssertField(body, "RecordSource", "Manual", "detail after rejected explicit null");
            AssertField(body, "ENAME", "API partial update", "detail after rejected explicit null");
            checks.Add(new { check = "rejected-null-state-unchanged", status = 200, recordSource = "Manual" });
        }

        using (var list = await client.GetAsync(
                   "/api/example/records?page=1&pageSize=50&sortBy=ECODE&sortDirection=asc",
                   cancellationToken))
        {
            var body = await list.Content.ReadAsStringAsync(cancellationToken);
            EnsureStatus(list, HttpStatusCode.OK, body, "Example list");
            if (!body.Contains(LegacyCode, StringComparison.Ordinal)
                || !body.Contains(apiCode, StringComparison.Ordinal)
                || !body.Contains("Legacy", StringComparison.Ordinal)
                || !body.Contains("Manual", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Example list omitted migrated or explicit required-field values.");
            }
            checks.Add(new { check = "list-legacy-and-manual", status = 200 });
        }

        await RecordScenarioSuccessAsync(
            ApiScenario,
            "example-required-field/api.json",
            new { status = "passed", checks },
            cancellationToken);
        await WriteCheckpointAsync("passed", ApiScenario, cancellationToken);
    }

    // 浏览器证据只由仓库 Acceptance runner 的 .NET Playwright owner 产生，不拼接开发 CLI smoke。
    private async Task RunBrowserScenarioAsync(CancellationToken cancellationToken)
    {
        var password = _adminPassword
            ?? throw new InvalidOperationException("Administrator password was not prepared.");
        _browserCode = $"{_dataPrefix}-BROWSER";
        var browserName = "Browser Manual row";
        var screenshotPath = Path.Combine(_runDirectory, "example-required-field", "browser.png");
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
        page.SetDefaultTimeout(120_000);
        page.SetDefaultNavigationTimeout(120_000);
        await page.GotoAsync($"http://127.0.0.1:{_shellPort}/login", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.DOMContentLoaded
        });
        await page.Locator("#ecode").FillAsync("admin");
        await page.Locator("#password").FillAsync(password);
        await page.GetByRole(
            AriaRole.Button,
            new PageGetByRoleOptions { Name = "登录", Exact = true }).ClickAsync();
        await WaitForPostLoginNavigationAsync(page);
        await page.GotoAsync(
            $"http://127.0.0.1:{_shellPort}/examples/standard-crud/persistent-records",
            new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.GetByRole(
            AriaRole.Heading,
            new PageGetByRoleOptions { Name = "持久化记录示例", Exact = true }).WaitForAsync();
        await page.GetByText(LegacyCode, new PageGetByTextOptions { Exact = true }).WaitForAsync();
        var legacyRow = page.Locator("tr").Filter(new LocatorFilterOptions { HasText = LegacyCode });
        await AssertLocatorContainsAsync(legacyRow, "Legacy", "migrated Legacy list value");

        await page.GetByRole(
            AriaRole.Button,
            new PageGetByRoleOptions { Name = "新建记录", Exact = true }).ClickAsync();
        var createDrawer = page.Locator(".ant-drawer-content:visible");
        await createDrawer.WaitForAsync();
        var sourceInput = createDrawer.GetByLabel("记录来源", new LocatorGetByLabelOptions { Exact = true });
        if (!string.Equals(await sourceInput.InputValueAsync(), "Manual", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Create drawer did not project the authoring default Manual.");
        }
        var requiredLabel = createDrawer.Locator("label.ant-form-item-required").Filter(
            new LocatorFilterOptions { HasText = "记录来源" });
        if (await requiredLabel.CountAsync() != 1)
        {
            throw new InvalidOperationException("Create drawer did not render RecordSource as required.");
        }
        await createDrawer.GetByLabel("示例编码", new LocatorGetByLabelOptions { Exact = true }).FillAsync(_browserCode);
        await createDrawer.GetByLabel("示例名称", new LocatorGetByLabelOptions { Exact = true }).FillAsync(browserName);
        await createDrawer.GetByRole(
            AriaRole.Button,
            new LocatorGetByRoleOptions { Name = "保存并关闭", Exact = true }).ClickAsync();

        var browserRow = page.Locator("tr").Filter(new LocatorFilterOptions { HasText = _browserCode });
        await browserRow.WaitForAsync();
        await AssertLocatorContainsAsync(browserRow, "Manual", "created Manual list value");
        await browserRow.GetByRole(
            AriaRole.Button,
            new LocatorGetByRoleOptions { Name = "编辑", Exact = true }).ClickAsync();
        var editDrawer = page.Locator(".ant-drawer-content:visible");
        await editDrawer.WaitForAsync();
        var editSource = editDrawer.GetByLabel("记录来源", new LocatorGetByLabelOptions { Exact = true });
        if (!string.Equals(await editSource.InputValueAsync(), "Manual", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Edit drawer did not preserve RecordSource Manual.");
        }
        await editDrawer.GetByLabel("示例名称", new LocatorGetByLabelOptions { Exact = true })
            .FillAsync("Browser Manual updated");
        await editDrawer.GetByRole(
            AriaRole.Button,
            new LocatorGetByRoleOptions { Name = "保存修改", Exact = true }).ClickAsync();
        browserRow = page.Locator("tr").Filter(new LocatorFilterOptions { HasText = _browserCode });
        await AssertLocatorContainsAsync(browserRow, "Browser Manual updated", "edited browser list row");
        await AssertLocatorContainsAsync(browserRow, "Manual", "edited Manual list value");

        await browserRow.GetByText(_browserCode, new LocatorGetByTextOptions { Exact = true }).ClickAsync();
        var detailDrawer = page.Locator(".ant-drawer-content:visible");
        await detailDrawer.GetByText("查看记录", new LocatorGetByTextOptions { Exact = true }).WaitForAsync();
        await AssertLocatorContainsAsync(detailDrawer, "Manual", "browser detail Manual value");
        await detailDrawer.GetByRole(
            AriaRole.Button,
            new LocatorGetByRoleOptions { Name = "Close", Exact = true }).ClickAsync();

        legacyRow = page.Locator("tr").Filter(new LocatorFilterOptions { HasText = LegacyCode });
        await legacyRow.GetByText(LegacyCode, new LocatorGetByTextOptions { Exact = true }).ClickAsync();
        detailDrawer = page.Locator(".ant-drawer-content:visible");
        await AssertLocatorContainsAsync(detailDrawer, "Legacy", "migrated browser detail value");
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = screenshotPath, FullPage = true });
        AddArtifact(screenshotPath);

        await RecordScenarioSuccessAsync(
            BrowserScenario,
            "example-required-field/browser.json",
            new
            {
                status = "passed",
                owner = "Acceptance.Runner/.NET Playwright",
                session = _runId,
                route = "/examples/standard-crud/persistent-records",
                checkpoint = BrowserScenario,
                assertions = new[]
                {
                    "legacy-list-and-detail",
                    "create-required-default-manual",
                    "create-manual",
                    "edit-preserves-manual",
                    "list-detail-manual"
                },
                screenshot = "example-required-field/browser.png"
            },
            cancellationToken);
        await WriteCheckpointAsync("passed", BrowserScenario, cancellationToken);
    }

    // Restart 使用新的 Host/Shell 与 API session，证明已完成 history 不重放且正式数据仍可读。
    private async Task RunRestartScenarioAsync(CancellationToken cancellationToken)
    {
        var before = await ReadExampleStateAsync(cancellationToken);
        await RunDevScriptAsync(
            "stop-dev.ps1",
            "example-required-field/restart-stop.json",
            cancellationToken,
            allowAlreadyStopped: true);
        await StartDevAsync("example-required-field/restart-dev.json", cancellationToken);
        await AssertReadyAsync(cancellationToken);
        var after = await ReadExampleStateAsync(cancellationToken);
        AssertEquivalentMigrationState(before, after, "managed Host restart");

        using var client = CreateClient(_hostPort);
        await LoginAdminAsync(client, cancellationToken);
        using (var legacy = await client.GetAsync(
                   $"/api/example/records/{Uri.EscapeDataString(LegacyId)}",
                   cancellationToken))
        {
            var body = await legacy.Content.ReadAsStringAsync(cancellationToken);
            EnsureStatus(legacy, HttpStatusCode.OK, body, "legacy detail after restart");
            AssertField(body, "RecordSource", "Legacy", "legacy detail after restart");
        }
        using (var api = await client.GetAsync(
                   $"/api/example/records/{Uri.EscapeDataString(_apiRecordId!)}",
                   cancellationToken))
        {
            var body = await api.Content.ReadAsStringAsync(cancellationToken);
            EnsureStatus(api, HttpStatusCode.OK, body, "API detail after restart");
            AssertField(body, "RecordSource", "Manual", "API detail after restart");
        }
        using (var list = await client.GetAsync(
                   "/api/example/records?page=1&pageSize=50&sortBy=ECODE&sortDirection=asc",
                   cancellationToken))
        {
            var body = await list.Content.ReadAsStringAsync(cancellationToken);
            EnsureStatus(list, HttpStatusCode.OK, body, "Example list after restart");
            if (!body.Contains(_browserCode!, StringComparison.Ordinal)
                || !body.Contains("Manual", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Browser-created Manual row was not persisted across restart.");
            }
        }

        await RecordScenarioSuccessAsync(
            RestartScenario,
            "example-required-field/restart.json",
            new
            {
                status = "passed",
                before,
                after,
                historyNoOp = true,
                hostReady = true,
                apiSessionRecreated = true,
                persistedRows = new[] { LegacyCode, $"{_dataPrefix}-API", _browserCode }
            },
            cancellationToken);
        await WriteCheckpointAsync("passed", RestartScenario, cancellationToken);
    }

    // 历史计划由 HostApi Acceptance fixture 经正式 persistence seam 应用，runner 不跨越实现边界。
    private async Task ResetCatalogToHistoricalStageAsync(int stage, CancellationToken cancellationToken)
    {
        await RunDevScriptAsync(
            "stop-dev.ps1",
            "example-required-field/reset-stop.json",
            cancellationToken,
            allowAlreadyStopped: true);
        if (_catalogCreated)
        {
            await VerifyCatalogOwnershipAsync(cancellationToken);
            await DropCatalogAsync(cancellationToken);
        }
        await CreateCatalogAsync(cancellationToken);

        await ApplyHistoricalStageAsync(stage, cancellationToken);
        _ownershipSnapshot = await ReadOwnershipSnapshotAsync(cancellationToken);
    }

    // 在同一 catalog 上推进历史前缀，支持 stage2 旧行与 stage3 Manual 行的真实时序。
    private async Task ApplyHistoricalStageAsync(int stage, CancellationToken cancellationToken)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Acceptance:Enabled"] = "true",
                ["Acceptance:RunId"] = _runId,
                ["Persistence:Adapter"] = "EfCore",
                ["Persistence:Provider"] = "SqlServer",
                ["ConnectionStrings:SqlServer"] = _applicationConnectionString
            })
            .Build();
        await ExampleRequiredFieldMigrationFixture.ApplyHistoricalStageAsync(
            _repositoryRoot,
            configuration,
            _runId,
            "Acceptance",
            stage,
            cancellationToken);
    }

    // Stage 2 行模拟尚未出现 RecordSource 列的旧数据。
    private async Task SeedStageTwoLegacyRowAsync(CancellationToken cancellationToken)
    {
        await ExecuteNonQueryAsync(
            """
            INSERT INTO [dbo].[ExampleRecord]
                ([EID], [CreatedAt], [CreatedBy], [ECODE], [ENAME], [ReviewerUserEID], [UpdatedAt], [UpdatedBy])
            VALUES
                (@legacyId, SYSUTCDATETIME(), N'acceptance', @legacyCode, N'Legacy migration row', NULL, SYSUTCDATETIME(), N'acceptance');
            """,
            command =>
            {
                command.Parameters.AddWithValue("@legacyId", LegacyId);
                command.Parameters.AddWithValue("@legacyCode", LegacyCode);
            },
            cancellationToken);
    }

    // Nullable 前缀应用后插入 Manual，证明 backfill 不覆盖已有非空业务值。
    private async Task SeedStageThreeManualRowAsync(CancellationToken cancellationToken)
    {
        await ExecuteNonQueryAsync(
            """
            INSERT INTO [dbo].[ExampleRecord]
                ([EID], [CreatedAt], [CreatedBy], [ECODE], [ENAME], [ReviewerUserEID], [RecordSource], [UpdatedAt], [UpdatedBy])
            VALUES
                (@manualId, SYSUTCDATETIME(), N'acceptance', @manualCode, N'Manual migration row', NULL, N'Manual', SYSUTCDATETIME(), N'acceptance');
            """,
            command =>
            {
                command.Parameters.AddWithValue("@manualId", ManualId);
                command.Parameters.AddWithValue("@manualCode", ManualCode);
            },
            cancellationToken);
    }

    // Stage 3 的 NULL/Manual 两行分别证明 backfill 修改目标与显式值保留。
    private async Task SeedStageThreeRowsAsync(CancellationToken cancellationToken)
    {
        await ExecuteNonQueryAsync(
            """
            INSERT INTO [dbo].[ExampleRecord]
                ([EID], [CreatedAt], [CreatedBy], [ECODE], [ENAME], [ReviewerUserEID], [RecordSource], [UpdatedAt], [UpdatedBy])
            VALUES
                (@legacyId, SYSUTCDATETIME(), N'acceptance', @legacyCode, N'Legacy migration row', NULL, NULL, SYSUTCDATETIME(), N'acceptance'),
                (@manualId, SYSUTCDATETIME(), N'acceptance', @manualCode, N'Manual migration row', NULL, N'Manual', SYSUTCDATETIME(), N'acceptance');
            """,
            command =>
            {
                command.Parameters.AddWithValue("@legacyId", LegacyId);
                command.Parameters.AddWithValue("@legacyCode", LegacyCode);
                command.Parameters.AddWithValue("@manualId", ManualId);
                command.Parameters.AddWithValue("@manualCode", ManualCode);
            },
            cancellationToken);
    }

    // AFTER UPDATE trigger 仅在本 run 的 NULL->Legacy backfill 命中时抛错，连同该 migration 事务回滚。
    private async Task CreateBackfillFailureTriggerAsync(CancellationToken cancellationToken)
    {
        await ExecuteNonQueryAsync(
            $"""
            CREATE TRIGGER [dbo].[{BackfillFailureTriggerName}]
            ON [dbo].[ExampleRecord]
            AFTER UPDATE
            AS
            BEGIN
                SET NOCOUNT ON;
                IF EXISTS
                (
                    SELECT 1
                    FROM [inserted] AS [i]
                    INNER JOIN [deleted] AS [d] ON [d].[EID] = [i].[EID]
                    WHERE [d].[RecordSource] IS NULL AND [i].[RecordSource] = N'Legacy'
                )
                    THROW 51000, 'ACC140_BACKFILL_FAILURE', 1;
            END;
            """,
            configure: null,
            cancellationToken);
    }

    // INSTEAD OF UPDATE 仅用于 contract failure fixture，使 backfill history 完成但 NULL 仍留待收窄前置检查。
    private async Task CreateContractPreconditionTriggerAsync(CancellationToken cancellationToken)
    {
        await ExecuteNonQueryAsync(
            $"""
            CREATE TRIGGER [dbo].[{ContractPreconditionTriggerName}]
            ON [dbo].[ExampleRecord]
            INSTEAD OF UPDATE
            AS
            BEGIN
                SET NOCOUNT ON;
            END;
            """,
            configure: null,
            cancellationToken);
    }

    // Host dev 失败是本旅程的正式观察；结果只保留稳定码与已脱敏 diagnostic。
    private async Task<DevFailure> RunDevExpectFailureAsync(
        string evidencePath,
        string? expectedDiagnostic,
        CancellationToken cancellationToken)
    {
        var arguments = DevScriptArguments(Path.Combine(_repositoryRoot, "scripts", "dev.ps1"));
        var result = await AcceptanceInfrastructure.RunProcessAsync(
            "pwsh.exe",
            arguments,
            _repositoryRoot,
            _devEnvironment,
            redirectOutput: false,
            cancellationToken);
        CaptureDevRunIdentity();
        var currentResultPath = Path.Combine(_repositoryRoot, ".codex-temp", "dev", "current-result.json");
        if (!File.Exists(currentResultPath))
        {
            throw new InvalidOperationException("Failed managed Host startup did not write current-result.json.");
        }

        using var currentResult = JsonDocument.Parse(await File.ReadAllTextAsync(currentResultPath, cancellationToken));
        var status = ReadString(currentResult.RootElement, "status");
        var failureCode = ReadString(currentResult.RootElement, "failureCode");
        var diagnostic = ReadString(currentResult.RootElement, "diagnostic");
        var expectedObserved = expectedDiagnostic is null
            || diagnostic.Contains(expectedDiagnostic, StringComparison.Ordinal)
            || OwnedHostLogContains(expectedDiagnostic);
        await WriteEvidenceAsync(
            evidencePath,
            new
            {
                script = "dev.ps1",
                exitCode = result.ExitCode,
                status,
                failureCode,
                diagnostic = Sanitize(diagnostic),
                expectedDiagnostic,
                expectedObserved,
                ownerRunId = _lastDevRunId
            },
            cancellationToken);
        if (result.ExitCode == 0 || !string.Equals(status, "failed", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Managed Host unexpectedly reached {status} with exit {result.ExitCode}.");
        }
        if (!expectedObserved)
        {
            throw new InvalidOperationException(
                $"Managed Host failure did not retain the expected diagnostic {expectedDiagnostic}.");
        }
        return new DevFailure(failureCode, expectedDiagnostic ?? diagnostic);
    }

    // 只在 owned dev run 的 Host 日志内查找稳定诊断，不把完整日志或潜在配置写入证据。
    private bool OwnedHostLogContains(string expectedDiagnostic)
    {
        try
        {
            var statePath = Path.Combine(_repositoryRoot, ".codex-temp", "dev", "state.json");
            using var state = JsonDocument.Parse(File.ReadAllText(statePath));
            var logs = state.RootElement.GetProperty("logs");
            foreach (var property in new[] { "hostStdout", "hostStderr" })
            {
                var path = Path.GetFullPath(ReadString(logs, property));
                var devRoot = Path.GetFullPath(Path.Combine(_repositoryRoot, ".codex-temp", "dev", "runs"));
                if (path.StartsWith(devRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                    && File.Exists(path)
                    && File.ReadAllText(path).Contains(expectedDiagnostic, StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }
        catch
        {
            // 缺失或越界日志都按未观察到处理，由调用者形成稳定失败证据。
        }
        return false;
    }

    // Host/Shell Ready 必须来自 managed dev supervisor 的 ready state 与正式 readiness endpoint。
    private async Task StartDevAsync(string evidencePath, CancellationToken cancellationToken)
    {
        await RunDevScriptAsync("dev.ps1", evidencePath, cancellationToken);
        var statePath = Path.Combine(_repositoryRoot, ".codex-temp", "dev", "state.json");
        using var state = JsonDocument.Parse(await File.ReadAllTextAsync(statePath, cancellationToken));
        if (!string.Equals(ReadString(state.RootElement, "status"), "ready", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Development supervisor did not reach ready state.");
        }
        CaptureDevRunIdentity(state.RootElement);
    }

    // 所有脚本执行都固定 managed dev 参数；连接凭据只从继承环境传入，不进入命令或证据。
    private async Task RunDevScriptAsync(
        string scriptName,
        string evidencePath,
        CancellationToken cancellationToken,
        bool allowAlreadyStopped = false)
    {
        var scriptPath = Path.Combine(_repositoryRoot, "scripts", scriptName);
        var isDev = scriptName.Equals("dev.ps1", StringComparison.OrdinalIgnoreCase);
        var result = await AcceptanceInfrastructure.RunProcessAsync(
            "pwsh.exe",
            isDev ? DevScriptArguments(scriptPath) : ["-NoLogo", "-NoProfile", "-File", scriptPath],
            _repositoryRoot,
            isDev ? _devEnvironment : null,
            redirectOutput: !isDev,
            cancellationToken);
        await WriteEvidenceAsync(
            evidencePath,
            new
            {
                script = scriptName,
                exitCode = result.ExitCode,
                standardOutput = Sanitize(result.StandardOutput),
                standardError = Sanitize(result.StandardError)
            },
            cancellationToken);
        if (result.ExitCode != 0)
        {
            if (!isDev && allowAlreadyStopped && SupervisorHasNoOwnedProcesses())
            {
                return;
            }
            if (isDev)
            {
                CaptureDevRunIdentity();
            }
            throw new InvalidOperationException($"{scriptName} failed: {Sanitize(result.StandardError)}");
        }
        if (!allowAlreadyStopped
            && !isDev
            && !result.StandardOutput.Contains("DEV_STOPPED", StringComparison.Ordinal)
            && !result.StandardOutput.Contains("DEV_ALREADY_STOPPED", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("stop-dev.ps1 did not report a recognized terminal state.");
        }
    }

    // Supervisor 在 Host 启动前失败时 processes 为 null；此时没有 owned process 需要 stop。
    private bool SupervisorHasNoOwnedProcesses()
    {
        try
        {
            var statePath = Path.Combine(_repositoryRoot, ".codex-temp", "dev", "state.json");
            using var state = JsonDocument.Parse(File.ReadAllText(statePath));
            var root = state.RootElement;
            return string.Equals(ReadString(root, "status"), "failed", StringComparison.Ordinal)
                && root.TryGetProperty("processes", out var processes)
                && processes.ValueKind is JsonValueKind.Null;
        }
        catch
        {
            return false;
        }
    }

    // 固定超时上限保证历史 migration 与失败恢复仍受 development slice 的时间预算约束。
    private static List<string> DevScriptArguments(string scriptPath) =>
    [
        "-NoLogo", "-NoProfile", "-File", scriptPath,
        "-HostStartupTimeoutSeconds", "600",
        "-ShellStartupTimeoutSeconds", "180",
        "-ShellRequestTimeoutSeconds", "60",
        "-HostEnvironmentName", "Acceptance",
        "-SkipDependencyInstall"
    ];

    // 捕获 supervisor runId 与 PID，形成 teardown 所需的 owned process identity。
    private void CaptureDevRunIdentity()
    {
        try
        {
            var statePath = Path.Combine(_repositoryRoot, ".codex-temp", "dev", "state.json");
            using var state = JsonDocument.Parse(File.ReadAllText(statePath));
            CaptureDevRunIdentity(state.RootElement);
        }
        catch
        {
            // 失败态若无法读取 supervisor identity，teardown 会保守地只校验 catalog ownership。
        }
    }

    // state 中 process pid 与 runId 是唯一可用于安全 stop 的 owned Host/Shell 身份。
    private void CaptureDevRunIdentity(JsonElement state)
    {
        _lastDevRunId = ReadString(state, "runId");
        if (state.TryGetProperty("ports", out var ports))
        {
            _hostPort = ports.TryGetProperty("host", out var host) ? host.GetInt32() : _hostPort;
            _shellPort = ports.TryGetProperty("shell", out var shell) ? shell.GetInt32() : _shellPort;
        }
        if (state.TryGetProperty("processes", out var processes))
        {
            foreach (var name in new[] { "host", "shell" })
            {
                if (processes.TryGetProperty(name, out var process)
                    && process.TryGetProperty("pid", out var pid))
                {
                    var identity = $"{name}:{pid.GetInt32()}:{_lastDevRunId}";
                    if (!_processIdentities.Contains(identity, StringComparer.Ordinal))
                    {
                        _processIdentities.Add(identity);
                    }
                }
            }
        }
    }

    // readiness endpoint 是 Host Ready 的正式外部契约，不以进程存在代替。
    private async Task AssertReadyAsync(CancellationToken cancellationToken)
    {
        using var client = CreateClient(_hostPort);
        using var response = await client.GetAsync("/api/health/ready", cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (response.StatusCode != HttpStatusCode.OK || !body.Contains("ready", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Host readiness returned {(int)response.StatusCode}: {Sanitize(body)}");
        }
    }

    // 迁移后状态一次读取 schema、数据、EF history、统一 history 与 DEFAULT 数量。
    private async Task<ExampleState> ReadExampleStateAsync(CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_applicationConnectionString!);
        await connection.OpenAsync(cancellationToken);
        var nullable = Convert.ToBoolean(await ExecuteScalarAsync(connection, """
            SELECT [columns].[is_nullable]
            FROM [sys].[columns] AS [columns]
            INNER JOIN [sys].[tables] AS [tables] ON [tables].[object_id] = [columns].[object_id]
            WHERE [tables].[name] = N'ExampleRecord' AND [columns].[name] = N'RecordSource';
            """, cancellationToken));
        var defaults = Convert.ToInt32(await ExecuteScalarAsync(connection, """
            SELECT COUNT(*)
            FROM [sys].[default_constraints] AS [defaults]
            INNER JOIN [sys].[columns] AS [columns]
              ON [columns].[object_id] = [defaults].[parent_object_id]
             AND [columns].[column_id] = [defaults].[parent_column_id]
            INNER JOIN [sys].[tables] AS [tables] ON [tables].[object_id] = [columns].[object_id]
            WHERE [tables].[name] = N'ExampleRecord' AND [columns].[name] = N'RecordSource';
            """, cancellationToken));
        var legacy = await ReadNullableStringAsync(connection, LegacyId, cancellationToken);
        var manual = await ReadNullableStringAsync(connection, ManualId, cancellationToken);
        var efHistory = await ReadStringsAsync(
            connection,
            "SELECT [MigrationId] FROM [dbo].[__EFMigrationsHistory] ORDER BY [MigrationId];",
            cancellationToken);
        var unifiedHistory = await ReadStringsAsync(
            connection,
            "SELECT [MigrationId] FROM [dbo].[__NewPMigrationHistory] ORDER BY [MigrationId];",
            cancellationToken);
        return new ExampleState(nullable, defaults, legacy, manual, efHistory, unifiedHistory);
    }

    // API 前只读核对 LocalAdmin 的 active permission catalog，区分 actor fixture 与 CRUD 产品 RED。
    private async Task<AdministratorPermissionSnapshot> ReadAdministratorPermissionSnapshotAsync(
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_applicationConnectionString!);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT DISTINCT [permission].[ECODE]
            FROM [dbo].[IdentityAccount] AS [account]
            INNER JOIN [dbo].[UserRole] AS [userRole] ON [userRole].[UserId] = [account].[EID]
            INNER JOIN [dbo].[Role] AS [role] ON [role].[EID] = [userRole].[RoleId]
            INNER JOIN [dbo].[RolePermission] AS [rolePermission] ON [rolePermission].[RoleId] = [role].[EID]
            INNER JOIN [dbo].[Permission] AS [permission] ON [permission].[EID] = [rolePermission].[PermissionId]
            WHERE [account].[ECODE] = N'admin' AND [role].[IsEnabled] = 1
            ORDER BY [permission].[ECODE];
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var permissions = new List<string>();
        while (await reader.ReadAsync(cancellationToken))
        {
            permissions.Add(reader.GetString(0));
        }
        return new AdministratorPermissionSnapshot(
            permissions,
            permissions.Contains("Example.Record.View", StringComparer.Ordinal),
            permissions.Contains("Example.Record.Create", StringComparer.Ordinal),
            permissions.Contains("Example.Record.Update", StringComparer.Ordinal));
    }

    // 失败/恢复断言同时约束数据、schema、DEFAULT 与双 history，不接受只看异常文本。
    private static void AssertState(
        ExampleState state,
        bool nullableExpected,
        string? expectedLegacySource,
        string expectedManualSource,
        IReadOnlyList<string> expectedMigrations,
        IReadOnlyList<string> unexpectedMigrations)
    {
        if (state.Nullable != nullableExpected
            || state.DefaultConstraints != 0
            || !string.Equals(state.LegacySource, expectedLegacySource, StringComparison.Ordinal)
            || !string.Equals(state.ManualSource, expectedManualSource, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Example schema/data state did not match the required migration checkpoint.");
        }
        foreach (var migration in expectedMigrations)
        {
            if (!state.EfHistory.Contains(migration, StringComparer.Ordinal)
                || !state.UnifiedHistory.Contains(migration, StringComparer.Ordinal))
            {
                throw new InvalidOperationException($"Required migration {migration} was not recorded in both histories.");
            }
        }
        foreach (var migration in unexpectedMigrations)
        {
            if (state.EfHistory.Contains(migration, StringComparer.Ordinal)
                || state.UnifiedHistory.Contains(migration, StringComparer.Ordinal))
            {
                throw new InvalidOperationException($"Rolled-back migration {migration} remained in a history table.");
            }
        }
    }

    // 每个 run 只创建其唯一 catalog；重置场景时仍复用同名且先验 ownership。
    private async Task CreateCatalogAsync(CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_adminConnectionString!);
        await connection.OpenAsync(cancellationToken);
        await using (var exists = connection.CreateCommand())
        {
            exists.CommandText = "SELECT COUNT(*) FROM [sys].[databases] WHERE [name] = @catalog;";
            exists.Parameters.AddWithValue("@catalog", _catalog!);
            if (Convert.ToInt32(await exists.ExecuteScalarAsync(cancellationToken)) != 0)
            {
                _catalogCreated = true;
                _ownershipSnapshot = await ReadOwnershipSnapshotAsync(cancellationToken);
                await VerifyCatalogOwnershipAsync(cancellationToken);
                await WriteEvidenceAsync(
                    "example-required-field/resumed-catalog-cleanup.json",
                    new
                    {
                        status = "ownership-verified",
                        runId = _runId,
                        catalog = _catalog,
                        action = "drop-before-recreate"
                    },
                    cancellationToken);
                await DropCatalogAsync(cancellationToken);
            }
        }

        await using var create = connection.CreateCommand();
        create.CommandText = $"CREATE DATABASE [{_catalog}];";
        await create.ExecuteNonQueryAsync(cancellationToken);
        _catalogCreated = true;
    }

    // DROP 前终止本 catalog 连接，并且只接受严格格式化的 runner catalog 名。
    private async Task DropCatalogAsync(CancellationToken cancellationToken)
    {
        ValidateCatalogName(_catalog!);
        SqlConnection.ClearAllPools();
        await using var connection = new SqlConnection(_adminConnectionString!);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            IF DB_ID(N'{_catalog}') IS NOT NULL
            BEGIN
                ALTER DATABASE [{_catalog}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                DROP DATABASE [{_catalog}];
            END;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
        _catalogCreated = false;
        _ownershipSnapshot = null;
    }

    // Catalog ownership marker 必须与本 run 捕获值完全一致才允许重置或 teardown。
    private async Task VerifyCatalogOwnershipAsync(CancellationToken cancellationToken)
    {
        var expected = _ownershipSnapshot
            ?? throw new InvalidOperationException("Acceptance catalog ownership was not captured.");
        var current = await ReadOwnershipSnapshotAsync(cancellationToken);
        if (current != expected)
        {
            throw new InvalidOperationException("Acceptance catalog ownership marker changed.");
        }
    }

    // 正式 ownership marker 是 catalog 安全清理的事实源。
    private async Task<DatabaseOwnershipSnapshot> ReadOwnershipSnapshotAsync(CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_applicationConnectionString!);
        await connection.OpenAsync(cancellationToken);
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
        return new DatabaseOwnershipSnapshot(
            reader.GetInt32(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5));
    }

    // Teardown 先重验数据库 ownership，再 stop owned supervisor，最后清理 catalog 与资源根。
    private async Task<AcceptanceCleanupResult> TeardownAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (_catalogCreated)
            {
                await VerifyCatalogOwnershipAsync(cancellationToken);
            }
            await _recorder.RecordAsync(
                AcceptanceGateKind.ExampleRequiredField,
                AcceptanceEventType.OwnershipVerified,
                cancellationToken: cancellationToken);
        }
        catch (Exception exception)
        {
            await WriteEvidenceAsync(
                "example-required-field/ownership-failure.json",
                new
                {
                    status = "failed",
                    failureCode = AcceptanceFailureCodes.OwnershipMismatch,
                    diagnostic = Sanitize(exception.Message),
                    resourcesPreserved = true
                },
                cancellationToken);
            await _recorder.RecordAsync(
                AcceptanceGateKind.ExampleRequiredField,
                AcceptanceEventType.OwnershipVerified,
                AcceptanceObservationOutcome.Failed,
                failureCode: AcceptanceFailureCodes.OwnershipMismatch,
                cancellationToken: cancellationToken);
            return new AcceptanceCleanupResult("failed", AcceptanceFailureCodes.OwnershipMismatch);
        }

        try
        {
            await RunDevScriptAsync(
                "stop-dev.ps1",
                "example-required-field/teardown-stop.json",
                cancellationToken,
                allowAlreadyStopped: true);
            if (_catalogCreated)
            {
                await DropCatalogAsync(cancellationToken);
            }
            foreach (var root in new[] { _pluginRoot, _keyRingRoot })
            {
                if (root is not null && Directory.Exists(root))
                {
                    EnsureSafeResourceRoot(root);
                    Directory.Delete(root, recursive: true);
                }
            }
            await WriteCheckpointAsync("cleaned", "teardown", cancellationToken);
            await _recorder.RecordAsync(
                AcceptanceGateKind.ExampleRequiredField,
                AcceptanceEventType.CleanupCompleted,
                cancellationToken: cancellationToken);
            return new AcceptanceCleanupResult("completed", null);
        }
        catch (Exception exception)
        {
            await WriteEvidenceAsync(
                "example-required-field/cleanup-failure.json",
                new
                {
                    status = "failed",
                    failureCode = AcceptanceFailureCodes.CleanupFailed,
                    diagnostic = Sanitize(exception.Message)
                },
                cancellationToken);
            await _recorder.RecordAsync(
                AcceptanceGateKind.ExampleRequiredField,
                AcceptanceEventType.CleanupCompleted,
                AcceptanceObservationOutcome.Failed,
                failureCode: AcceptanceFailureCodes.CleanupFailed,
                cancellationToken: cancellationToken);
            return new AcceptanceCleanupResult("failed", AcceptanceFailureCodes.CleanupFailed);
        }
    }

    // Checkpoint 是同一 child 续接的最小状态，包含 run/catalog/data/process owner 与当前阶段。
    private async Task WriteCheckpointAsync(
        string status,
        string stage,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(_runDirectory, "example-required-field", "checkpoint.json");
        await AcceptanceInfrastructure.WriteJsonAtomicallyAsync(
            path,
            new
            {
                schemaVersion = "1.0",
                runId = _runId,
                status,
                stage,
                catalog = _catalog,
                dataPrefix = _dataPrefix,
                supervisorRunId = _lastDevRunId,
                hostPort = _hostPort == 0 ? (int?)null : _hostPort,
                shellPort = _shellPort == 0 ? (int?)null : _shellPort,
                processIdentities = _processIdentities,
                result = "result.json",
                log = "events.jsonl",
                lastProgressAtUtc = DateTimeOffset.UtcNow
            },
            cancellationToken);
        AddArtifact(path);
    }

    // 场景成功事件与 evidence 事件必须共同出现，reducer 才能形成正式通过结论。
    private async Task RecordScenarioSuccessAsync(
        string scenario,
        string evidencePath,
        object evidence,
        CancellationToken cancellationToken)
    {
        var evidenceRef = await WriteEvidenceAsync(evidencePath, evidence, cancellationToken);
        await _recorder.RecordAsync(
            AcceptanceGateKind.ExampleRequiredField,
            AcceptanceEventType.ScenarioCompleted,
            scenario: scenario,
            cancellationToken: cancellationToken);
        await _recorder.RecordAsync(
            AcceptanceGateKind.ExampleRequiredField,
            AcceptanceEventType.EvidenceRecorded,
            scenario: scenario,
            evidenceRef: evidenceRef,
            cancellationToken: cancellationToken);
    }

    // JSON 证据只能写入本 run 目录，路径越界直接失败。
    private async Task<string> WriteEvidenceAsync(
        string relativePath,
        object value,
        CancellationToken cancellationToken)
    {
        var path = Path.GetFullPath(Path.Combine(
            _runDirectory,
            relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!path.StartsWith(_runDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Evidence path escaped the acceptance run directory.");
        }
        await AcceptanceInfrastructure.WriteJsonAsync(path, value, cancellationToken);
        return AddArtifact(path);
    }

    // 每个证据引用都以 run directory 相对路径存储，避免泄漏机器绝对路径。
    private string AddArtifact(string path)
    {
        var artifact = AcceptanceInfrastructure.RelativeArtifact(_runDirectory, path);
        if (!_artifactRefs.Contains(artifact, StringComparer.Ordinal))
        {
            _artifactRefs.Add(artifact);
        }
        return artifact;
    }

    // 证据扫描覆盖 run directory 全部文件，不允许连接密码或临时身份凭据残留。
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
            AcceptanceGateKind.ExampleRequiredField,
            AcceptanceEventType.SecretScanCompleted,
            leaked ? AcceptanceObservationOutcome.Failed : AcceptanceObservationOutcome.Succeeded,
            failureCode: leaked ? AcceptanceFailureCodes.SecretDetected : null,
            cancellationToken: cancellationToken);
    }

    // 连接串相关 token 只用于证据反向扫描，不进入日志。
    private void AddConnectionSecrets(string configuredConnectionString, string password)
    {
        _secretTokens.Add(configuredConnectionString);
        _secretTokens.Add(password);
        try
        {
            var builder = new SqlConnectionStringBuilder(configuredConnectionString);
            _secretTokens.Add(builder.UserID);
        }
        catch
        {
            // Resolver 已负责连接串合法性；这里只跳过无法拆分的额外 token。
        }
    }

    // runner resource root 只能是当前 run/resources 的直接子路径。
    private void EnsureSafeResourceRoot(string root)
    {
        var resourceRoot = Path.GetFullPath(Path.Combine(_runDirectory, "resources"));
        var full = Path.GetFullPath(root);
        if (!full.StartsWith(resourceRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Acceptance resource root escaped the owned run directory.");
        }
    }

    // Catalog 格式包含 gate owner 与完整随机 runId，拒绝宽泛 DROP 目标。
    private static void ValidateCatalogName(string catalog)
    {
        if (!CatalogPattern.IsMatch(catalog))
        {
            throw new InvalidOperationException("Acceptance catalog name was not ownership-safe.");
        }
    }

    // SQL helper 始终连接已固定的 application catalog，并支持参数化数据输入。
    private async Task ExecuteNonQueryAsync(
        string sql,
        Action<SqlCommand>? configure,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_applicationConnectionString!);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        configure?.Invoke(command);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    // runner trigger 名为内部固定值，只删除该 run 自己创建的数据库对象。
    private Task DropTriggerAsync(string triggerName, CancellationToken cancellationToken) =>
        ExecuteNonQueryAsync(
            $"DROP TRIGGER IF EXISTS [dbo].[{triggerName}];",
            configure: null,
            cancellationToken);

    // Scalar helper 把 DBNull 规范化为 null，便于显式断言 NULL 保留。
    private static async Task<object?> ExecuteScalarAsync(
        SqlConnection connection,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is DBNull ? null : result;
    }

    // 按 EID 读取 RecordSource，保留数据库 NULL 语义。
    private static async Task<string?> ReadNullableStringAsync(
        SqlConnection connection,
        string id,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT [RecordSource] FROM [dbo].[ExampleRecord] WHERE [EID] = @id;";
        command.Parameters.AddWithValue("@id", id);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is null or DBNull ? null : Convert.ToString(result);
    }

    // 双 history 以稳定 MigrationId 排序读取，供 checkpoint 与 restart 比较。
    private static async Task<string[]> ReadStringsAsync(
        SqlConnection connection,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var values = new List<string>();
        while (await reader.ReadAsync(cancellationToken))
        {
            values.Add(reader.GetString(0));
        }
        return values.ToArray();
    }

    // HttpClient 由后续 API 场景显式管理 cookie。
    private static HttpClient CreateClient(int port) => new()
    {
        BaseAddress = new Uri($"http://127.0.0.1:{port}"),
        Timeout = TimeSpan.FromMinutes(5)
    };

    // Bootstrap 临时凭据只使用一次，随后切换到本 run 的一次性管理员密码。
    private async Task PrepareAdministratorPasswordAsync(
        string temporaryCredential,
        CancellationToken cancellationToken)
    {
        using var client = CreateClient(_hostPort);
        using var login = await LoginAsync(client, "admin", temporaryCredential, cancellationToken);
        _adminPassword = CreateAdministratorPassword();
        _secretTokens.Add(_adminPassword);
        _ = await ChangePasswordAsync(
            client,
            temporaryCredential,
            _adminPassword,
            ReadLong(login.RootElement, "revision"),
            cancellationToken);
    }

    // API 与 restart 场景每次创建新 client 并重新登录，避免旧 session 遮蔽恢复缺陷。
    private async Task LoginAdminAsync(HttpClient client, CancellationToken cancellationToken)
    {
        using var _ = await LoginAsync(
            client,
            "admin",
            _adminPassword ?? throw new InvalidOperationException("Administrator password was not prepared."),
            cancellationToken);
    }

    // 登录响应必须签发指定 acceptance cookie，凭据只存在请求内存中。
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

    // 密码切换保留 optimistic revision 并更新 authentication cookie。
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

    // Secure acceptance cookie 由 loopback client 显式回放，不放宽 Host cookie 策略。
    private static void CaptureAuthenticationCookie(HttpClient client, HttpResponseMessage response)
    {
        var prefix = $"{AuthenticationCookieName}=";
        var cookie = response.Headers.TryGetValues("Set-Cookie", out var values)
            ? values.Select(static value => value.Split(';', 2)[0])
                .SingleOrDefault(value => value.StartsWith(prefix, StringComparison.Ordinal))
            : null;
        if (string.IsNullOrWhiteSpace(cookie))
        {
            throw new InvalidOperationException("Identity response did not issue the expected authentication cookie.");
        }
        client.DefaultRequestHeaders.Remove("Cookie");
        client.DefaultRequestHeaders.TryAddWithoutValidation("Cookie", cookie);
    }

    // 所有写 API 先取得一次 antiforgery token，再在同一 client session 发出请求。
    private static async Task<HttpResponseMessage> SendJsonWithAntiforgeryAsync(
        HttpClient client,
        HttpMethod method,
        string path,
        object payload,
        CancellationToken cancellationToken)
    {
        using var tokenResponse = await client.GetAsync("/api/auth/antiforgery-token", cancellationToken);
        var tokenBody = await tokenResponse.Content.ReadAsStringAsync(cancellationToken);
        EnsureStatus(tokenResponse, HttpStatusCode.OK, tokenBody, "issue antiforgery token");
        using var token = JsonDocument.Parse(tokenBody);
        using var request = new HttpRequestMessage(method, path)
        {
            Content = JsonContent.Create(payload)
        };
        request.Headers.TryAddWithoutValidation("X-CSRF-TOKEN", ReadString(token.RootElement, "requestToken"));
        return await client.SendAsync(request, cancellationToken);
    }

    // HTTP status 不符时只报告响应状态和短诊断，调用者负责先经 Sanitize 进入证据。
    private static void EnsureStatus(
        HttpResponseMessage response,
        HttpStatusCode expected,
        string body,
        string check)
    {
        if (response.StatusCode != expected)
        {
            throw new InvalidOperationException(
                $"{check} returned {(int)response.StatusCode}, expected {(int)expected}: {body}");
        }
    }

    // 错误契约同时固定 HTTP status 与稳定 code。
    private static void EnsureProblem(
        HttpResponseMessage response,
        string body,
        HttpStatusCode expectedStatus,
        string expectedCode,
        string check)
    {
        EnsureStatus(response, expectedStatus, body, check);
        using var json = JsonDocument.Parse(body);
        var code = ReadString(json.RootElement, "code");
        if (!string.Equals(code, expectedCode, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{check} returned {code}, expected {expectedCode}.");
        }
    }

    // 标准 CRUD detail/create/update envelope 中正式字段统一位于 fields 对象。
    private static void AssertField(string body, string field, string expected, string check)
    {
        using var json = JsonDocument.Parse(body);
        var fields = ReadObject(json.RootElement, "fields");
        var actual = ReadString(fields, field);
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{check} returned {field}={actual}, expected {expected}.");
        }
    }

    // 正式 Standard CRUD mutation detail 的主键位于 fields.EID，不接受旧式顶层 id 猜测。
    internal static string ReadCreatedRecordId(string body)
    {
        using var json = JsonDocument.Parse(body);
        var id = ReadString(ReadObject(json.RootElement, "fields"), "EID");
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new InvalidOperationException("Standard CRUD create response omitted fields.EID.");
        }
        return id;
    }

    // 一次性管理员密码仅存在进程内，不从会写入 evidence 的 run identity 派生。
    internal static string CreateAdministratorPassword()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    // Restart/no-op 比较只看三步相关 schema/data/history，不受 identity bootstrap 其他 stream 影响。
    private static void AssertEquivalentMigrationState(ExampleState before, ExampleState after, string check)
    {
        if (before.Nullable != after.Nullable
            || before.DefaultConstraints != after.DefaultConstraints
            || !string.Equals(before.LegacySource, after.LegacySource, StringComparison.Ordinal)
            || !string.Equals(before.ManualSource, after.ManualSource, StringComparison.Ordinal)
            || !before.EfHistory.SequenceEqual(after.EfHistory, StringComparer.Ordinal)
            || !before.UnifiedHistory.SequenceEqual(after.UnifiedHistory, StringComparer.Ordinal))
        {
            throw new InvalidOperationException($"{check} replayed migration or changed Example required-field state.");
        }
    }

    // 登录成功以离开 /login 且退出登录入口出现为准。
    private static async Task WaitForPostLoginNavigationAsync(IPage page)
    {
        await page.WaitForURLAsync(
            url => !url.Contains("/login", StringComparison.OrdinalIgnoreCase),
            new PageWaitForURLOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.GetByText("退出登录", new PageGetByTextOptions { Exact = true }).WaitForAsync();
    }

    // Locator 文本断言在真实 DOM 稳定后执行，失败时只暴露期望标签。
    private static async Task AssertLocatorContainsAsync(ILocator locator, string expected, string check)
    {
        var matchingLocator = locator.Filter(new LocatorFilterOptions { HasText = expected });
        await matchingLocator.WaitForAsync();
        if (!(await matchingLocator.InnerTextAsync()).Contains(expected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Browser assertion failed for {check}.");
        }
    }

    // 正式 runner 优先采用显式 Chromium 配置，其次使用本机受支持 Chrome。
    private static string ResolveChromePath()
    {
        var configured = Environment.GetEnvironmentVariable("NEWP_ACCEPTANCE_CHROMIUM_EXECUTABLE");
        var candidates = new[]
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
        };
        return candidates.FirstOrDefault(static path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
            ?? throw new InvalidOperationException("No Chromium executable was found for the Example browser observer.");
    }

    // 任意 evidence diagnostic 都先替换 runner 知道的凭据 token。
    private string Sanitize(string? value)
    {
        var sanitized = value ?? string.Empty;
        foreach (var token in _secretTokens.Where(static token => !string.IsNullOrWhiteSpace(token)))
        {
            sanitized = sanitized.Replace(token, "[REDACTED]", StringComparison.Ordinal);
        }
        return sanitized;
    }

    // JSON helper 只读取字符串型稳定字段，缺失时返回空串供调用者明确失败。
    private static string ReadString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind is JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    // JSON object 缺失时立即失败，避免把响应 envelope 漂移误判为空值。
    private static JsonElement ReadObject(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind is JsonValueKind.Object
            ? value
            : throw new InvalidOperationException($"JSON response omitted object {property}.");

    // Revision 必须保持整数契约。
    private static long ReadLong(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.TryGetInt64(out var result)
            ? result
            : throw new InvalidOperationException($"JSON response omitted integer {property}.");

    // 场景到稳定 failure code 的映射保持单一，避免实现细节泄漏给 reducer。
    private static string FailureCodeFor(string scenario) => scenario switch
    {
        PreflightScenario => AcceptanceFailureCodes.ExampleRequiredFieldPreflightFailed,
        MigrationScenario => AcceptanceFailureCodes.ExampleRequiredFieldMigrationFailed,
        BackfillRecoveryScenario => AcceptanceFailureCodes.ExampleRequiredFieldBackfillRecoveryFailed,
        ContractPreconditionScenario => AcceptanceFailureCodes.ExampleRequiredFieldContractPreconditionFailed,
        ApiScenario => AcceptanceFailureCodes.ExampleRequiredFieldApiFailed,
        BrowserScenario => AcceptanceFailureCodes.ExampleRequiredFieldBrowserFailed,
        RestartScenario => AcceptanceFailureCodes.ExampleRequiredFieldRestartFailed,
        _ => AcceptanceFailureCodes.ExampleRequiredFieldPreflightFailed
    };

    private string LegacyId => $"{_dataPrefix}-legacy";
    private string LegacyCode => $"{_dataPrefix}-LEGACY";
    private string ManualId => $"{_dataPrefix}-manual";
    private string ManualCode => $"{_dataPrefix}-MANUAL";
    private string BackfillFailureTriggerName => $"trg_ACC140_Backfill_{_runId[..8]}";
    private string ContractPreconditionTriggerName => $"trg_ACC140_Contract_{_runId[..8]}";

    private sealed record DevFailure(string FailureCode, string Diagnostic);
    private sealed record ExampleState(
        bool Nullable,
        int DefaultConstraints,
        string? LegacySource,
        string? ManualSource,
        IReadOnlyList<string> EfHistory,
        IReadOnlyList<string> UnifiedHistory);
    private sealed record AdministratorPermissionSnapshot(
        IReadOnlyList<string> PermissionCodes,
        bool HasExampleView,
        bool HasExampleCreate,
        bool HasExampleUpdate);
    private sealed record DatabaseOwnershipSnapshot(
        int Version,
        string RepositoryId,
        string WorktreeId,
        string Provider,
        string ServerFingerprint,
        string CatalogFingerprint);
}
