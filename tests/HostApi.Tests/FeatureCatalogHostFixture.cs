using System.Diagnostics;
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
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

// 只在一次性发行副本修改真实Example声明；Host仍执行正式编译、migration、runtime staging及目录同步。
internal sealed class FeatureCatalogHostFixture : IAsyncDisposable
{
    private readonly string _adminConnection;
    private readonly string _database = "NewPFeature205_" + Guid.NewGuid().ToString("N");
    private readonly string _temporaryRoot;
    private Process? _host;
    private Task[] _captures = [];
    private int _phase;
    internal string RepositoryRoot { get; }
    internal string EvidenceRoot { get; }
    internal string HostRoot => Path.Combine(_temporaryRoot, "host");
    internal string PluginRoot => Path.Combine(_temporaryRoot, "plugins");
    internal string HostUrl { get; }
    internal string ConnectionString { get; }
    internal string AdministratorPassword { get; } = "Feature-205-" + Guid.NewGuid().ToString("N") + "!";
    internal HttpClient Administrator { get; private set; } = null!;
    internal IReadOnlyList<FeaturePoint> Catalog { get; private set; } = [];
    internal string CapsulePath => Path.Combine(HostRoot, "modules", "Example", "plugin.json");
    internal string LastError { get; private set; } = "";

    private FeatureCatalogHostFixture()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, ".git"))) directory = directory.Parent;
        RepositoryRoot = directory?.FullName ?? throw new InvalidOperationException("Repository root missing.");
        EvidenceRoot = Path.Combine(RepositoryRoot, ".codex-temp", "issue205", _database);
        Directory.CreateDirectory(EvidenceRoot);
        _temporaryRoot = Path.Combine(EvidenceRoot, "workspace");
        _adminConnection = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("NEWP_ORGANIZATION_SQLSERVER_ADMIN_CONNECTION")
            ?? "Server=(localdb)\\MSSQLLocalDB;Database=master;Integrated Security=true;TrustServerCertificate=true") { InitialCatalog = "master" }.ConnectionString;
        ConnectionString = new SqlConnectionStringBuilder(_adminConnection) { InitialCatalog = _database }.ConnectionString;
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        HostUrl = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}";
    }

    internal static async Task<FeatureCatalogHostFixture> CreateAsync(JsonArray declarations)
    {
        var fixture = new FeatureCatalogHostFixture();
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
            File.Copy(Path.Combine(fixture.RepositoryRoot, "modules", "Example", "newp.plugin.json"), fixture.CapsulePath, overwrite: true);
            fixture.SetDeclarations(declarations);
            await fixture.ExecuteAdminAsync($"CREATE DATABASE [{fixture._database}]");
            await fixture.StartAsync();
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
        start.Environment["Platform__ExternalPluginsEnabled"] = "false";
        start.Environment["Authentication__Host__KeyRingPath"] = Path.Combine(_temporaryRoot, "keys");
        start.Environment["Authentication__Host__AllowInsecureDevelopmentCookie"] = "true";
        start.Environment["Identity__Credentials__UseSharedDevelopmentCredential"] = "false";
        start.Environment["Logging__LogLevel__Default"] = "Warning";
        _host = Process.Start(start) ?? throw new InvalidOperationException("Host failed to start.");
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
                    Catalog = manifest!.RootElement.GetProperty("modules").EnumerateArray().SelectMany(module =>
                        module.GetProperty("features").Deserialize<FeaturePoint[]>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!).ToArray();
                    return;
                }
            }
            catch (HttpRequestException) { }
            catch (TaskCanceledException) { }
            await Task.Delay(150);
        }
        throw new TimeoutException("Host startup exceeded 90 seconds.");
    }

    internal async Task StopAsync()
    {
        if (_host is null) return;
        if (!_host.HasExited) _host.Kill(entireProcessTree: false);
        await _host.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
        await Task.WhenAll(_captures).WaitAsync(TimeSpan.FromSeconds(15));
        _host.Dispose(); _host = null;
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

    private async Task ExecuteAdminAsync(string sql)
    {
        await using var connection = new SqlConnection(_adminConnection);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
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
            SqlConnection.ClearAllPools();
            await ExecuteAdminAsync($"IF DB_ID(N'{_database}') IS NOT NULL BEGIN ALTER DATABASE [{_database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_database}]; END");
            if (Directory.Exists(_temporaryRoot)) Directory.Delete(_temporaryRoot, recursive: true);
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
