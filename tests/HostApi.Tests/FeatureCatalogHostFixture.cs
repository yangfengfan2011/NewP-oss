using ICP.Platform.Testing;
using System.Diagnostics;
using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Nodes;
using ICP.Platform.Authentication.Contracts;
using ICP.Platform.Crud.ObjectIds;
using ICP.Platform.HostApi.Antiforgery;
using ICP.Platform.HostApi.Controllers;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Modules.Authorization.Persistence.Descriptors;
using ICP.Platform.Modules.Authorization.Services;
using ICP.Platform.Modules.Identity.Application;
using ICP.Platform.Modules.Identity.Models;
using ICP.Platform.Modules.Identity.Persistence.Descriptors;
using ICP.Platform.Modules.Identity.Persistence.Ef;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Persistence.EfCore.Projection;
using ICP.Platform.Persistence.EfCore.Transactions;
using ICP.Platform.Testing.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

// 只在一次性发行副本修改真实Example声明；Host仍执行正式编译、migration、runtime staging及目录同步。
internal sealed class FeatureCatalogHostFixture : IAsyncDisposable
{
    private readonly ReusableSqlServerDatabase _databaseLease;
    private readonly string _runId = "NewPFeature205_" + Guid.NewGuid().ToString("N");
    private readonly string _temporaryRoot;
    private Process? _host;
    private Task[] _captures = [];
    private int _phase;
    private int _ownedHostProcessId;
    private readonly HashSet<string> _databaseTriggers = new(StringComparer.Ordinal);
    internal string RepositoryRoot { get; }
    internal string EvidenceRoot { get; }
    internal string HostRoot => Path.Combine(_temporaryRoot, "host");
    internal string PluginRoot => Path.Combine(_temporaryRoot, "plugins");
    internal string StorageRoot => Path.Combine(_temporaryRoot, "storage");
    internal string HostUrl { get; }
    internal int OwnedHostProcessId => _ownedHostProcessId;
    internal string ConnectionString { get; }
    internal string AdministratorPassword { get; } = "Feature-205-" + Guid.NewGuid().ToString("N") + "!";
    internal HttpClient Administrator { get; private set; } = null!;
    internal IReadOnlyList<FeaturePoint> Catalog { get; private set; } = [];
    internal string CapsulePath => Path.Combine(Path.Combine(HostRoot, "modules", "Example"), "plugin.json");
    internal string LastError { get; private set; } = "";
    internal bool ExternalPluginsEnabled { get; set; }

    private FeatureCatalogHostFixture(ReusableSqlServerDatabase databaseLease)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, ".git"))) directory = directory.Parent;
        RepositoryRoot = directory?.FullName ?? throw new InvalidOperationException("Repository root missing.");
        EvidenceRoot = Path.Combine(RepositoryRoot, ".codex-temp", "issue205", _runId);
        Directory.CreateDirectory(EvidenceRoot);
        _temporaryRoot = Path.Combine(EvidenceRoot, "workspace");
        _databaseLease = databaseLease;
        ConnectionString = databaseLease.ConnectionString;
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        HostUrl = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}";
    }

    internal static async Task<FeatureCatalogHostFixture> CreateAsync(JsonArray declarations,
        Func<FeatureCatalogHostFixture, Task>? beforeFirstStartup = null, bool expectInitialStartupFailure = false)
    {
        var lease = await ReusableSqlServerDatabase.AcquireAsync();
        FeatureCatalogHostFixture fixture;
        try { fixture = new FeatureCatalogHostFixture(lease); }
        catch { await lease.DisposeAsync(); throw; }
        try
        {
            var output = new DirectoryInfo(AppContext.BaseDirectory);
            var source = Path.Combine(fixture.RepositoryRoot, "apps", "host-api", "bin", output.Parent!.Name, output.Name);
            foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                var target = Path.Combine(fixture.HostRoot, Path.GetRelativePath(source, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target);
            }
            File.Copy(Path.Combine(ICP.Platform.Modeling.Metadata.ModuleAuthoringPaths.ResolveModuleRoot(fixture.RepositoryRoot, "Example"), "newp.plugin.json"), fixture.CapsulePath, overwrite: true);
            fixture.SetDeclarations(declarations);
            if (beforeFirstStartup is not null) await beforeFirstStartup(fixture);
            await fixture.StartAsync(expectFailure: expectInitialStartupFailure);
            if (expectInitialStartupFailure) return fixture;
            await fixture.StopAsync();
            await fixture.BootstrapAsync();
            await fixture.StartAsync();
            fixture.Administrator = await fixture.LoginAsync("admin", fixture.AdministratorPassword);
            var current = await fixture.Administrator.GetFromJsonAsync<JsonElement>("/api/auth/current-user");
            using var passwordChange = await fixture.Administrator.PostAsJsonAsync("/api/identity/me/password-change", new
            {
                CurrentPassword = fixture.AdministratorPassword,
                NewPassword = fixture.AdministratorPassword + "-ready",
                ExpectedRevision = current.GetProperty("revision").GetInt64()
            });
            Assert.Equal(HttpStatusCode.OK, passwordChange.StatusCode);
            await BusinessAuditHostFixture.RefreshAntiforgeryAsync(fixture.Administrator);
            return fixture;
        }
        catch { await fixture.DisposeAsync(); throw; }
    }

    internal void SetDeclarations(JsonArray declarations)
    {
        var capsule = JsonNode.Parse(File.ReadAllText(CapsulePath))!;
        capsule["features"] = declarations.DeepClone();
        File.WriteAllText(CapsulePath, capsule.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    internal async Task StartAsync(bool expectFailure = false)
    {
        Assert.Null(_host);
        var start = new ProcessStartInfo("dotnet") { WorkingDirectory = HostRoot, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(Path.Combine(HostRoot, "HostApi.dll"));
        start.ArgumentList.Add("--urls"); start.ArgumentList.Add(HostUrl);
        start.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        start.Environment["ConnectionStrings__SqlServer"] = ConnectionString;
        start.Environment["Platform__PluginInstallRoot"] = PluginRoot;
        start.Environment["Platform__ExternalPluginsEnabled"] = ExternalPluginsEnabled ? "true" : "false";
        start.Environment["Files__StorageRoot"] = StorageRoot;
        start.Environment["Authentication__Host__KeyRingPath"] = Path.Combine(_temporaryRoot, "keys");
        start.Environment["Authentication__Host__AllowInsecureDevelopmentCookie"] = "true";
        start.Environment["Identity__Credentials__UseSharedDevelopmentCredential"] = "false";
        start.Environment["Logging__LogLevel__Default"] = "Warning";
        _host = Process.Start(start) ?? throw new InvalidOperationException("Host failed to start.");
        _ownedHostProcessId = _host.Id;
        var phase = ++_phase;
        _captures = [CaptureAsync(_host.StandardOutput, $"host-{phase}.stdout.log"), CaptureAsync(_host.StandardError, $"host-{phase}.stderr.log")];
        using var client = new HttpClient { BaseAddress = new Uri(HostUrl), Timeout = TimeSpan.FromSeconds(2) };
        var deadline = DateTime.UtcNow.AddSeconds(90);
        while (DateTime.UtcNow < deadline)
        {
            if (_host.HasExited)
            {
                var exit = _host.ExitCode;
                await StopAsync();
                LastError = await File.ReadAllTextAsync(Path.Combine(EvidenceRoot, $"host-{phase}.stderr.log"));
                if (expectFailure && exit != 0) return;
                throw new InvalidOperationException($"Host exited {exit}: {LastError}");
            }
            try
            {
                if ((await client.GetAsync("/api/health")).IsSuccessStatusCode)
                {
                    Assert.False(expectFailure, "Invalid candidate opened HTTP requests.");
                    using var manifest = await client.GetFromJsonAsync<JsonDocument>("/plugins/metadata-runtime-manifest.json");
                    Catalog = RuntimeFeatureCatalogTestSupport.Parse(manifest!.RootElement);
                    return;
                }
            }
            catch (HttpRequestException) { }
            catch (TaskCanceledException) { }
            await Task.Delay(150);
        }
        throw new TimeoutException("Host startup exceeded 90 seconds.");
    }

    // 仅窄读本夹具所属 Host 在当前 catalog 的会话/请求，不记录 SQL 原文、参数或其他进程。
    internal Task<JsonElement> ReadOwnedHostDatabaseStageAsync()
    {
        var ownedHost = _host;
        bool exited;
        try { exited = ownedHost is null || ownedHost.HasExited; }
        catch (InvalidOperationException) { exited = true; }
        return ReadDatabaseStageAsync(ConnectionString, OwnedHostProcessId, exited);
    }

    // 最低层 SQL 探针复用正式采样的同一条 SQL、列转换与空结果行为。
    internal static async Task<JsonElement> ReadDatabaseStageAsync(string connectionString, int ownedProcessId, bool exited)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT CAST(DB_ID() AS int);
            SELECT CAST(s.session_id AS int) AS sessionId, CAST(s.database_id AS int) AS databaseId, s.status AS sessionStatus,
                   COALESCE(r.command, N'') AS command, COALESCE(r.status, N'') AS requestStatus,
                   COALESCE(r.wait_type, N'') AS waitType, COALESCE(r.wait_time, 0) AS waitMilliseconds,
                   COALESCE(r.blocking_session_id, 0) AS blockingSessionId,
                   CAST(CASE WHEN t.text LIKE N'%sp_getapplock%' THEN 1 ELSE 0 END AS bit) AS isAppLockRequest,
                   CAST(CASE WHEN t.text LIKE N'%__NewPMigrationHistory%' THEN 1 ELSE 0 END AS bit) AS isHistoryRequest
            FROM sys.dm_exec_sessions AS s
            LEFT JOIN sys.dm_exec_requests AS r ON r.session_id = s.session_id
            OUTER APPLY sys.dm_exec_sql_text(r.sql_handle) AS t
            WHERE s.host_process_id = @ownedProcessId AND s.is_user_process = 1 AND s.database_id = DB_ID()
            ORDER BY s.session_id;
            """;
        command.Parameters.AddWithValue("@ownedProcessId", ownedProcessId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        var databaseId = reader.GetInt32(0);
        Assert.True(await reader.NextResultAsync());
        var columns = reader.GetColumnSchema().Select(column => new
        {
            column.ColumnName, column.DataTypeName, clrType = column.DataType?.FullName, column.AllowDBNull
        }).ToArray();
        var sessions = new List<object>();
        while (await reader.ReadAsync())
        {
            sessions.Add(new
            {
                sessionId = reader.GetInt32(0),
                databaseId = reader.GetInt32(1),
                sessionStatus = reader.GetString(2),
                command = reader.GetString(3),
                requestStatus = reader.GetString(4),
                waitType = reader.GetString(5),
                waitMilliseconds = reader.GetInt32(6),
                blockingSessionId = reader.GetInt32(7),
                isAppLockRequest = reader.GetBoolean(8),
                isHistoryRequest = reader.GetBoolean(9)
            });
        }
        return JsonSerializer.SerializeToElement(new

        {
            processId = ownedProcessId,
            exited,
            catalog = connection.Database,
            databaseId,
            columns,
            sessions
        });
    }

    // 诊断不能覆盖原 checkpoint 异常；同时留下 xUnit Data 之外可读取的私有异常栈。
    internal async Task RecordCheckpointDiagnosticFailureAsync(Exception checkpointFailure, Exception diagnosticFailure)
    {
        checkpointFailure.Data["CheckpointDiagnosticFailure"] = diagnosticFailure;
        try
        {
            await File.WriteAllTextAsync(Path.Combine(EvidenceRoot, "checkpoint-diagnostic-error.json"),
                JsonSerializer.Serialize(new { OwnedHostProcessId, diagnostic = diagnosticFailure.ToString() }));
        }
        catch (Exception dumpFailure) { checkpointFailure.Data["CheckpointDiagnosticDumpFailure"] = dumpFailure; }
    }

    internal async Task StopAsync()
    {
        if (_host is null) return;
        if (!_host.HasExited) _host.Kill(entireProcessTree: false);
        await _host.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
        await Task.WhenAll(_captures).WaitAsync(TimeSpan.FromSeconds(15));
        _host.Dispose(); _host = null;
    }

    // 只中断本夹具持有的 Host 树；StartAsync 随后按真实退出收尾，不依赖 Host catch 发布资产。
    internal async Task InterruptStartupAsync()
    {
        var host = _host ?? throw new InvalidOperationException("Fixture Host is not running.");
        if (!host.HasExited) host.Kill(entireProcessTree: true);
        await host.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
    }

    // 登记在 Create 前，但拒绝已存在的名字；即使创建回调中途抛错，也只归还本场景拥有的 trigger。
    internal async Task CreateDatabaseTriggerAsync(string name, string sql)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            IF DB_NAME() <> N'NewP_Test' OR NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=0
                AND name=N'NewP.AutomatedTests' AND CONVERT(nvarchar(128),value)=N'disposable-schema')
                THROW 51000,N'Expected disposable test catalog missing.',1;
            SELECT COUNT(*) FROM sys.triggers WHERE parent_class=0 AND name=@name;
            """;
        command.Parameters.AddWithValue("@name", name);
        if (Convert.ToInt32(await command.ExecuteScalarAsync()) != 0)
            throw new InvalidOperationException("Fixture trigger name already exists: " + name);
        _databaseTriggers.Add(name);
        command.Parameters.Clear();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    internal async Task<HttpClient> LoginAsync(string code, string password)
    {
        var client = new HttpClient(new HttpClientHandler { CookieContainer = new CookieContainer(), UseCookies = true }) { BaseAddress = new Uri(HostUrl) };
        await BusinessAuditHostFixture.RefreshAntiforgeryAsync(client);
        using var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(code, password));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await BusinessAuditHostFixture.RefreshAntiforgeryAsync(client);
        return client;
    }

    internal EfPlatformDbContext CreateContext() => new(new DbContextOptionsBuilder<EfPlatformDbContext>()
        .UseSqlServer(ConnectionString).ReplaceService<IModelCacheKeyFactory, EfPlatformModelCacheKeyFactory>().Options,
        IdentityModelDefinitions.All.Concat(AuthorizationModelDefinitions.All).ToArray());

    private async Task BootstrapAsync()
    {
        await using var db = CreateContext();
        var ids = new FixtureIds();
        var service = new IdentityLocalAdministratorMaintenanceService(new EfIdentityRepository(db), new EfPlatformTransactionExecutor(db),
            new NoOpSessions(), new EfLocalAdministratorRoleAssignment(db, ids), new PasswordHasher<IdentityAccount>(),
            new FixtureCredential(AdministratorPassword), ids, TimeProvider.System);
        await service.BootstrapAsync(new("admin", "功能目录管理员", "isolated lifecycle setup",
            new(IdentityMaintenanceActors.LocalMaintenance, IdentitySecurityActorKind.LocalMaintenance, Guid.NewGuid().ToString("N"))));
    }

    private async Task CaptureAsync(StreamReader reader, string file)
    {
        var text = (await reader.ReadToEndAsync()).Replace(ConnectionString, "[connection]").Replace(AdministratorPassword, "[credential]");
        await File.WriteAllTextAsync(Path.Combine(EvidenceRoot, file), text);
    }

    public async ValueTask DisposeAsync()
    {
        try { await StopAsync(); }
        finally
        {
            Administrator?.Dispose();
            try { await _databaseLease.CleanupDatabaseTriggersAsync(_databaseTriggers); }
            finally
            {
                await _databaseLease.DisposeAsync();
                if (Directory.Exists(_temporaryRoot)) Directory.Delete(_temporaryRoot, recursive: true);
            }
        }
    }

    private sealed class FixtureCredential(string value) : ITemporaryCredentialIssuer { public string Issue() => value; }
    internal sealed class FixtureIds : IPlatformObjectIdGenerator { public string NewId(string name) => name + "_" + Guid.NewGuid().ToString("N"); }
    private sealed class NoOpSessions : IAuthenticationSessionRegistry
    {
        public ValueTask<AuthenticationSessionIssuance> RegisterAsync(AuthenticationSessionRegistration value, CancellationToken token = default) => throw new NotSupportedException();
        public ValueTask<AuthenticationSessionValidation> ValidateAsync(string value, CancellationToken token = default) => throw new NotSupportedException();
        public ValueTask<bool> RevokeCurrentAsync(AuthenticationSessionRevocation value, CancellationToken token = default) => throw new NotSupportedException();
        public ValueTask<int> RevokeAllAsync(AccountAuthenticationSessionsRevocation value, CancellationToken token = default) => ValueTask.FromResult(0);
    }
}
