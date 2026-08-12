using System.Diagnostics;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using ICP.Platform.Authentication.Contracts;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Crud.ObjectIds;
using ICP.Platform.HostApi.Controllers;
using ICP.Platform.HostApi.Antiforgery;
using ICP.Platform.Identity.Contracts;
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

public sealed class HostAuthenticationProcessRestartTests
{
    private const string RunEnvironmentVariable = "NEWP_RUN_HOST_AUTH_RESTART_INTEGRATION";
    private static readonly ConcurrentDictionary<int, Task[]> CaptureTasks = new();
    private static readonly ConcurrentDictionary<int, string[]> LogPaths = new();

    [Fact]
    [Trait("Category", "SqlServerSmoke")]
    public async Task RealHostProcessRestart_ReusesDatabaseSessionAndProtectedCookie()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable(RunEnvironmentVariable), "1", StringComparison.Ordinal))
        {
            return;
        }

        var catalog = $"NewPAuth35_{Guid.NewGuid():N}";
        var root = Path.Combine(Path.GetTempPath(), $"newp-auth35-{Guid.NewGuid():N}");
        var keyRing = Path.Combine(root, "key-ring");
        var plugins = Path.Combine(root, "plugins");
        Directory.CreateDirectory(keyRing);
        Directory.CreateDirectory(plugins);
        var adminConnectionString = "Server=localhost;Database=master;Integrated Security=true;TrustServerCertificate=true";
        var applicationConnectionString = new SqlConnectionStringBuilder(adminConnectionString)
        {
            InitialCatalog = catalog
        }.ConnectionString;
        var port = ReservePort();
        Process? process = null;

        await CreateCatalogAsync(adminConnectionString, catalog);
        try
        {
            process = StartHost(root, keyRing, plugins, applicationConnectionString, port, "migration");
            await WaitForHostAsync(process, port);
            StopExactProcess(process);
            process = null;

            await BootstrapAdministratorAsync(applicationConnectionString);

            using var handler = new HttpClientHandler
            {
                CookieContainer = new CookieContainer(),
                UseCookies = true
            };
            using var client = new HttpClient(handler)
            {
                BaseAddress = new Uri($"http://127.0.0.1:{port}"),
                Timeout = TimeSpan.FromSeconds(10)
            };
            process = StartHost(root, keyRing, plugins, applicationConnectionString, port, "authenticated");
            await WaitForHostAsync(process, port);
            var tokenResponse = await client.GetFromJsonAsync<AntiforgeryTokenResponse>(HostAntiforgeryDefaults.TokenPath)
                ?? throw new InvalidOperationException("Antiforgery token response was empty.");
            using var loginRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
            {
                Content = JsonContent.Create(new LoginRequest("admin", "restart-secret-123"))
            };
            loginRequest.Headers.TryAddWithoutValidation(HostAntiforgeryDefaults.HeaderName, tokenResponse.RequestToken);
            var login = await client.SendAsync(loginRequest);
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
            var beforeRestart = await client.GetAsync("/api/auth/current-user");
            Assert.Equal(HttpStatusCode.OK, beforeRestart.StatusCode);
            StopExactProcess(process);
            process = null;

            process = StartHost(root, keyRing, plugins, applicationConnectionString, port, "restarted");
            await WaitForHostAsync(process, port);
            var afterRestart = await client.GetAsync("/api/auth/current-user");

            Assert.Equal(HttpStatusCode.OK, afterRestart.StatusCode);
            Assert.Contains("admin", await afterRestart.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
            Assert.NotEmpty(Directory.GetFiles(keyRing, "key-*.xml"));
        }
        finally
        {
            if (process is not null)
            {
                StopExactProcess(process);
            }

            SqlConnection.ClearAllPools();
            await DropCatalogAsync(adminConnectionString, catalog);
            Directory.Delete(root, recursive: true);
        }
    }

    private static Process StartHost(
        string root,
        string keyRing,
        string plugins,
        string connectionString,
        int port,
        string phase)
    {
        var hostAssembly = Path.Combine(FindRepositoryRoot(), "apps", "host-api", "bin", "Debug", "net10.0", "HostApi.dll");
        var startInfo = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(hostAssembly)!
        };
        startInfo.ArgumentList.Add(hostAssembly);
        startInfo.ArgumentList.Add("--urls");
        startInfo.ArgumentList.Add($"http://127.0.0.1:{port}");
        startInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        startInfo.Environment["ConnectionStrings__SqlServer"] = connectionString;
        startInfo.Environment["Platform__PluginInstallRoot"] = plugins;
        startInfo.Environment["Platform__ExternalPluginsEnabled"] = "false";
        startInfo.Environment["Authentication__Host__KeyRingPath"] = keyRing;
        startInfo.Environment["Authentication__Host__CookieName"] = "ICP.Platform.Auth";
        startInfo.Environment["Authentication__Host__AllowInsecureDevelopmentCookie"] = "true";
        startInfo.Environment["Identity__Credentials__UseSharedDevelopmentCredential"] = "false";
        startInfo.Environment["DOTNET_NOLOGO"] = "1";
        startInfo.Environment["Logging__LogLevel__Default"] = "Warning";
        var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Host process did not start.");
        var stdoutPath = Path.Combine(root, $"host-{phase}.stdout.log");
        var stderrPath = Path.Combine(root, $"host-{phase}.stderr.log");
        LogPaths[process.Id] = [stdoutPath, stderrPath];
        CaptureTasks[process.Id] =
        [
            CaptureAsync(process.StandardOutput, stdoutPath),
            CaptureAsync(process.StandardError, stderrPath)
        ];
        return process;
    }

    private static string FindRepositoryRoot()
    {
        var candidate = new DirectoryInfo(AppContext.BaseDirectory);
        while (candidate is not null && !File.Exists(Path.Combine(candidate.FullName, "ICP-Platform.sln")))
        {
            candidate = candidate.Parent;
        }

        return candidate?.FullName
            ?? throw new DirectoryNotFoundException("Repository root was not found from the test output directory.");
    }

    private static async Task WaitForHostAsync(Process process, int port)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (DateTime.UtcNow < deadline)
        {
            if (process.HasExited)
            {
                WaitForCapture(process.Id);
                var diagnostics = LogPaths.TryGetValue(process.Id, out var paths)
                    ? string.Join(Environment.NewLine, paths.Where(File.Exists).Select(File.ReadAllText))
                    : string.Empty;
                throw new InvalidOperationException($"Host exited before listening, exit code {process.ExitCode}.{Environment.NewLine}{diagnostics}");
            }

            try
            {
                if ((await client.GetAsync($"http://127.0.0.1:{port}/api/health")).IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch (HttpRequestException)
            {
            }
            catch (TaskCanceledException)
            {
            }

            await Task.Delay(200);
        }

        throw new TimeoutException("Host did not become reachable within 60 seconds.");
    }

    private static void StopExactProcess(Process process)
    {
        if (process.HasExited)
        {
            WaitForCapture(process.Id);
            process.Dispose();
            return;
        }

        process.Kill(entireProcessTree: false);
        if (!process.WaitForExit(15000))
        {
            throw new TimeoutException($"Host PID {process.Id} did not stop within 15 seconds.");
        }

        var processId = process.Id;
        WaitForCapture(processId);
        LogPaths.TryRemove(processId, out _);
        process.Dispose();
    }

    private static void WaitForCapture(int processId)
    {
        if (CaptureTasks.TryRemove(processId, out var captures)
            && !Task.WaitAll(captures, TimeSpan.FromSeconds(15)))
        {
            throw new TimeoutException($"Host PID {processId} log capture did not finish within 15 seconds.");
        }
    }

    private static async Task BootstrapAdministratorAsync(string connectionString)
    {
        await using var context = CreateIdentityAndAuthorizationContext(connectionString);
        var repository = new EfIdentityRepository(context);
        var ids = new GuidObjectIdGenerator();
        var service = new IdentityLocalAdministratorMaintenanceService(
            repository,
            new EfPlatformTransactionExecutor(context),
            new NoOpSessionRegistry(),
            new EfLocalAdministratorRoleAssignment(context, ids),
            new PasswordHasher<User>(),
            new FixedCredentialIssuer(),
            ids,
            TimeProvider.System);
        await service.BootstrapAsync(new BootstrapLocalAdministratorCommand(
            "admin",
            "Local Administrator",
            "host restart integration setup",
            new IdentityActorContext(
                IdentityMaintenanceActors.LocalMaintenance,
                IdentitySecurityActorKind.LocalMaintenance,
                Guid.NewGuid().ToString("N"))));
    }

    private static EfPlatformDbContext CreateIdentityAndAuthorizationContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<EfPlatformDbContext>()
            .UseSqlServer(connectionString)
            .ReplaceService<IModelCacheKeyFactory, EfPlatformModelCacheKeyFactory>()
            .Options;
        return new EfPlatformDbContext(
            options,
            IdentityModelDefinitions.All.Concat(AuthorizationModelDefinitions.All).ToArray());
    }

    private static int ReservePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static async Task CaptureAsync(StreamReader reader, string path)
    {
        var content = await reader.ReadToEndAsync();
        await File.WriteAllTextAsync(path, content);
    }

    private static async Task CreateCatalogAsync(string connectionString, string catalog)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE DATABASE [{catalog}]";
        await command.ExecuteNonQueryAsync();
    }

    private static async Task DropCatalogAsync(string connectionString, string catalog)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            IF DB_ID(N'{catalog}') IS NOT NULL
            BEGIN
                ALTER DATABASE [{catalog}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                DROP DATABASE [{catalog}];
            END
            """;
        await command.ExecuteNonQueryAsync();
    }

    private sealed class FixedCredentialIssuer : ITemporaryCredentialIssuer
    {
        public string Issue() => "restart-secret-123";
    }

    private sealed class GuidObjectIdGenerator : IPlatformObjectIdGenerator
    {
        public string NewId(string objectName) => $"{objectName}_{Guid.NewGuid():N}";
    }

    private sealed class NoOpSessionRegistry : IAuthenticationSessionRegistry
    {
        public ValueTask<AuthenticationSessionIssuance> RegisterAsync(AuthenticationSessionRegistration registration, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<AuthenticationSessionValidation> ValidateAsync(string credential, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<bool> RevokeCurrentAsync(AuthenticationSessionRevocation revocation, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<int> RevokeAllAsync(UserAuthenticationSessionsRevocation revocation, CancellationToken cancellationToken = default) => ValueTask.FromResult(0);
    }
}
