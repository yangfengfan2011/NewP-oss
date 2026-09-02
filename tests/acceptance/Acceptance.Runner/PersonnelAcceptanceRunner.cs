using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
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
    private const string TabletScreenshotName = "browser-tablet.png";
    private const string NarrowScreenshotName = "browser-narrow.png";
    private const string PersonnelBirthDate = "2000-02-29";
    private const string PersonnelBirthDateDisplay = "2000/2/29";
    private const string PersonnelStartedAtUtc = "2030-01-02T00:04:05Z";
    private const string PersonnelStartedAtDisplay = "2030/1/2 08:04:05";
    private const string PersonnelEndedAtUtc = "2030-06-30T09:30:00Z";
    private const string PersonnelEndedAtDisplay = "2030/6/30 17:30:00";
    internal static IReadOnlyList<(string Label, string Value)> PersonnelTemporalFormInputs { get; } =
    [
        ("出生日期", PersonnelBirthDate),
        ("任职开始时间", "2030-01-02 08:04:05"),
        ("任职结束时间", "2030-06-30 17:30:00")
    ];
    private static readonly JsonSerializerOptions WebJsonOptions = new(JsonSerializerDefaults.Web);
    private readonly List<string> _artifactRefs = [];
    private readonly List<string> _processIdentities = [];
    private readonly List<string> _apiAssertions = [];
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
    private PositionData? _position;
    private string? _adminPassword;
    private string? _readOnlyECode;
    private string? _readOnlyPassword;
    private long? _readOnlyRevision;
    private string? _unauthorizedECode;
    private string? _unauthorizedPassword;
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
        var build = await AcceptanceInfrastructure.RunProcessAsync(
            "dotnet",
            ["build", "apps/host-api/HostApi.csproj", "--configuration", "Release", "--no-restore", "--nologo"],
            _repositoryRoot,
            cancellationToken: cancellationToken);
        if (build.ExitCode != 0)
        {
            throw new InvalidOperationException($"HostApi release build failed: {Sanitize(build.StandardError)}");
        }
        await WriteEvidenceAsync(
            "personnel/preflight-build.json",
            new { command = "dotnet build HostApi", exitCode = build.ExitCode },
            cancellationToken);
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

        _readOnlyECode = $"acceptance.readonly.{_runId[..8]}";
        using (var created = await SendJsonWithAntiforgeryAsync(
                   administrator,
                   HttpMethod.Post,
                   "/api/identity/accounts",
                   new { ecode = _readOnlyECode, ename = "Personnel Acceptance Read Only" },
                   cancellationToken))
        {
            var createdBody = await created.Content.ReadAsStringAsync(cancellationToken);
            EnsureStatus(created, HttpStatusCode.Created, createdBody, "create read-only identity account");
            using var createdJson = JsonDocument.Parse(createdBody);
            var account = ReadObject(createdJson.RootElement, "account");
            var readOnlyAccountEid = ReadString(account, "eid");
            var temporaryCredential = ReadString(createdJson.RootElement, "temporaryCredential");
            RegisterSecret(temporaryCredential);
            using var readOnly = CreateClient(_hostPort);
            using var restrictedReadOnlyLogin = await LoginAsync(
                readOnly,
                _readOnlyECode,
                temporaryCredential,
                cancellationToken);
            _readOnlyPassword = $"Read-{_runId}-Pass";
            RegisterSecret(_readOnlyPassword);
            _readOnlyRevision = await ChangePasswordAsync(
                readOnly,
                temporaryCredential,
                _readOnlyPassword,
                ReadLong(restrictedReadOnlyLogin.RootElement, "revision"),
                cancellationToken);

            using var response = await SendJsonWithAntiforgeryAsync(
                administrator,
                HttpMethod.Post,
                "/api/acceptance/personnel/prepare",
                new { readOnlyAccountEid },
                cancellationToken,
                request => request.Headers.Add("X-NewP-Acceptance-Secret", fixtureSecret));
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

    // API 场景通过正式认证、授权与业务 endpoint 建立浏览器旅程所需数据，并锁定失败语义。
    private async Task RunApiScenarioAsync(CancellationToken cancellationToken)
    {
        var fixture = _fixture ?? throw new InvalidOperationException("Personnel fixture was not prepared.");
        var checks = new List<object>();
        (_unauthorizedECode, _unauthorizedPassword) = await CreateUnauthorizedActorAsync(cancellationToken);

        using (var anonymous = CreateClient(_hostPort))
        using (var response = await anonymous.GetAsync("/api/organization/persons", cancellationToken))
        {
            EnsureStatus(response, HttpStatusCode.Unauthorized, await response.Content.ReadAsStringAsync(cancellationToken), "anonymous list");
            checks.Add(new { check = "anonymous-list", status = 401 });
        }

        using (var readOnly = CreateClient(_hostPort))
        {
            using var login = await LoginAsync(
                readOnly,
                _readOnlyECode ?? throw new InvalidOperationException("Read-only ECODE was not prepared."),
                _readOnlyPassword ?? throw new InvalidOperationException("Read-only password was not prepared."),
                cancellationToken);
            using var allowed = await readOnly.GetAsync("/api/organization/persons?pageIndex=1&pageSize=20", cancellationToken);
            EnsureStatus(allowed, HttpStatusCode.OK, await allowed.Content.ReadAsStringAsync(cancellationToken), "read-only list");
            checks.Add(new { check = "read-only-list", status = 200 });
            using var denied = await SendJsonWithAntiforgeryAsync(
                readOnly,
                HttpMethod.Post,
                "/api/organization/persons",
                PersonPayload("ACC-DENIED", "Denied Person", fixture.CompanyId, fixture.DepartmentId),
                cancellationToken);
            EnsureStatus(denied, HttpStatusCode.Forbidden, await denied.Content.ReadAsStringAsync(cancellationToken), "read-only create");
            checks.Add(new { check = "read-only-create", status = 403 });
        }

        using var admin = CreateClient(_hostPort);
        await LoginAdminAsync(admin, cancellationToken);

        var unaffiliated = await CreatePersonAsync(
            admin,
            $"ACC-U-{_runId[..8]}",
            "Unaffiliated Person",
            null,
            null,
            cancellationToken);
        checks.Add(new { check = "create-without-affiliation", status = 201 });

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

        await AssertPersonErrorAsync(admin, "partial-affiliation-missing-company", PersonPayload("ACC-E01", "Missing Company", null, fixture.DepartmentId), "Organization.Person.CompanyRequired", checks, cancellationToken);
        await AssertPersonErrorAsync(admin, "partial-affiliation-missing-department", PersonPayload("ACC-E02", "Missing Department", fixture.CompanyId, null), "Organization.Person.DepartmentRequired", checks, cancellationToken);
        await AssertPersonErrorAsync(admin, "company-not-found", PersonPayload("ACC-E03", "Unknown Company", "Company_missing", fixture.DepartmentId), "Organization.Person.CompanyNotFound", checks, cancellationToken);
        await AssertPersonErrorAsync(admin, "department-not-found", PersonPayload("ACC-E04", "Unknown Department", fixture.CompanyId, "Department_missing"), "Organization.Person.DepartmentNotFound", checks, cancellationToken);
        await AssertPersonErrorAsync(admin, "department-company-mismatch", PersonPayload("ACC-E05", "Mismatch Department", fixture.CompanyId, secondDepartment.Id), "Organization.Person.DepartmentCompanyMismatch", checks, cancellationToken);

        await PutCompanyAsync(admin, fixture.CompanyId, fixture.CompanyName, false, cancellationToken);
        await AssertPersonErrorAsync(admin, "company-disabled", PersonPayload("ACC-E06", "Disabled Company", fixture.CompanyId, fixture.DepartmentId), "Organization.Person.CompanyDisabled", checks, cancellationToken);
        await PutCompanyAsync(admin, fixture.CompanyId, fixture.CompanyName, true, cancellationToken);
        await PutDepartmentAsync(admin, fixture.DepartmentId, fixture.CompanyId, fixture.DepartmentName, false, cancellationToken);
        await AssertPersonErrorAsync(admin, "department-disabled", PersonPayload("ACC-E07", "Disabled Department", fixture.CompanyId, fixture.DepartmentId), "Organization.Person.DepartmentDisabled", checks, cancellationToken);
        await PutDepartmentAsync(admin, fixture.DepartmentId, fixture.CompanyId, fixture.DepartmentName, true, cancellationToken);

        var personNo = $"ACC-{_runId[..10]}";
        var personName = $"Personnel Gate Person {_runId[..8]}";
        var displayCompanionPayload = PersonPayload(
            personNo,
            personName,
            fixture.CompanyId,
            fixture.DepartmentId);
        displayCompanionPayload["CompanyId_DisplayValue"] = fixture.CompanyName;
        await AssertCanonicalDisplayCompanionWriteRejectedAsync(
            admin,
            HttpMethod.Post,
            "/api/organization/persons",
            displayCompanionPayload,
            "canonical-display-companion-create-read-only",
            checks,
            cancellationToken);
        var createdPerson = await CreatePersonAsync(
            admin,
            personNo,
            personName,
            fixture.CompanyId,
            fixture.DepartmentId,
            cancellationToken,
            fixture.CompanyName,
            PersonnelBirthDate,
            PersonnelStartedAtUtc,
            PersonnelEndedAtUtc);
        checks.Add(new { check = "create-with-company-display-envelope", status = 201 });
        var updatedName = $"{personName} Updated";
        var updateDisplayCompanionPayload = PersonPayload(
            personNo,
            $"{personName} Rejected Update",
            fixture.CompanyId,
            fixture.DepartmentId);
        updateDisplayCompanionPayload["Revision"] = createdPerson.Revision;
        updateDisplayCompanionPayload["CompanyId_DisplayValue"] = fixture.CompanyName;
        await AssertCanonicalDisplayCompanionWriteRejectedAsync(
            admin,
            HttpMethod.Put,
            $"/api/organization/persons/{Uri.EscapeDataString(createdPerson.Id)}",
            updateDisplayCompanionPayload,
            "canonical-display-companion-update-read-only",
            checks,
            cancellationToken);
        await AssertPersonMutationStateAsync(
            admin,
            createdPerson.Id,
            personName,
            fixture.CompanyId,
            fixture.CompanyName,
            createdPerson.Revision,
            cancellationToken);
        var updatePayload = PersonPayload(personNo, updatedName, fixture.CompanyId, fixture.DepartmentId);
        updatePayload["Revision"] = createdPerson.Revision;
        using (var updated = await SendJsonWithAntiforgeryAsync(
                   admin,
                   HttpMethod.Put,
                   $"/api/organization/persons/{Uri.EscapeDataString(createdPerson.Id)}",
                   updatePayload,
                   cancellationToken))
        {
            var updatedBody = await updated.Content.ReadAsStringAsync(cancellationToken);
            EnsureStatus(updated, HttpStatusCode.OK, updatedBody, "admin edit person");
            AssertPersonCompanyDisplayEnvelope(updatedBody, fixture.CompanyId, fixture.CompanyName);
            var updatedPerson = ReadPerson(updatedBody, personNo, updatedName, "admin edit person");
            if (!string.Equals(updatedPerson.CreatedAt, createdPerson.CreatedAt, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Personnel update response changed the create audit timestamp.");
            }
            _person = updatedPerson;
        }
        var person = _person;

        using (var detail = await admin.GetAsync(
                   $"/api/organization/persons/{Uri.EscapeDataString(person.Id)}",
                   cancellationToken))
        {
            var detailBody = await detail.Content.ReadAsStringAsync(cancellationToken);
            EnsureStatus(detail, HttpStatusCode.OK, detailBody, "admin detail");
            AssertPersonBody(detailBody, person, fixture, "admin detail");
        }
        await AssertApiAuditCheckpointAfterRestartAsync(cancellationToken);
        await LoginAdminAsync(admin, cancellationToken);

        using (var anonymous = CreateClient(_hostPort))
        {
            using var deniedDetail = await anonymous.GetAsync(
                $"/api/organization/persons/{Uri.EscapeDataString(person.Id)}",
                cancellationToken);
            EnsureStatus(
                deniedDetail,
                HttpStatusCode.Unauthorized,
                await deniedDetail.Content.ReadAsStringAsync(cancellationToken),
                "anonymous detail");
            using var deniedStatus = await anonymous.PutAsJsonAsync(
                $"/api/organization/persons/{Uri.EscapeDataString(person.Id)}/status",
                new { isEnabled = false, expectedRevision = person.Revision, reason = "Anonymous disable" },
                cancellationToken);
            EnsureStatus(
                deniedStatus,
                HttpStatusCode.Unauthorized,
                await deniedStatus.Content.ReadAsStringAsync(cancellationToken),
                "anonymous disable");
            checks.Add(new { check = "detail-disable-unauthorized", status = 401 });
            _apiAssertions.Add("detail-disable-unauthorized-401");
        }

        using (var readOnly = CreateClient(_hostPort))
        {
            using var login = await LoginAsync(
                readOnly,
                _readOnlyECode ?? throw new InvalidOperationException("Read-only ECODE was not prepared."),
                _readOnlyPassword ?? throw new InvalidOperationException("Read-only password was not prepared."),
                cancellationToken);
            using var allowedDetail = await readOnly.GetAsync(
                $"/api/organization/persons/{Uri.EscapeDataString(person.Id)}",
                cancellationToken);
            EnsureStatus(
                allowedDetail,
                HttpStatusCode.OK,
                await allowedDetail.Content.ReadAsStringAsync(cancellationToken),
                "read-only detail");
            using var deniedStatus = await SendJsonWithAntiforgeryAsync(
                readOnly,
                HttpMethod.Put,
                $"/api/organization/persons/{Uri.EscapeDataString(person.Id)}/status",
                new { isEnabled = false, expectedRevision = person.Revision, reason = "Read-only disable" },
                cancellationToken);
            EnsureStatus(
                deniedStatus,
                HttpStatusCode.Forbidden,
                await deniedStatus.Content.ReadAsStringAsync(cancellationToken),
                "read-only disable");
            checks.Add(new { check = "detail-edit-permission", status = 200 });
            checks.Add(new { check = "detail-disable-forbidden", status = 403 });
            _apiAssertions.Add("detail-disable-forbidden-403");
        }

        using (var invalidReason = await SendJsonWithAntiforgeryAsync(
                   admin,
                   HttpMethod.Put,
                   $"/api/organization/persons/{Uri.EscapeDataString(person.Id)}/status",
                   new { isEnabled = false, expectedRevision = person.Revision, reason = "" },
                   cancellationToken))
        {
            var invalidReasonBody = await invalidReason.Content.ReadAsStringAsync(cancellationToken);
            EnsureStatus(invalidReason, HttpStatusCode.BadRequest, invalidReasonBody, "empty disable reason");
            AssertContains(invalidReasonBody, "ORGANIZATION_PERSON_LIFECYCLE_REASON_INVALID", "disable business failure code");
            checks.Add(new { check = "detail-disable-business-failure", status = 400 });
            _apiAssertions.Add("detail-disable-business-failure-400");
        }

        using var list = await admin.GetAsync($"/api/organization/persons?keyword={Uri.EscapeDataString(personNo)}&pageIndex=1&pageSize=20", cancellationToken);
        var listBody = await list.Content.ReadAsStringAsync(cancellationToken);
        EnsureStatus(list, HttpStatusCode.OK, listBody, "admin list");
        if (!listBody.Contains(person.Id, StringComparison.Ordinal) || !listBody.Contains(personNo, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Personnel list did not contain the created person.");
        }

        using (var rangedList = await admin.GetAsync(
                   $"/api/organization/persons?birthDateFrom={PersonnelBirthDate}&birthDateTo={PersonnelBirthDate}&pageIndex=1&pageSize=20&sortBy=BirthDate&sortDirection=asc",
                   cancellationToken))
        {
            var rangedListBody = await rangedList.Content.ReadAsStringAsync(cancellationToken);
            EnsureStatus(rangedList, HttpStatusCode.OK, rangedListBody, "inclusive birth date range list");
            using var rangedJson = JsonDocument.Parse(rangedListBody);
            var rangedItems = ReadArray(rangedJson.RootElement, "items");
            if (ReadInt(rangedJson.RootElement, "total") != 1
                || rangedItems.GetArrayLength() != 1
                || !string.Equals(ReadString(rangedItems[0], "EID"), person.Id, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Personnel inclusive birth date range did not return the one matching person.");
            }
            checks.Add(new { check = "calendar-date-range-list-count-sort", status = 200 });
        }

        for (var index = 0; index < 19; index++)
        {
            await CreatePersonAsync(
                admin,
                $"ACC-PG-{_runId[..6]}-{index:00}",
                $"Pagination Person {index:00}",
                fixture.CompanyId,
                fixture.DepartmentId,
                cancellationToken);
        }

        using (var secondPage = await admin.GetAsync(
                   "/api/organization/persons?pageIndex=2&pageSize=20&sortBy=PersonNo&sortDirection=asc",
                   cancellationToken))
        {
            var secondPageBody = await secondPage.Content.ReadAsStringAsync(cancellationToken);
            EnsureStatus(secondPage, HttpStatusCode.OK, secondPageBody, "server pagination and sorting");
            using var pageJson = JsonDocument.Parse(secondPageBody);
            if (ReadInt(pageJson.RootElement, "total") < 21 || ReadArray(pageJson.RootElement, "items").GetArrayLength() == 0)
            {
                throw new InvalidOperationException("Personnel server pagination did not expose a populated second page.");
            }
            checks.Add(new { check = "server-pagination-sorting-stable-order", status = 200 });
        }

        var exportColumns = new[]
        {
            new { key = "PersonNo", field = "PersonNo", title = "人员编号" },
            new { key = "ENAME", field = "ENAME", title = "姓名" },
            new { key = "BirthDate", field = "BirthDate", title = "出生日期" },
            new { key = "CompanyId", field = "CompanyId", title = "公司" },
            new { key = "EmploymentStartedAt", field = "EmploymentStartedAt", title = "入职时间" },
            new { key = "EmploymentEndedAt", field = "EmploymentEndedAt", title = "离职时间" }
        };
        foreach (var mode in new[] { "current", "all" })
        {
            using var exported = await SendJsonWithAntiforgeryAsync(
                admin,
                HttpMethod.Post,
                "/api/organization/persons/export",
                new
                {
                    mode,
                    columns = exportColumns,
                    query = new
                    {
                        pageIndex = 1,
                        pageSize = 20,
                        sortBy = "BirthDate",
                        sortDirection = "asc",
                        filters = new { birthDateFrom = PersonnelBirthDate, birthDateTo = PersonnelBirthDate }
                    }
                },
                cancellationToken);
            var bytes = await exported.Content.ReadAsByteArrayAsync(cancellationToken);
            if (exported.StatusCode != HttpStatusCode.OK
                || bytes.Length == 0
                || !string.Equals(
                    exported.Content.Headers.ContentType?.MediaType,
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Personnel {mode} export did not return a non-empty XLSX file.");
            }
            var sheetXml = ReadXlsxSheetXml(bytes, mode);
            AssertContains(sheetXml, fixture.CompanyName, $"company display name in {mode} export");
            AssertContains(sheetXml, PersonnelBirthDateDisplay, $"birth date display value in {mode} export");
            AssertContains(sheetXml, PersonnelStartedAtDisplay, $"employment start display value in {mode} export");
            AssertContains(sheetXml, PersonnelEndedAtDisplay, $"employment end display value in {mode} export");
            AssertDoesNotContain(sheetXml, fixture.CompanyId, $"company raw id in {mode} export");
            AssertDoesNotContain(sheetXml, PersonnelStartedAtUtc, $"employment start raw value in {mode} export");
            AssertDoesNotContain(sheetXml, PersonnelEndedAtUtc, $"employment end raw value in {mode} export");
            AssertDoesNotContain(sheetXml, "CompanyName", $"legacy company alias in {mode} export");
            checks.Add(new { check = $"export-{mode}-with-filter-sort-column-order", status = 200, bytes = bytes.Length });
        }

        using var disabled = await SendJsonWithAntiforgeryAsync(
            admin,
            HttpMethod.Put,
            $"/api/organization/persons/{Uri.EscapeDataString(person.Id)}/status",
            new { isEnabled = false, expectedRevision = person.Revision, reason = "Personnel acceptance disable" },
            cancellationToken);
        var disabledBody = await disabled.Content.ReadAsStringAsync(cancellationToken);
        EnsureStatus(disabled, HttpStatusCode.OK, disabledBody, "disable person");
        using var disabledJson = JsonDocument.Parse(disabledBody);
        var disabledRevision = ReadLong(disabledJson.RootElement, "revision");

        using (var conflict = await SendJsonWithAntiforgeryAsync(
                   admin,
                   HttpMethod.Put,
                   $"/api/organization/persons/{Uri.EscapeDataString(person.Id)}/status",
                   new { isEnabled = true, expectedRevision = person.Revision, reason = "Stale personnel acceptance enable" },
                   cancellationToken))
        {
            EnsureStatus(conflict, HttpStatusCode.Conflict, await conflict.Content.ReadAsStringAsync(cancellationToken), "stale lifecycle update");
            checks.Add(new { check = "detail-disable-conflict", status = 409 });
        }

        using var enabled = await SendJsonWithAntiforgeryAsync(
            admin,
            HttpMethod.Put,
            $"/api/organization/persons/{Uri.EscapeDataString(person.Id)}/status",
            new { isEnabled = true, expectedRevision = disabledRevision, reason = "Personnel acceptance enable" },
            cancellationToken);
        var enabledBody = await enabled.Content.ReadAsStringAsync(cancellationToken);
        EnsureStatus(enabled, HttpStatusCode.OK, enabledBody, "enable person");
        using var enabledJson = JsonDocument.Parse(enabledBody);
        var enabledRevision = ReadLong(enabledJson.RootElement, "revision");
        _person = person with { Revision = enabledRevision };
        checks.Add(new { check = "detail-disable-success", status = 200 });

        using (var bound = await SendJsonWithAntiforgeryAsync(
                   admin,
                   HttpMethod.Put,
                   $"/api/organization/persons/{Uri.EscapeDataString(person.Id)}/user-binding",
                   new
                   {
                       accountEid = fixture.ReadOnlyUserId,
                       expectedRevision = _readOnlyRevision
                           ?? throw new InvalidOperationException("Read-only account revision was not prepared."),
                       reason = "Personnel acceptance permanent binding"
                   },
                   cancellationToken))
        {
            EnsureStatus(bound, HttpStatusCode.OK, await bound.Content.ReadAsStringAsync(cancellationToken), "bind person account");
        }
        using (var blockedDelete = await SendWithAntiforgeryAsync(
                   admin,
                   HttpMethod.Delete,
                   $"/api/organization/persons/{Uri.EscapeDataString(person.Id)}",
                   cancellationToken))
        {
            var blockedDeleteBody = await blockedDelete.Content.ReadAsStringAsync(cancellationToken);
            EnsureStatus(blockedDelete, HttpStatusCode.Conflict, blockedDeleteBody, "bound person delete guard");
            AssertContains(blockedDeleteBody, "Organization.Person.DeleteBlockedByUserBinding", "bound delete guard code");
            checks.Add(new { check = "bound-person-delete-guard", status = 409 });
        }
        using (var deleted = await SendWithAntiforgeryAsync(
                   admin,
                   HttpMethod.Delete,
                   $"/api/organization/persons/{Uri.EscapeDataString(unaffiliated.Id)}",
                   cancellationToken))
        {
            EnsureStatus(deleted, HttpStatusCode.NoContent, string.Empty, "delete unbound person");
            checks.Add(new { check = "delete-unbound-person", status = 204 });
        }

        checks.Add(new { check = "admin-create-edit-detail-list", status = 200, personId = person.Id, personNo });
        await RecordScenarioSuccessAsync(ApiScenario, "personnel/api.json", new
        {
            status = "passed",
            apiAssertions = checks,
            matrix = new[]
            {
                "page-and-query",
                "toolbar-and-preferences",
                "crud-and-actions",
                "permissions-and-security",
                "responsive-and-accessibility"
            },
            person = new { id = person.Id, personNo, name = updatedName },
            organization = new { fixture.CompanyId, fixture.CompanyName, fixture.DepartmentId, fixture.DepartmentName }
        }, cancellationToken);
    }

    // 首次浏览器生命周期改写审计字段前，真实重启 Host 并通过正式 detail seam 锁定 API 合法更新 checkpoint。
    private async Task AssertApiAuditCheckpointAfterRestartAsync(CancellationToken cancellationToken)
    {
        var person = _person ?? throw new InvalidOperationException("Personnel API scenario did not create a person.");
        var fixture = _fixture ?? throw new InvalidOperationException("Personnel fixture was not prepared.");
        await RunDevScriptAsync("stop-dev.ps1", "personnel/pre-browser-restart-stop.json", cancellationToken);
        await StartDevAsync("personnel/pre-browser-restart-dev-start.json", cancellationToken);

        using var admin = CreateClient(_hostPort);
        await LoginAdminAsync(admin, cancellationToken);
        using var response = await admin.GetAsync(
            $"/api/organization/persons/{Uri.EscapeDataString(person.Id)}",
            cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        EnsureStatus(response, HttpStatusCode.OK, body, "detail after pre-browser restart");
        AssertPersonBody(body, person, fixture, "detail after pre-browser restart");
    }

    // 浏览器场景只通过正式 Shell 路由和 API 完成列表、Drawer、完整详情、动作与权限旅程。
    private async Task RunBrowserScenarioAsync(string phase, CancellationToken cancellationToken)
    {
        var person = _person ?? throw new InvalidOperationException("Personnel API scenario did not create a person.");
        var fixture = _fixture ?? throw new InvalidOperationException("Personnel fixture was not prepared.");
        var adminPassword = _adminPassword ?? throw new InvalidOperationException("Administrator password was not prepared.");
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
        await page.Locator("#ecode").FillAsync("admin");
        await page.Locator("#password").FillAsync(adminPassword);
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "登录", Exact = true }).ClickAsync();
        await WaitForPostLoginNavigationAsync(page);
        if (phase == "initial")
        {
            var position = await CreatePositionFromBrowserAsync(page, cancellationToken);
            person = await AssignPositionToPersonFromBrowserAsync(page, person, position, cancellationToken);
            person = await MaintainPersonnelTemporalFieldsFromBrowserAsync(page, person, cancellationToken);
            _position = position;
            _person = person;
            // 回到岗位列表后再从导航进入人员中心，让原有首屏请求守卫仍观察一个干净的列表进入动作。
            await OpenPositionManagementFromShellAsync(page);
        }

        BrowserRequestDuplicateGuard? initialRequestGuard = null;
        BrowserRequestDuplicateGuardReport? initialRequestReport = null;
        EventHandler<IRequest>? initialRequestObserver = null;
        object? requestFingerprintEvidence = null;
        if (phase == "initial")
        {
            initialRequestGuard = new BrowserRequestDuplicateGuard(
                "GET",
                "/api/organization/persons",
                []);
            initialRequestObserver = (_, request) =>
                initialRequestGuard.Observe(request.Method, request.Url, request.PostData);
            page.Request += initialRequestObserver;
        }

        try
        {
            await OpenPersonnelFromShellAsync(page);
            await AssertPersonListAndDrawerAsync(page, person, fixture, _position?.Name);
            if (initialRequestGuard is not null)
            {
                initialRequestReport = await initialRequestGuard.WaitForQuietPeriodAsync(
                    TimeSpan.FromMilliseconds(250),
                    TimeSpan.FromSeconds(5),
                    cancellationToken);
                if (initialRequestObserver is not null)
                {
                    page.Request -= initialRequestObserver;
                    initialRequestObserver = null;
                }

                var initialRequestEvidenceRef = await WriteEvidenceAsync(
                    "personnel/browser-initial-request-fingerprints.json",
                    initialRequestReport,
                    cancellationToken);
                requestFingerprintEvidence = new
                {
                    evidenceRef = initialRequestEvidenceRef,
                    decision = initialRequestReport.Decision
                };
                initialRequestReport.EnsurePassed();
            }
        }
        finally
        {
            if (initialRequestObserver is not null)
            {
                page.Request -= initialRequestObserver;
            }
        }

        IReadOnlyList<string> interceptedResponseAssertions = [];
        var browserAssertions = new List<string>
        {
            "canonical-list-row-visible",
            "canonical-view-drawer-visible",
            "company-display-name-readable",
            "department-display-name-readable",
            "personnel-temporal-display-values-readable"
        };
        if (phase == "initial")
        {
            await page.ReloadAsync(new PageReloadOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
            await AssertPersonListAndDrawerAsync(page, person, fixture, _position?.Name);
            await AssertSubmittedQueryAndKeyboardAsync(page, person);
            await AssertPersonBirthDateRangeAsync(page, person);
            browserAssertions.AddRange([
                "hard-refresh-canonical-list-and-drawer",
                "enter-submits-query-and-updates-url",
                "reset-restores-default-query",
                "single-refresh-entry",
                "named-keyboard-query-control",
                "calendar-date-range-restored-and-applied"
            ]);

            await CaptureResponsiveBrowserEvidenceAsync(
                browser,
                person,
                adminPassword,
                "tablet",
                900,
                900,
                cancellationToken);
            browserAssertions.Add("tablet-table-with-responsive-columns");
            await CaptureResponsiveBrowserEvidenceAsync(
                browser,
                person,
                adminPassword,
                "narrow",
                390,
                844,
                cancellationToken);
            browserAssertions.Add("narrow-table-with-horizontal-scroll");
            await CaptureRestrictedBrowserEvidenceAsync(browser, person, cancellationToken);
            browserAssertions.Add("restricted-view-only-actions-hidden");
            await AssertPersonListAndDrawerAsync(page, person, fixture, _position?.Name);
            await AssertFullDetailJourneyAsync(page, person, fixture, exerciseActions: true, cancellationToken);
            browserAssertions.AddRange([
                "drawer-to-full-detail",
                "full-detail-direct-refresh-back-forward",
                "detail-three-sections-four-audit-fields",
                "detail-summary-ready",
                "detail-edit-permission",
                "detail-disable-required-reason",
                "detail-disable-success",
                "detail-disable-conflict",
                "detail-disable-conflict-dialog-auto-closed",
                "detail-disable-conflict-detail-refreshed",
                "detail-disable-conflict-command-not-replayed"
            ]);
            interceptedResponseAssertions = await CaptureDetailContractDriftBrowserEvidenceAsync(
                browser,
                person,
                adminPassword,
                cancellationToken);
            browserAssertions.AddRange(interceptedResponseAssertions.Select(
                static check => $"browser-intercepted-response:{check}"));
            await CaptureUnauthorizedDetailEvidenceAsync(browser, person, cancellationToken);
            browserAssertions.Add("detail-capability-denied-before-download");
            browserAssertions.AddRange([
                "position-management-navigation-visible",
                "position-standard-create-drawer-saved",
                "person-position-remote-search-requested",
                "person-position-reference-saved"
            ]);
        }
        else
        {
            await AssertFullDetailJourneyAsync(page, person, fixture, exerciseActions: false, cancellationToken);
            browserAssertions.Add("restart-relogin-canonical-list-and-drawer");
            browserAssertions.Add("restart-full-detail-route-and-summary");
        }
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = screenshotPath, FullPage = true });
        _artifactRefs.Add(AcceptanceInfrastructure.RelativeArtifact(_runDirectory, screenshotPath));

        var evidenceRef = await WriteEvidenceAsync($"personnel/browser-{phase}.json", new
        {
            status = "passed",
            phase,
            url = page.Url,
            requestFingerprintEvidence,
            browserAssertions = browserAssertions,
            interceptedResponseAssertions = interceptedResponseAssertions.Select(check => new
            {
                check,
                source = "browser-intercepted-response",
                scope = "frontend-contract-drift-presentation",
                apiContractEvidence = false,
                realBackendExceptionPath = false
            }),
            apiAssertions = _apiAssertions,
            unitAssertions = new[]
            {
                new
                {
                    check = "detail-summary-ready-notConfigured-dependencyUnavailable",
                    source = "frontend/packages/shell/src/__tests__/organization-person-center-detail-layout.test.tsx",
                    verification = "targeted-unit-outside-browser-gate"
                },
                new
                {
                    check = "detail-disable-client-400-401-403",
                    source = "frontend/modules/organization/src/__tests__/personLifecycleClient.test.ts",
                    verification = "targeted-unit-outside-browser-gate"
                }
            },
            coverage = new[]
            {
                new
                {
                    area = "page-and-query",
                    browser = new[] { "initial", "query-enter", "reset", "url-hard-refresh", "restart" },
                    automated = new[] { "standard-crud-list-host", "standard-crud-metadata-query", "standard-crud-pro-adapter" }
                },
                new
                {
                    area = "toolbar-and-preferences",
                    browser = new[] { "single-refresh-entry" },
                    automated = new[] { "standard-crud-column-preference", "standard-crud-list-host-export" }
                },
                new
                {
                    area = "crud-and-actions",
                    browser = new[] { "view-drawer", "bound-delete-disabled" },
                    automated = new[] { "standard-crud-action-runtime", "standard-crud-field-form", "organization-person-center-list-layout" }
                },
                new
                {
                    area = "permissions-and-security",
                    browser = new[] { "administrator", "restricted-view-only" },
                    automated = new[] { "organization-permission-pipeline", "organization-lifecycle-http-api" }
                },
                new
                {
                    area = "responsive-and-accessibility",
                    browser = new[] { "desktop", "tablet", "narrow", "keyboard-enter", "named-controls" },
                    automated = new[] { "standard-crud-component-source-contract", "organization-person-center-list-layout" }
                }
            },
            screenshot = AcceptanceInfrastructure.RelativeArtifact(_runDirectory, screenshotPath)
        }, cancellationToken);

        _ = evidenceRef;
    }

    // 只拦截浏览器收到的正式详情响应，用于证明前端面对契约漂移时的局部降级；不能计作后端异常路径证据。
    private async Task<IReadOnlyList<string>> CaptureDetailContractDriftBrowserEvidenceAsync(
        IBrowser browser,
        PersonData person,
        string adminPassword,
        CancellationToken cancellationToken)
    {
        var checks = new List<string>();
        var mode = "null";
        var interceptedModeSignals = new[]
        {
            "null",
            "field-missing",
            "section-unavailable",
            "critical-id",
            "action-dependency"
        }.ToDictionary(
            static key => key,
            static _ => new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously),
            StringComparer.Ordinal);
        var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            Locale = "zh-CN",
            ViewportSize = new ViewportSize { Width = 1280, Height = 900 }
        });
        await using (context)
        {
            var page = await LoginAndOpenPersonnelAsync(context, "admin", adminPassword, cancellationToken);
            var detailApiPath = $"/api/organization/persons/{Uri.EscapeDataString(person.Id)}";
            await page.RouteAsync("**/api/organization/persons/**", async route =>
            {
                var requestUri = new Uri(route.Request.Url);
                if (!string.Equals(requestUri.AbsolutePath, detailApiPath, StringComparison.Ordinal))
                {
                    await route.ContinueAsync();
                    return;
                }

                var interceptedMode = mode;
                var response = await route.FetchAsync();
                var payload = JsonNode.Parse(await response.TextAsync())?.AsObject()
                    ?? throw new InvalidOperationException("Intercepted Personnel detail response is not a JSON object.");
                var fields = payload["fields"]?.AsObject()
                    ?? throw new InvalidOperationException("Intercepted Personnel detail response does not contain fields.");

                switch (interceptedMode)
                {
                    case "null":
                        fields["UpdatedBy"] = null;
                        break;
                    case "field-missing":
                        fields.Remove("UpdatedBy");
                        break;
                    case "section-unavailable":
                        foreach (var fieldKey in new[] { "CreatedBy", "CreatedAt", "UpdatedBy", "UpdatedAt" })
                        {
                            fields.Remove(fieldKey);
                        }
                        break;
                    case "critical-id":
                        fields.Remove("EID");
                        break;
                    case "action-dependency":
                        fields.Remove("Revision");
                        break;
                    default:
                        throw new InvalidOperationException($"Unsupported detail contract drift mode '{interceptedMode}'.");
                }

                await route.FulfillAsync(new RouteFulfillOptions
                {
                    Response = response,
                    Body = payload.ToJsonString(),
                    ContentType = "application/json"
                });
                interceptedModeSignals[interceptedMode].TrySetResult(true);
            });

            var detailUrl = $"http://127.0.0.1:{_shellPort}/organization/persons/{Uri.EscapeDataString(person.Id)}";
            await page.GotoAsync(detailUrl, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
            await page.GetByRole(AriaRole.Heading, new PageGetByRoleOptions { Name = "人员详情", Exact = true })
                .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
            await WaitForInterceptedModeAsync(interceptedModeSignals["null"].Task, "null", cancellationToken);
            var updatedByItem = page.Locator("[data-field-key='UpdatedBy']");
            await updatedByItem.WaitForAsync(new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Visible,
                Timeout = 15_000
            });
            AssertContains(await updatedByItem.InnerTextAsync(), "—", "present null detail field");
            checks.Add("detail-null-renders-em-dash");

            mode = "field-missing";
            await page.ReloadAsync(new PageReloadOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
            await WaitForInterceptedModeAsync(
                interceptedModeSignals["field-missing"].Task,
                "field-missing",
                cancellationToken);
            await page.Locator("[data-field-key='UpdatedBy']")
                .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
            if (await page.Locator("[data-section-key='audit']").CountAsync() != 0)
            {
                throw new InvalidOperationException("One missing audit field incorrectly degraded the whole section.");
            }
            checks.Add("detail-missing-field-degrades-locally");

            mode = "section-unavailable";
            await page.ReloadAsync(new PageReloadOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
            await WaitForInterceptedModeAsync(
                interceptedModeSignals["section-unavailable"].Task,
                "section-unavailable",
                cancellationToken);
            var unavailableSection = page.Locator("[data-section-key='audit']");
            await unavailableSection.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
            AssertContains(await unavailableSection.InnerTextAsync(), "数据不可用", "unavailable audit section");
            checks.Add("detail-unavailable-section-degrades-whole-section");

            mode = "critical-id";
            await page.ReloadAsync(new PageReloadOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
            await WaitForInterceptedModeAsync(
                interceptedModeSignals["critical-id"].Task,
                "critical-id",
                cancellationToken);
            await page.GetByText(
                    "详情数据契约失败：STANDARD_CRUD_DETAIL_CRITICAL_ID_MISSING",
                    new PageGetByTextOptions { Exact = true })
                .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
            if (await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "编辑", Exact = true }).CountAsync() != 0
                || await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "停用", Exact = true }).CountAsync() != 0)
            {
                throw new InvalidOperationException("Critical Personnel detail identity failure left actions visible.");
            }
            checks.Add("detail-critical-id-fails-whole-detail");

            mode = "action-dependency";
            await page.ReloadAsync(new PageReloadOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
            await WaitForInterceptedModeAsync(
                interceptedModeSignals["action-dependency"].Task,
                "action-dependency",
                cancellationToken);
            var edit = page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "编辑", Exact = true });
            var disable = page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "停用", Exact = true });
            await edit.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
            if (!await edit.IsDisabledAsync() || !await disable.IsDisabledAsync())
            {
                throw new InvalidOperationException("Missing Personnel Revision did not disable all dependency-bound actions.");
            }
            await page.GetByText(
                    "动作所需数据不可用，请刷新详情后重试。",
                    new PageGetByTextOptions { Exact = true })
                .First
                .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
            checks.Add("detail-action-dependency-disables-action");

            var screenshotPath = Path.Combine(_runDirectory, "personnel", "browser-intercepted-response.png");
            await page.ScreenshotAsync(new PageScreenshotOptions { Path = screenshotPath, FullPage = true });
            _artifactRefs.Add(AcceptanceInfrastructure.RelativeArtifact(_runDirectory, screenshotPath));
            await WriteEvidenceAsync("personnel/browser-intercepted-response.json", new
            {
                status = "passed",
                source = "browser-intercepted-response",
                scope = "frontend-contract-drift-presentation",
                apiContractEvidence = false,
                realBackendExceptionPath = false,
                checks,
                screenshot = AcceptanceInfrastructure.RelativeArtifact(_runDirectory, screenshotPath)
            }, cancellationToken);
        }

        return checks;
    }

    private static async Task WaitForInterceptedModeAsync(
        Task signal,
        string expectedMode,
        CancellationToken cancellationToken)
    {
        try
        {
            await signal.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
        }
        catch (TimeoutException exception)
        {
            throw new InvalidOperationException(
                $"Personnel detail response was not intercepted for contract drift mode '{expectedMode}'.",
                exception);
        }
    }

    private async Task AssertSubmittedQueryAndKeyboardAsync(IPage page, PersonData person)
    {
        var drawer = page.Locator(".ant-drawer-content:visible").Last;
        if (await drawer.IsVisibleAsync())
        {
            await drawer.Locator(".ant-drawer-close").ClickAsync();
            await drawer.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden });
        }

        var keyword = page.GetByLabel("关键字", new PageGetByLabelOptions { Exact = true });
        await keyword.FillAsync(string.Empty);
        await keyword.FillAsync(person.PersonNo);
        await keyword.PressAsync("Enter");
        await page.WaitForFunctionAsync("() => new URL(window.location.href).searchParams.has('keyword')");
        await page.Locator("tr:visible", new PageLocatorOptions { HasTextString = person.PersonNo }).Last
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        if (!await keyword.EvaluateAsync<bool>("element => element === document.activeElement"))
        {
            throw new InvalidOperationException("Personnel keyword control did not retain keyboard focus after Enter submission.");
        }

        var refresh = page.GetByRole(
            AriaRole.Button,
            new PageGetByRoleOptions { Name = "刷新", Exact = true }).First;
        await refresh.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await refresh.First.ClickAsync();
        await page.Locator("tr:visible", new PageLocatorOptions { HasTextString = person.PersonNo }).Last
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        var reset = page.GetByRole(
            AriaRole.Button,
            new PageGetByRoleOptions { Name = "重置", Exact = true }).First;
        await reset.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await reset.First.ClickAsync();
        await page.WaitForFunctionAsync("() => !new URL(window.location.href).searchParams.has('keyword')");
    }

    private async Task CaptureResponsiveBrowserEvidenceAsync(
        IBrowser browser,
        PersonData person,
        string adminPassword,
        string viewportName,
        int width,
        int height,
        CancellationToken cancellationToken)
    {
        var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            Locale = "zh-CN",
            ViewportSize = new ViewportSize { Width = width, Height = height }
        });
        await using (context)
        {
            var page = await LoginAndOpenPersonnelAsync(context, "admin", adminPassword, cancellationToken);
            await SubmitPersonnelKeywordAsync(page, person.PersonNo);
            var row = page.Locator("tr:visible", new PageLocatorOptions { HasTextString = person.PersonNo }).Last;
            await row.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
            AssertContains(await row.InnerTextAsync(), person.Name, $"{viewportName} person row");
            var tableContent = page.Locator(".ant-table-content:visible").First;
            await tableContent.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
            if (viewportName == "narrow"
                && !await tableContent.EvaluateAsync<bool>("element => element.scrollWidth > element.clientWidth"))
            {
                throw new InvalidOperationException("Personnel narrow viewport did not retain horizontal table scrolling.");
            }

            var screenshotName = viewportName == "tablet" ? TabletScreenshotName : NarrowScreenshotName;
            var screenshotPath = Path.Combine(_runDirectory, "personnel", screenshotName);
            await page.ScreenshotAsync(new PageScreenshotOptions { Path = screenshotPath, FullPage = true });
            _artifactRefs.Add(AcceptanceInfrastructure.RelativeArtifact(_runDirectory, screenshotPath));
        }
    }

    private async Task CaptureRestrictedBrowserEvidenceAsync(
        IBrowser browser,
        PersonData person,
        CancellationToken cancellationToken)
    {
        var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            Locale = "zh-CN",
            ViewportSize = new ViewportSize { Width = 1280, Height = 900 }
        });
        await using (context)
        {
            var page = await LoginAndOpenPersonnelAsync(
                context,
                _readOnlyECode ?? throw new InvalidOperationException("Read-only ECODE was not prepared."),
                _readOnlyPassword ?? throw new InvalidOperationException("Read-only password was not prepared."),
                cancellationToken);
            await SubmitPersonnelKeywordAsync(page, person.PersonNo);
            var row = page.Locator("tr:visible", new PageLocatorOptions { HasTextString = person.PersonNo }).Last;
            await row.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
            if (await page.Locator("button:visible", new PageLocatorOptions { HasTextString = "新建人员" }).CountAsync() != 0
                || await row.Locator("button:visible", new LocatorLocatorOptions { HasTextString = "编辑" }).CountAsync() != 0)
            {
                throw new InvalidOperationException("Restricted Personnel subject received a write action in the Shell projection.");
            }

            await row.GetByRole(
                    AriaRole.Button,
                    new LocatorGetByRoleOptions { Name = person.Name, Exact = true })
                .ClickAsync();
            var drawer = page.Locator(".ant-drawer-content:visible").Last;
            await drawer.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
            await drawer.GetByRole(
                    AriaRole.Button,
                    new LocatorGetByRoleOptions { Name = "查看完整详情", Exact = true })
                .ClickAsync();
            await page.WaitForURLAsync(
                $"http://127.0.0.1:{_shellPort}/organization/persons/{Uri.EscapeDataString(person.Id)}",
                new PageWaitForURLOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
            await page.GetByRole(AriaRole.Heading, new PageGetByRoleOptions { Name = "人员详情", Exact = true })
                .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
            if (await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "编辑", Exact = true }).CountAsync() != 0
                || await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "停用", Exact = true }).CountAsync() != 0)
            {
                throw new InvalidOperationException("Restricted Personnel subject received a full-detail write action.");
            }

            var screenshotPath = Path.Combine(_runDirectory, "personnel", "browser-restricted.png");
            await page.ScreenshotAsync(new PageScreenshotOptions { Path = screenshotPath, FullPage = true });
            _artifactRefs.Add(AcceptanceInfrastructure.RelativeArtifact(_runDirectory, screenshotPath));
        }
    }

    // 无权限 actor 直达详情时必须先停在 Shell guard，不能触发人员详情 capability 的动态下载。
    private async Task CaptureUnauthorizedDetailEvidenceAsync(
        IBrowser browser,
        PersonData person,
        CancellationToken cancellationToken)
    {
        var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            Locale = "zh-CN",
            ViewportSize = new ViewportSize { Width = 1280, Height = 900 }
        });
        await using (context)
        {
            var requestedUrls = new List<string>();
            var page = await LoginInBrowserAsync(
                context,
                _unauthorizedECode ?? throw new InvalidOperationException("Unauthorized ECODE was not prepared."),
                _unauthorizedPassword ?? throw new InvalidOperationException("Unauthorized password was not prepared."),
                cancellationToken);
            page.Request += (_, request) => requestedUrls.Add(request.Url);
            requestedUrls.Clear();

            var detailUrl = $"http://127.0.0.1:{_shellPort}/organization/persons/{Uri.EscapeDataString(person.Id)}";
            await page.GotoAsync(detailUrl, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
            await page.GetByText("无权限访问", new PageGetByTextOptions { Exact = true })
                .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
            var requestedCapabilityUrls = requestedUrls
                .Where(static url => url.Contains("personStandardCrudResource", StringComparison.OrdinalIgnoreCase))
                .Select(static url => new Uri(url).AbsolutePath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (requestedCapabilityUrls.Length > 0)
            {
                throw new InvalidOperationException(
                    $"Unauthorized detail route downloaded the Personnel capability before access denial: {string.Join(", ", requestedCapabilityUrls)}");
            }

            var screenshotPath = Path.Combine(_runDirectory, "personnel", "browser-detail-unauthorized.png");
            await page.ScreenshotAsync(new PageScreenshotOptions { Path = screenshotPath, FullPage = true });
            _artifactRefs.Add(AcceptanceInfrastructure.RelativeArtifact(_runDirectory, screenshotPath));
            var evidenceRef = await WriteEvidenceAsync("personnel/browser-detail-unauthorized.json", new
            {
                status = "passed",
                denial = "无权限访问",
                requestedPersonnelCapabilityUrls = requestedCapabilityUrls
            }, cancellationToken);
            _artifactRefs.Add(evidenceRef);
        }
    }

    // 列表观察复用同一正式登录入口，登录完成后再从 Shell 导航进入人员中心。
    private async Task<IPage> LoginAndOpenPersonnelAsync(
        IBrowserContext context,
        string ecode,
        string password,
        CancellationToken cancellationToken)
    {
        var page = await LoginInBrowserAsync(context, ecode, password, cancellationToken);
        await OpenPersonnelFromShellAsync(page);
        return page;
    }

    // 浏览器 actor 只通过正式登录表单建立认证状态，不注入 Cookie 或前端权限快照。
    private async Task<IPage> LoginInBrowserAsync(
        IBrowserContext context,
        string ecode,
        string password,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var page = await context.NewPageAsync();
        page.SetDefaultTimeout(120_000);
        page.SetDefaultNavigationTimeout(120_000);
        await page.GotoAsync($"http://127.0.0.1:{_shellPort}/login", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.DOMContentLoaded
        });
        await page.Locator("#ecode").FillAsync(ecode);
        await page.Locator("#password").FillAsync(password);
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "登录", Exact = true }).ClickAsync();
        await WaitForPostLoginNavigationAsync(page);
        return page;
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

    // 同一 Personnel 浏览器场景通过标准 Drawer 创建岗位，不把字段边界语义提升到浏览器层。
    private async Task<PositionData> CreatePositionFromBrowserAsync(
        IPage page,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await OpenPositionManagementFromShellAsync(page);
        var name = $"Personnel Gate Position {_runId[..8]}";
        var description = "Personnel Gate browser journey";
        await page.GetByRole(
                AriaRole.Button,
                new PageGetByRoleOptions { Name = "新建岗位", Exact = true })
            .ClickAsync();
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
        if (response.Status != (int)HttpStatusCode.Created)
        {
            throw new InvalidOperationException($"Browser position create returned {response.Status}, expected 201.");
        }

        await drawer.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden });
        var row = page.Locator("tr:visible", new PageLocatorOptions { HasTextString = name })
            .Filter(new LocatorFilterOptions { HasTextString = description })
            .Last;
        await row.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        return new PositionData(name);
    }

    // 人员编辑 Drawer 必须把搜索文本送到远程岗位 Lookup，并通过标准 mutation 保存所选引用。
    private async Task<PersonData> AssignPositionToPersonFromBrowserAsync(
        IPage page,
        PersonData person,
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
        var positionLookup = drawer.GetByLabel(
            "岗位",
            new LocatorGetByLabelOptions { Exact = true });
        await positionLookup.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
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

        var dropdown = page.Locator(".ant-select-dropdown:visible").Last;
        await dropdown.GetByText(position.Name, new LocatorGetByTextOptions { Exact = true })
            .ClickAsync();
        var personApiPath = $"/api/organization/persons/{Uri.EscapeDataString(person.Id)}";
        var updateResponseTask = page.WaitForResponseAsync(response =>
            response.Request.Method == "PUT"
            && new Uri(response.Url).AbsolutePath.Equals(personApiPath, StringComparison.Ordinal));
        await drawer.GetByRole(
                AriaRole.Button,
                new LocatorGetByRoleOptions { Name = "保存修改", Exact = true })
            .ClickAsync();
        var updateResponse = await updateResponseTask;
        var updateBody = await updateResponse.TextAsync();
        if (updateResponse.Status != (int)HttpStatusCode.OK)
        {
            throw new InvalidOperationException(
                $"Browser person position update returned {updateResponse.Status}, expected 200: {updateBody}");
        }

        await drawer.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden });
        var updated = ReadPerson(updateBody, person.PersonNo, person.Name, "browser assign person position");
        var updatedRow = page.Locator("tr:visible", new PageLocatorOptions { HasTextString = person.PersonNo })
            .Filter(new LocatorFilterOptions { HasTextString = position.Name })
            .Last;
        await updatedRow.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        return updated;
    }

    // Personnel smoke 通过共享标准字段维护三个时间值；输入 hydration 与提交均不绕过正式 Drawer/API。
    private static async Task<PersonData> MaintainPersonnelTemporalFieldsFromBrowserAsync(
        IPage page,
        PersonData person,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var row = page.Locator("tr:visible", new PageLocatorOptions { HasTextString = person.PersonNo })
            .Filter(new LocatorFilterOptions { HasTextString = person.Name })
            .Last;
        await row.GetByRole(
                AriaRole.Button,
                new LocatorGetByRoleOptions { Name = "编辑", Exact = true })
            .ClickAsync();

        var drawer = page.Locator(".ant-drawer-content:visible").Last;
        await drawer.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        foreach (var (label, value) in PersonnelTemporalFormInputs)
        {
            var input = drawer.GetByLabel(label, new LocatorGetByLabelOptions { Exact = true });
            await input.FillAsync(value);
            await input.PressAsync("Enter");
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
                $"Browser person temporal update returned {response.Status}, expected 200: {body}");
        }

        using var json = JsonDocument.Parse(body);
        var fields = ReadObject(json.RootElement, "fields");
        foreach (var (field, expected) in new[]
                 {
                     ("BirthDate", PersonnelBirthDate),
                     ("BirthDate_DisplayValue", PersonnelBirthDateDisplay),
                     ("EmploymentStartedAt", PersonnelStartedAtUtc),
                     ("EmploymentStartedAt_DisplayValue", PersonnelStartedAtDisplay),
                     ("EmploymentEndedAt", PersonnelEndedAtUtc),
                     ("EmploymentEndedAt_DisplayValue", PersonnelEndedAtDisplay)
                 })
        {
            if (!string.Equals(ReadString(fields, field), expected, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Browser person temporal update returned an unexpected {field} value.");
            }
        }

        await drawer.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden });
        return ReadPerson(body, person.PersonNo, person.Name, "browser maintain personnel temporal fields");
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

    // 列表姓名入口必须打开共享 Detail View Drawer，并呈现组织展示值、可选岗位与审计字段。
    private static async Task AssertPersonListAndDrawerAsync(
        IPage page,
        PersonData person,
        FixtureData fixture,
        string? positionName)
    {
        await SubmitPersonnelKeywordAsync(page, person.PersonNo);
        var row = page.Locator("tr:visible", new PageLocatorOptions { HasTextString = person.PersonNo })
            .Filter(new LocatorFilterOptions { HasTextString = person.Name })
            .Filter(new LocatorFilterOptions { HasTextString = fixture.CompanyName })
            .Filter(new LocatorFilterOptions { HasTextString = fixture.DepartmentName });
        if (!string.IsNullOrWhiteSpace(positionName))
        {
            row = row.Filter(new LocatorFilterOptions { HasTextString = positionName });
        }
        row = row.Last;
        await row.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        var rowText = await row.InnerTextAsync();
        AssertContains(rowText, person.PersonNo, "person number in list row");
        AssertContains(rowText, person.Name, "person name in list row");
        AssertContains(rowText, PersonnelBirthDateDisplay, "birth date display value in list row");
        AssertContains(rowText, fixture.CompanyName, "company display name in list row");
        AssertContains(rowText, fixture.DepartmentName, "department display name in list row");
        AssertContains(rowText, PersonnelStartedAtDisplay, "employment start display value in list row");
        AssertContains(rowText, PersonnelEndedAtDisplay, "employment end display value in list row");
        if (!string.IsNullOrWhiteSpace(positionName))
        {
            AssertContains(rowText, positionName, "position display name in list row");
        }

        var drawer = page.Locator(".ant-drawer-content:visible").Last;
        // 硬刷新会按 URL 恢复已打开的查看抽屉；此时遮罩仍在，不能再次点击行内入口。
        if (!await drawer.IsVisibleAsync())
        {
            await row.GetByRole(
                AriaRole.Button,
                new LocatorGetByRoleOptions { Name = person.Name, Exact = true }).ClickAsync();
        }
        await drawer.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        foreach (var expected in new[]
                 {
                     person.PersonNo,
                     person.Name,
                     PersonnelBirthDateDisplay,
                     fixture.CompanyName,
                     fixture.DepartmentName,
                     positionName,
                     PersonnelStartedAtDisplay,
                     PersonnelEndedAtDisplay
                 }
                     .Where(static value => !string.IsNullOrWhiteSpace(value)))
        {
            await drawer.GetByText(expected!, new LocatorGetByTextOptions { Exact = true }).First
                .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        }
        var drawerText = await drawer.InnerTextAsync();
        AssertContains(drawerText, person.PersonNo, "person number in view drawer");
        AssertContains(drawerText, person.Name, "person name in view drawer");
        AssertContains(drawerText, PersonnelBirthDateDisplay, "birth date display value in view drawer");
        AssertContains(drawerText, fixture.CompanyName, "company display name in view drawer");
        AssertContains(drawerText, fixture.DepartmentName, "department display name in view drawer");
        AssertContains(drawerText, PersonnelStartedAtDisplay, "employment start display value in view drawer");
        AssertContains(drawerText, PersonnelEndedAtDisplay, "employment end display value in view drawer");
        if (!string.IsNullOrWhiteSpace(positionName))
        {
            AssertContains(drawerText, positionName, "position display name in view drawer");
        }
        foreach (var expected in new[]
                 {
                     "基础信息", "组织信息", "任职信息", "审计信息", "创建人", "创建时间", "更新人", "更新时间"
                 })
        {
            AssertContains(drawerText, expected, $"drawer detail field {expected}");
        }
    }

    // 完整详情旅程覆盖共享内容与停用生命周期；409 后观察 Dialog 自动关闭、单次刷新且命令不重放。
    private async Task AssertFullDetailJourneyAsync(
        IPage page,
        PersonData person,
        FixtureData fixture,
        bool exerciseActions,
        CancellationToken cancellationToken)
    {
        var drawer = page.Locator(".ant-drawer-content:visible").Last;
        await drawer.GetByRole(
                AriaRole.Button,
                new LocatorGetByRoleOptions { Name = "查看完整详情", Exact = true })
            .ClickAsync();
        var detailUrl = $"http://127.0.0.1:{_shellPort}/organization/persons/{Uri.EscapeDataString(person.Id)}";
        await page.WaitForURLAsync(detailUrl, new PageWaitForURLOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await AssertFullDetailContentAsync(page, person, fixture);

        await page.ReloadAsync(new PageReloadOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await AssertFullDetailContentAsync(page, person, fixture);

        await page.GoBackAsync(new PageGoBackOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        var listUri = new Uri(page.Url);
        var listQuery = System.Web.HttpUtility.ParseQueryString(listUri.Query);
        if (!string.Equals(listUri.AbsolutePath, "/organization/persons", StringComparison.Ordinal)
            || !Uri.UnescapeDataString(listUri.Query).Contains(person.PersonNo, StringComparison.Ordinal)
            || !string.Equals(listQuery["drawer"], "view", StringComparison.Ordinal)
            || !string.Equals(listQuery["id"], person.Id, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Browser back returned an unstable Personnel list URL: {listUri.PathAndQuery}");
        }
        await page.GetByText(person.Name, new PageGetByTextOptions { Exact = true }).First
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        var restoredDrawer = page.Locator(".ant-drawer-content:visible").Last;
        await restoredDrawer.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await restoredDrawer.GetByText(person.Name, new LocatorGetByTextOptions { Exact = true }).First
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        await page.GoForwardAsync(new PageGoForwardOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForURLAsync(detailUrl, new PageWaitForURLOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await AssertFullDetailContentAsync(page, person, fixture);

        await page.GotoAsync(detailUrl, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await AssertFullDetailContentAsync(page, person, fixture);
        if (!exerciseActions)
        {
            return;
        }

        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "编辑", Exact = true })
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        var reasonInput = await OpenDisableReasonDialogAsync(page);
        var inputModal = page.Locator(".ant-modal-content:visible").Last;
        await inputModal.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { NameRegex = new Regex("^确\\s*认$") })
            .ClickAsync();
        await page.GetByText("请填写停用原因。", new PageGetByTextOptions { Exact = true })
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        await reasonInput.FillAsync("Personnel detail browser disable");
        var successResponseTask = page.WaitForResponseAsync(static response =>
            response.Request.Method == "PUT"
            && response.Url.EndsWith("/status", StringComparison.Ordinal));
        await inputModal.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { NameRegex = new Regex("^确\\s*认$") })
            .ClickAsync();
        var successResponse = await successResponseTask;
        if (successResponse.Status != (int)HttpStatusCode.OK)
        {
            throw new InvalidOperationException($"Browser disable returned {successResponse.Status}, expected 200.");
        }
        await inputModal.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden });

        _person = await EnablePersonAfterBrowserDisableAsync(person, cancellationToken);
        reasonInput = await OpenDisableReasonDialogAsync(page);
        inputModal = page.Locator(".ant-modal-content:visible").Last;
        await reasonInput.FillAsync("Personnel detail stale disable");
        var personDetailApiPath = $"/api/organization/persons/{Uri.EscapeDataString(person.Id)}";
        var conflictActionGuard = new BrowserRequestDuplicateGuard(
            "PUT",
            $"{personDetailApiPath}/status",
            []);
        EventHandler<IRequest> conflictActionObserver = (_, request) =>
            conflictActionGuard.Observe(request.Method, request.Url, request.PostData);
        page.Request += conflictActionObserver;
        try
        {
            var conflictResponseTask = page.WaitForResponseAsync(static response =>
                response.Request.Method == "PUT"
                && response.Url.EndsWith("/status", StringComparison.Ordinal));
            var refreshedDetailResponseTask = page.WaitForResponseAsync(response =>
                response.Request.Method == "GET"
                && response.Url.EndsWith(personDetailApiPath, StringComparison.Ordinal));
            await inputModal.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { NameRegex = new Regex("^确\\s*认$") })
                .ClickAsync();
            var conflictResponse = await conflictResponseTask;
            if (conflictResponse.Status != (int)HttpStatusCode.Conflict)
            {
                throw new InvalidOperationException($"Browser stale disable returned {conflictResponse.Status}, expected 409.");
            }
            await inputModal.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden });
            await page.GetByText("LIFECYCLE_CONFLICT", new PageGetByTextOptions { Exact = true })
                .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
            var refreshedDetailResponse = await refreshedDetailResponseTask;
            if (refreshedDetailResponse.Status != (int)HttpStatusCode.OK)
            {
                throw new InvalidOperationException(
                    $"Browser conflict refresh returned {refreshedDetailResponse.Status}, expected 200.");
            }

            var conflictActionReport = await conflictActionGuard.WaitForQuietPeriodAsync(
                TimeSpan.FromMilliseconds(250),
                TimeSpan.FromSeconds(5),
                cancellationToken);
            conflictActionReport.EnsurePassed();
            if (conflictActionReport.Fingerprints.Sum(static fingerprint => fingerprint.PhysicalCount) != 1)
            {
                throw new InvalidOperationException("Browser stale disable command was replayed after the 409 response.");
            }
        }
        finally
        {
            page.Request -= conflictActionObserver;
        }
    }

    // 完整详情两容器共享字段结构；完整页额外必须展示两个真实 ready 摘要块和权限动作。
    private async Task AssertFullDetailContentAsync(IPage page, PersonData person, FixtureData fixture)
    {
        await page.GetByRole(AriaRole.Heading, new PageGetByRoleOptions { Name = "人员详情", Exact = true })
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        var main = page.Locator("main");
        await main.GetByText(person.PersonNo, new LocatorGetByTextOptions { Exact = true }).First
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await main.GetByText("账号绑定", new LocatorGetByTextOptions { Exact = true }).First
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await main.GetByText("授权摘要", new LocatorGetByTextOptions { Exact = true }).First
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await main.GetByText("已永久绑定", new LocatorGetByTextOptions { Exact = true }).First
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await main.GetByText("已加载", new LocatorGetByTextOptions { Exact = true }).First
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await main.GetByText(
                _readOnlyECode ?? throw new InvalidOperationException("Read-only ECODE was not prepared."),
                new LocatorGetByTextOptions { Exact = true })
            .First
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        var pageText = await main.InnerTextAsync();
        foreach (var expected in new[]
                 {
                     person.PersonNo,
                     person.Name,
                     fixture.CompanyName,
                     fixture.DepartmentName,
                     "基础信息",
                     "组织信息",
                     "审计信息",
                     "创建人",
                     "创建时间",
                     "更新人",
                     "更新时间",
                     "账号绑定",
                     "授权摘要"
                 })
        {
            AssertContains(pageText, expected, $"full detail content {expected}");
        }
    }

    // 带输入的危险动作在一个 Review Dialog 中同时呈现 metadata 确认文案和模块拥有的必填原因。
    private static async Task<ILocator> OpenDisableReasonDialogAsync(IPage page)
    {
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "停用", Exact = true })
            .ClickAsync();
        var inputModal = page.Locator(".ant-modal-content:visible").Last;
        await inputModal.GetByText("确认停用当前人员吗？", new LocatorGetByTextOptions { Exact = true })
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        var reasonInput = inputModal.GetByLabel("停用原因", new LocatorGetByLabelOptions { Exact = true });
        await reasonInput.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        return reasonInput;
    }

    // 浏览器成功停用后由正式 API 恢复人员，使同一页面保留旧 Revision 并产生可观测 409。
    private async Task<PersonData> EnablePersonAfterBrowserDisableAsync(
        PersonData person,
        CancellationToken cancellationToken)
    {
        using var admin = CreateClient(_hostPort);
        await LoginAdminAsync(admin, cancellationToken);
        using var detail = await admin.GetAsync(
            $"/api/organization/persons/{Uri.EscapeDataString(person.Id)}",
            cancellationToken);
        var detailBody = await detail.Content.ReadAsStringAsync(cancellationToken);
        EnsureStatus(detail, HttpStatusCode.OK, detailBody, "detail after browser disable");
        using var detailJson = JsonDocument.Parse(detailBody);
        var fields = ReadObject(detailJson.RootElement, "fields");
        if (ReadBoolean(fields, "IsEnabled"))
        {
            throw new InvalidOperationException("Browser disable did not persist through the formal API.");
        }

        using var enabled = await SendJsonWithAntiforgeryAsync(
            admin,
            HttpMethod.Put,
            $"/api/organization/persons/{Uri.EscapeDataString(person.Id)}/status",
            new
            {
                isEnabled = true,
                expectedRevision = ReadLong(fields, "Revision"),
                reason = "Prepare browser lifecycle conflict"
            },
            cancellationToken);
        var enabledBody = await enabled.Content.ReadAsStringAsync(cancellationToken);
        EnsureStatus(enabled, HttpStatusCode.OK, enabledBody, "enable after browser disable");
        using var enabledJson = JsonDocument.Parse(enabledBody);
        var enabledRevision = ReadLong(enabledJson.RootElement, "revision");

        using var restoredDetail = await admin.GetAsync(
            $"/api/organization/persons/{Uri.EscapeDataString(person.Id)}",
            cancellationToken);
        var restoredBody = await restoredDetail.Content.ReadAsStringAsync(cancellationToken);
        EnsureStatus(restoredDetail, HttpStatusCode.OK, restoredBody, "detail after browser lifecycle restore");
        var restoredPerson = ReadPerson(
            restoredBody,
            person.PersonNo,
            person.Name,
            "detail after browser lifecycle restore");
        if (restoredPerson.Revision != enabledRevision
            || !string.Equals(restoredPerson.CreatedAt, person.CreatedAt, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Browser lifecycle restore returned inconsistent Personnel audit state.");
        }

        return restoredPerson;
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
            AssertPersonBody(body, person, fixture, "detail after restart");
        }

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
        using var _ = await LoginAsync(
            client,
            "admin",
            _adminPassword ?? throw new InvalidOperationException("Administrator password was not prepared."),
            cancellationToken);
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

    private async Task<EntityId> PostAndReadIdAsync(
        HttpClient client,
        string path,
        object payload,
        CancellationToken cancellationToken)
    {
        using var response = await SendJsonWithAntiforgeryAsync(client, HttpMethod.Post, path, payload, cancellationToken);
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
        using var response = await SendJsonWithAntiforgeryAsync(
            client,
            HttpMethod.Post,
            "/api/organization/persons",
            payload,
            cancellationToken);
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

    // canonical display companion 是正式 REST 输出字段；真实 Personnel POST/PUT 必须稳定拒绝客户端回写。
    private static async Task AssertCanonicalDisplayCompanionWriteRejectedAsync(
        HttpClient client,
        HttpMethod method,
        string path,
        IReadOnlyDictionary<string, object?> payload,
        string check,
        ICollection<object> checks,
        CancellationToken cancellationToken)
    {
        const string expectedCode = "StandardCrud.DisplayValueFieldReadOnly";
        const string expectedReasonKey = "standardCrud.displayValueFieldReadOnly";
        const string expectedField = "CompanyId_DisplayValue";
        const string expectedMessage = "标准 CRUD 显示字段 CompanyId_DisplayValue 只允许输出，不能提交写入。";
        using var response = await SendJsonWithAntiforgeryAsync(
            client,
            method,
            path,
            payload,
            cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        EnsureStatus(response, HttpStatusCode.BadRequest, body, check);
        using var json = JsonDocument.Parse(body);
        var code = ReadString(json.RootElement, "code");
        var reasonKey = ReadString(json.RootElement, "reasonKey");
        var message = ReadString(json.RootElement, "message");
        var fieldErrors = ReadArray(json.RootElement, "fieldErrors");
        if (!string.Equals(code, expectedCode, StringComparison.Ordinal)
            || !string.Equals(reasonKey, expectedReasonKey, StringComparison.Ordinal)
            || !string.Equals(message, expectedMessage, StringComparison.Ordinal)
            || fieldErrors.GetArrayLength() != 1)
        {
            throw new InvalidOperationException($"{check} did not return the frozen standard error contract.");
        }

        var fieldError = fieldErrors[0];
        if (!string.Equals(ReadString(fieldError, "field"), expectedField, StringComparison.Ordinal)
            || !string.Equals(ReadString(fieldError, "code"), expectedCode, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{check} did not identify the submitted companion field.");
        }

        checks.Add(new { check, status = 400, code, reasonKey, field = expectedField });
    }

    // 被拒绝的 PUT 必须保持此前详情不变，随后才能用同一 revision 证明合法 raw PUT 仍可成功。
    private static async Task AssertPersonMutationStateAsync(
        HttpClient client,
        string personId,
        string expectedName,
        string expectedCompanyId,
        string expectedCompanyDisplayValue,
        long expectedRevision,
        CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(
            $"/api/organization/persons/{Uri.EscapeDataString(personId)}",
            cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        EnsureStatus(response, HttpStatusCode.OK, body, "detail after rejected canonical display companion update");
        using var json = JsonDocument.Parse(body);
        var fields = ReadObject(json.RootElement, "fields");
        if (!string.Equals(ReadString(fields, "ENAME"), expectedName, StringComparison.Ordinal)
            || !string.Equals(ReadString(fields, "CompanyId"), expectedCompanyId, StringComparison.Ordinal)
            || !string.Equals(
                ReadString(fields, "CompanyId_DisplayValue"),
                expectedCompanyDisplayValue,
                StringComparison.Ordinal)
            || ReadLong(fields, "Revision") != expectedRevision)
        {
            throw new InvalidOperationException("Rejected canonical display companion update changed the Personnel detail state.");
        }
    }

    private static async Task PutCompanyAsync(
        HttpClient client,
        string id,
        string name,
        bool enabled,
        CancellationToken cancellationToken)
    {
        using var response = await SendJsonWithAntiforgeryAsync(
            client,
            HttpMethod.Put,
            $"/api/organization/companies/{Uri.EscapeDataString(id)}",
            new
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
        using var response = await SendJsonWithAntiforgeryAsync(
            client,
            HttpMethod.Put,
            $"/api/organization/departments/{Uri.EscapeDataString(id)}",
            new
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

    private static async Task<HttpResponseMessage> SendWithAntiforgeryAsync(
        HttpClient client,
        HttpMethod method,
        string path,
        CancellationToken cancellationToken)
    {
        using var tokenResponse = await client.GetAsync("/api/auth/antiforgery-token", cancellationToken);
        var tokenBody = await tokenResponse.Content.ReadAsStringAsync(cancellationToken);
        EnsureStatus(tokenResponse, HttpStatusCode.OK, tokenBody, "issue antiforgery token");
        using var tokenJson = JsonDocument.Parse(tokenBody);
        using var request = new HttpRequestMessage(method, path);
        request.Headers.TryAddWithoutValidation("X-CSRF-TOKEN", ReadString(tokenJson.RootElement, "requestToken"));
        return await client.SendAsync(request, cancellationToken);
    }

    private static async Task<PersonData> CreatePersonAsync(
        HttpClient client,
        string personNo,
        string name,
        string? companyId,
        string? departmentId,
        CancellationToken cancellationToken,
        string? expectedCompanyDisplayValue = null,
        string? birthDate = null,
        string? employmentStartedAt = null,
        string? employmentEndedAt = null)
    {
        var payload = PersonPayload(personNo, name, companyId, departmentId);
        payload["BirthDate"] = birthDate;
        payload["EmploymentStartedAt"] = employmentStartedAt;
        payload["EmploymentEndedAt"] = employmentEndedAt;
        using var response = await SendJsonWithAntiforgeryAsync(
            client,
            HttpMethod.Post,
            "/api/organization/persons",
            payload,
            cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        EnsureStatus(response, HttpStatusCode.Created, body, $"create person {personNo}");
        if (expectedCompanyDisplayValue is not null)
        {
            AssertPersonCompanyDisplayEnvelope(body, companyId, expectedCompanyDisplayValue);
        }
        return ReadPerson(body, personNo, name, $"create person {personNo}");
    }

    private static void AssertPersonCompanyDisplayEnvelope(
        string body,
        string? expectedCompanyId,
        string expectedCompanyDisplayValue)
    {
        if (string.IsNullOrWhiteSpace(expectedCompanyId))
        {
            throw new InvalidOperationException("Personnel company display envelope requires a raw CompanyId expectation.");
        }

        using var json = JsonDocument.Parse(body);
        var fields = ReadObject(json.RootElement, "fields");
        if (!string.Equals(ReadString(fields, "CompanyId"), expectedCompanyId, StringComparison.Ordinal)
            || !string.Equals(
                ReadString(fields, "CompanyId_DisplayValue"),
                expectedCompanyDisplayValue,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Personnel create detail did not preserve the server-owned company raw/display envelope.");
        }
    }

    private static PersonData ReadPerson(string body, string personNo, string name, string operation)
    {
        using var json = JsonDocument.Parse(body);
        var fields = ReadObject(json.RootElement, "fields");
        var auditTimestamps = ReadPersonAuditTimestamps(fields, operation);
        return new PersonData(
            ReadString(fields, "EID"),
            personNo,
            name,
            ReadLong(fields, "Revision"),
            auditTimestamps.CreatedAt,
            auditTimestamps.UpdatedAt);
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

    private static void AssertPersonBody(
        string body,
        PersonData person,
        FixtureData fixture,
        string operation)
    {
        AssertPersonCompanyDisplayEnvelope(body, fixture.CompanyId, fixture.CompanyName);
        using var json = JsonDocument.Parse(body);
        var fields = ReadObject(json.RootElement, "fields");
        AssertPersonAuditTimestamps(fields, person, operation);
        foreach (var (field, expected) in new[]
                 {
                     ("BirthDate", PersonnelBirthDate),
                     ("BirthDate_DisplayValue", PersonnelBirthDateDisplay),
                     ("EmploymentStartedAt", PersonnelStartedAtUtc),
                     ("EmploymentStartedAt_DisplayValue", PersonnelStartedAtDisplay),
                     ("EmploymentEndedAt", PersonnelEndedAtUtc),
                     ("EmploymentEndedAt_DisplayValue", PersonnelEndedAtDisplay)
                 })
        {
            if (!string.Equals(ReadString(fields, field), expected, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"{operation} returned an unexpected {field} value.");
            }
        }
        foreach (var expected in new[] { person.Id, person.PersonNo, person.Name, fixture.CompanyName, fixture.DepartmentName })
        {
            if (!body.Contains(expected, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Personnel detail omitted expected value: {expected}");
            }
        }
    }

    private static void AssertPersonAuditTimestamps(JsonElement fields, PersonData person, string operation)
    {
        var auditTimestamps = ReadPersonAuditTimestamps(fields, operation);
        if (!string.Equals(auditTimestamps.CreatedAt, person.CreatedAt, StringComparison.Ordinal)
            || !string.Equals(auditTimestamps.UpdatedAt, person.UpdatedAt, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{operation} changed the persisted Personnel audit timestamps.");
        }
    }

    private static PersonAuditTimestamps ReadPersonAuditTimestamps(JsonElement fields, string operation) =>
        new(
            ReadUtcAuditString(fields, "CreatedAt", operation),
            ReadUtcAuditString(fields, "UpdatedAt", operation));

    private static string ReadUtcAuditString(JsonElement fields, string field, string operation)
    {
        var value = ReadString(fields, field);
        if (!value.EndsWith("Z", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{operation} returned {field} without the canonical UTC Z suffix.");
        }

        return value;
    }

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

    // 只接受明确 JSON boolean，避免验收把字符串或缺失字段误判为业务状态。
    private static bool ReadBoolean(JsonElement element, string property)
    {
        foreach (var item in element.EnumerateObject())
        {
            if (string.Equals(item.Name, property, StringComparison.OrdinalIgnoreCase)
                && item.Value.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                return item.Value.GetBoolean();
            }
        }
        throw new InvalidOperationException($"JSON boolean {property} was missing.");
    }

    private static int ReadInt(JsonElement element, string property)
    {
        foreach (var item in element.EnumerateObject())
        {
            if (string.Equals(item.Name, property, StringComparison.OrdinalIgnoreCase)
                && item.Value.TryGetInt32(out var value))
            {
                return value;
            }
        }
        throw new InvalidOperationException($"JSON integer {property} was missing.");
    }

    private static JsonElement ReadArray(JsonElement element, string property)
    {
        foreach (var item in element.EnumerateObject())
        {
            if (string.Equals(item.Name, property, StringComparison.OrdinalIgnoreCase)
                && item.Value.ValueKind == JsonValueKind.Array)
            {
                return item.Value;
            }
        }
        throw new InvalidOperationException($"JSON array {property} was missing.");
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

    private static long ReadNestedLong(JsonElement element, string parent, string property)
    {
        foreach (var item in element.EnumerateObject())
        {
            if (string.Equals(item.Name, parent, StringComparison.OrdinalIgnoreCase))
            {
                return ReadLong(item.Value, property);
            }
        }
        throw new InvalidOperationException($"JSON property {parent}.{property} was missing.");
    }

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

    private static void AssertContains(string actual, string expected, string label)
    {
        if (!actual.Contains(expected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Browser assertion failed for {label}.");
        }
    }

    // 导出观察必须读取真实工作表内容，不能把 HTTP 200 与非空 zip 当成 display 值已生效。
    private static string ReadXlsxSheetXml(byte[] bytes, string mode)
    {
        using var stream = new MemoryStream(bytes);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var entry = archive.GetEntry("xl/worksheets/sheet1.xml")
            ?? throw new InvalidOperationException($"Personnel {mode} export omitted the first worksheet.");
        using var entryStream = entry.Open();
        using var reader = new StreamReader(entryStream, Encoding.UTF8);
        var sheetXml = reader.ReadToEnd();
        return !string.IsNullOrWhiteSpace(sheetXml)
            ? sheetXml
            : throw new InvalidOperationException($"Personnel {mode} export first worksheet was empty.");
    }

    private static void AssertDoesNotContain(string actual, string unexpected, string label)
    {
        if (actual.Contains(unexpected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Acceptance assertion failed for {label}.");
        }
    }

    private sealed record FixtureData(
        string RunId,
        string AdminUserId,
        string ReadOnlyUserId,
        string CompanyId,
        string CompanyName,
        string DepartmentId,
        string DepartmentName);
    private sealed record PersonData(
        string Id,
        string PersonNo,
        string Name,
        long Revision,
        string CreatedAt,
        string UpdatedAt);
    private sealed record PositionData(string Name);
    private sealed record PersonAuditTimestamps(string CreatedAt, string UpdatedAt);
    private sealed record EntityId(string Id);
    private sealed record DatabaseOwnershipSnapshot(
        int Version,
        string RepositoryId,
        string WorktreeId,
        string Provider,
        string ServerFingerprint,
        string CatalogFingerprint);
}
